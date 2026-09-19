# PosPalasy

Punto de venta (POS) con **facturación electrónica e-CF** conforme a la normativa de la DGII (República Dominicana).

## Arquitectura

Solución .NET 10 en capas (Clean Architecture):

| Proyecto | Rol |
|---|---|
| `src/POS.Domain` | Entidades, enums y contratos de repositorio del dominio fiscal |
| `src/POS.Domain.Types` | Tipos primitivos y tipos de datos compartidos |
| `src/POS.Application` | Casos de uso y servicios de aplicación |
| `src/POS.Infrastructure` | Persistencia (EF Core / SQL Server), cliente HTTP DGII, serialización XML, firma digital XML-DSig |
| `src/POS.UI` | App web ASP.NET Core (MVC) |
| `tests/POS.Domain.Types.Tests` | Pruebas unitarias |

## Características fiscales

- Emisión de e-CF 31 (Crédito Fiscal) y e-CF 32 (Consumo)
- Validación contra los **XSD oficiales de la DGII** (carpeta `documentacion xsd/`)
- Firma digital **XML-DSig** con certificado .pfx del emisor
- Cola de emisión resiliente (`EmisionesDGIIQueue`) para contingencia offline
- Reportes fiscales: formato **607**, resumen ITBIS (IT-1)
- Anulaciones e-CF (ANECF)

## Requisitos

- .NET SDK 10.0
- SQL Server / LocalDB

## Cómo ejecutar

```bash
# 1. Defina la contraseña de la cuenta inicial (nunca en appsettings.json)
dotnet user-secrets --project src/POS.UI set "Seguridad:AdminInicial:Password" "<contraseña-fuerte>"

# 2. Ejecute la aplicación
dotnet run --project src/POS.UI
```

La aplicación inicializa la base de datos, los datos semilla y la cuenta administradora inicial.
Si no define la contraseña inicial, el sistema genera una temporal, la registra una sola vez en el
log de arranque y **obliga a cambiarla en el primer acceso**.

> **Nota:** la configuración DGII incluye un modo simulador (`DGII:ModoSimulador=true` por defecto)
> para desarrollo sin conectarse al entorno real. Si el simulador está activo fuera del entorno
> `Development`, **la aplicación se niega a arrancar**: el simulador nunca puede facturar "de mentira"
> en producción.

## Seguridad

- Todas las pantallas y operaciones exigen sesión autenticada; el inicio de sesión es la única acción anónima.
- Roles: `SuperAdmin`, `Administrador`, `Supervisor`, `Cajero`, `Contador`, con políticas por operación
  (venta, anulación fiscal, ajustes de inventario, configuración, cambio de ambiente DGII, reportes).
- Toda petición que modifica estado exige token antiforgery (incluido el POST JSON del terminal POS).
- Contraseñas con PBKDF2 (sal por contraseña), política mínima, bloqueo tras 5 intentos fallidos.
- El certificado `.pfx` se guarda fuera del directorio de la aplicación y su contraseña se lee de
  user-secrets o variable de entorno (`Certificado:Password`), nunca del repositorio.

Detalle completo de roles, matriz de permisos y pruebas: [`documentacion/06_Seguridad_Matriz_Permisos.md`](documentacion/06_Seguridad_Matriz_Permisos.md).

## Venta transaccional

El registro de una venta es un caso de uso de `POS.Application` (`ProcesarVentaHandler`), no lógica de
controlador. Todo lo que ocurre en una venta es **una sola transacción** de base de datos:

- el precio, el ITBIS, la unidad de medida y la descripción los resuelve el **servidor** desde el catálogo;
- la numeración fiscal (eNCF) se asigna dentro de la transacción con bloqueo de fila en SQL Server
  (`UPDLOCK, ROWLOCK`), concurrencia optimista como red de seguridad e índice único como última barrera:
  0 duplicados verificados hasta con 100 ventas simultáneas;
- existencias, kardex, acumulado de caja, comprobante y cola de emisión se confirman o se revierten juntos;
- la **política de existencias** (`Permitir` / `Advertir` / `Bloquear`) la impone el servidor y su cambio
  queda auditado;
- la transmisión a la DGII ocurre **fuera** de la transacción: un fallo de red no revierte una venta ya
  cobrada ni se reporta como comprobante aceptado;
- cada intento lleva una clave de idempotencia: doble clic, doble POST, recarga o reintento no producen
  dos ventas, dos movimientos de inventario, dos movimientos de caja ni dos e-CF.
- El registro de ventas tiene **un único camino** (`ProcesarVentaHandler`): la emisión manual de
  comprobantes sin venta fue retirada en la Fase 3.

Flujo completo, máquina de estados, idempotencia y matriz de escenarios:
[`documentacion/07_Flujo_Venta_Transaccional.md`](documentacion/07_Flujo_Venta_Transaccional.md).
Política de existencias: [`documentacion/08_Politica_Stock.md`](documentacion/08_Politica_Stock.md).
Numeración fiscal bajo concurrencia: [`documentacion/09_Concurrencia_eNCF.md`](documentacion/09_Concurrencia_eNCF.md).
Caja y devoluciones: [`documentacion/10_Caja_Devoluciones.md`](documentacion/10_Caja_Devoluciones.md).

## Pruebas

```bash
dotnet test PosPalasy.slnx
```

- `tests/POS.Domain.Types.Tests` — dominio, tipos DGII, cálculos y flujos del POS.
- `tests/POS.Ventas.Tests` — caso de uso de venta contra base de datos real: atomicidad, inventario,
  caja, numeración fiscal, idempotencia y fallos inyectados.
- `tests/POS.UI.SecurityTests` — seguridad de extremo a extremo sobre el pipeline HTTP real
  (autenticación, autorización por rol, antiforgery, cuentas, matriz de permisos) y venta de extremo a
  extremo por HTTP.

## Documentación

La documentación de diseño, arquitectura y mapeo XSD ↔ C# está en [`documentacion/`](documentacion/index.md).

> **Aviso de fidelidad:** parte de la documentación de diseño describe la implementación **prevista**.
> Las funciones fiscales marcadas como pendientes (firma XML-DSig integrada, validación XSD en el flujo
> de emisión, ANECF conforme al esquema, autenticación DGII) **aún no están operativas**; consulte la
> auditoría técnica antes de considerar el sistema apto para producción.
