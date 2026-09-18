# Concurrencia de la numeración fiscal eNCF (Fase 3)

## Problema (detectado en el Gate)

La asignación de eNCF usaba concurrencia optimista sobre la fila de la serie (`SecuenciaECF.Version`):
con contención, el perdedor de la carrera reintentaba (máximo 5) y, si no lograba un intento "limpio",
la venta fallaba con `SECUENCIA_NO_DISPONIBLE`. Integridad perfecta (nunca dos ventas con el mismo
número) pero **disponibilidad pobre**.

## Medición (SQL Server LocalDB, base temporal por corrida, repositorios y servicio reales)

Barrido 1 → 100 ventas simultáneas, tipo 32, con la implementación optimista:

| Concurrentes | Éxitos | Fallos (`SECUENCIA_NO_DISPONIBLE`) | Duplicados | Latencia total |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 1 | 0 | 0 | 1 575 ms |
| 5 | 5 | 0 | 0 | 134 ms |
| 10 | 6 | 4 | 0 | 119 ms |
| 20 | 7 | 13 | 0 | 167 ms |
| 30 | 8 | 22 | 0 | 193 ms |
| 50 | 9 | 41 | 0 | 309 ms |
| 100 | 11 | 89 | 0 | 421 ms |

**Punto de degradación: ~10 concurrentes.** La fila de la serie es un *hot row*: el reintento optimista
no espera, compite de nuevo y vuelve a perder.

## Solución elegida: Opción B — `UPDLOCK, ROWLOCK` sobre la fila de la serie

`SecuenciaECFRepository` lee la fila con `WITH (UPDLOCK, ROWLOCK)` en SQL Server: la primera venta
bloquea la fila hasta el fin de su transacción y las siguientes **esperan** su turno en el punto fiscal
en lugar de competir. La fila se libera con el rollback, así que una venta revertida no consume número
(sin huecos).

Por qué B y no las demás:

| Opción | Veredicto |
|---|---|
| A. Retry con backoff | No elimina la contención; solo la disimula. Latencia p2p peor y aún posible `SECUENCIA_NO_DISPONIBLE`. Descartada como solución principal. |
| **B. UPDLOCK/ROWLOCK** | **Elegida**: serializa exactamente el tramo fiscal, nativo, multi-instancia, sin huecos, observable (esperas visibles en `sys.dm_exec_requests`). |
| C. Otros mecanismos nativos | `sp_getapplock` añade una primitiva extra sin ventaja sobre el bloqueo de fila; `SEQUENCE` de SQL Server no da control de rango autorizado ni rollback por venta dentro del modelo actual. |
| D. Contador en memoria | Rompe con 2+ instancias y pierde números al reiniciar. Prohibido por el objetivo de la fase. |
| E. Cola centralizadora | Convierte un problema de milisegundos en un servicio distribuido. Sobrediseño. |

La concurrencia optimista queda como **red de seguridad** (proveedores sin sugerencias de bloqueo, como
SQLite en pruebas) y el índice único `IX_ElectronicInvoices_eNCF` como **última barrera**.

## Resultados después (misma herramienta, misma máquina)

| Concurrentes | Éxitos | Fallos | Duplicados | Latencia total |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 1 | 0 | 0 | 1 233 ms |
| 5 | 5 | 0 | 0 | 110 ms |
| 10 | 10 | 0 | 0 | 172 ms |
| 20 | 20 | 0 | 0 | 290 ms |
| 30 | 30 | 0 | 0 | 364 ms |
| 50 | 50 | 0 | 0 | 527 ms |
| 100 | 100 | 0 | 0 | 1 815 ms |

Mezcla de tipos y sucursales (OBJ 6): 20 simultáneas alternando e-CF **31** y **32** en **dos
sucursales** → 20/20 exitosas, E31=10, E32=226 (acumulado de todas las corridas), **0 duplicados**,
0 cruces de serie. 41 no es un tipo soportado por el terminal POS (solo 31/32; ver
`ProcesarVentaValidator`), así que no se ejercita.

## Límites declarados

- LocalDB en la máquina de desarrollo: las latencias absolutas no son de producción; lo que se verifica
  es el **orden de magnitud** de la contención y la unicidad absoluta.
- El bloqueo serializa la numeración: la latencia total crece linealmente con los emisores simultáneos
  (100 ventas ≈ 1,8 s). Es el costo de no duplicar números fiscales y no genera huecos.
- Con múltiples instancias de la aplicación el mecanismo es el mismo: el bloqueo vive en SQL Server,
  no en la memoria de ningún proceso.
