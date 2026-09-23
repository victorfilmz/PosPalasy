# 15 — Plan FASE 6: reporte 606, contingencia y portal de consultas

> **Fecha:** 23-09-2026 · **Predecesor:** FASE 5 cerrada (doc 13) · **Estado:** En ejecución —
> sub-fase 6.0 completada (gate G0 pendiente de cierre con el informe de fase).
>
> Los candidatos aquí listados son los identificados en el veredicto del gate 5.6. Este plan
> sigue el estilo del doc 11: sub-fases pequeñas con gate verificable cada una, commits por
> sub-fase y `git status` limpio antes de cada gate.

---

## 1. Alcance propuesto

| Sub-fase | Alcance | Entregable | Gate |
|---|---|---|---|
| 6.0 | **Reporte 606** (libro de compras) | `ReportesController` + exportación TXT oficial, patrón del 607 — **IMPLEMENTADA 23-09-2026** | G0 |
| 6.1 | **Régimen de contingencia** — declarar y operar | Estados de contingencia del emisor, indicador en el e-CF, cola local sin transmisión | G1 |
| 6.2 | **Contingencia — recuperación** | Transmisión masiva diferida de comprobantes en contingencia al volver la conectividad | G2 |
| 6.3 | **Portal de consultas** (UI) | Pantalla de consulta de comprobantes emitidos: estado fiscal, TrackId, mensajes DGII, reenvío manual | G3 |
| 6.4 | **Gate final de fase** | Informe consolidado con evidencia (estilo doc 13) | G4 |

## 2. Detalle por sub-fase

### 6.0 — Reporte 606 (libro de compras)

El 607 (ventas) e IT-1 ya operan desde Fase 3. El 606 completa el trío: compras registradas
del período con RNC del proveedor, NCF de compra, montos e ITBIS soportado.

- Fuente de datos — **decisión D1 resuelta:** NO existe módulo de compras formal; la fuente es
  el **kardex** (`MovimientoInventario` con `Tipo=EntradaCompra`), que ya registra proveedor,
  costo unitario, cantidad y referencia de documento. El NCF de compra se extrae de
  `ReferenciaDocumento` o del concepto libre (regex NCF tradicional B+10 / electrónico E+11).
  Limitación conocida y honesta: el costo del kardex no separa ITBIS (columna ITBIS=0) ni forma
  de pago (01) — ajustable cuando exista módulo de compras.
- Implementado: `Registro606Dto` + `GenerarRegistros606/606Txt` en `ReporteFiscalService`,
  `GetByTipoYFechaAsync` en el repositorio del kardex, acciones `Compras606/Exportar606Txt`,
  vista y entrada en el menú de Reportes.
- **Gate G0 — evidencia:** build 0/0; suite 306/306 (incluye 2 pruebas del 606: registros,
  bordes sin proveedor/NCF, formato TXT y período vacío; y el acceso del Contador a la vista
  por HTTP real en la suite de seguridad). El ITBIS real de compras queda abierto para cuando
  el propietario decida el módulo de compras.

### 6.1 — Contingencia: declarar y operar

La DGII permite emitir en contingencia cuando su plataforma no está disponible. El e-CF lleva
el indicador de contingencia y el tipo de contingencia; la transmisión se difiere.

- Dominio: tipo de contingencia (falla DGII / falla sistema propio / corte certificado),
  ventana temporal permitida, marcado del comprobante.
- Emisor: XML con `IndicadorNotaDeCredito`/contingencia según XSD — validar contra el mapa XSD.
- La cola existente retiene sin transmitir (reutiliza la máquina de estados de FASE 5).
- Estimado: 3–4 días.
- **Gate G1:** comprobante generado en contingencia pasa XSD, queda en cola marcado y la UI lo
  distingue.

### 6.2 — Contingencia: recuperación

Al volver la conectividad, transmitir los comprobantes en contingencia en orden y consolidar
el resultado fiscal de cada uno.

- Reutiliza el worker de la cola (lease + reintentos) y `ConsolidadorResultadoFiscal`.
- Estimado: 2–3 días.
- **Gate G2:** E2E simulado: corte → N comprobantes en contingencia → vuelta → todos
  transmitidos en orden, sin duplicados, resultado fiscal persistido.

### 6.3 — Portal de consultas (UI)

Pantalla única de transparencia fiscal: comprobantes emitidos con estado DGII, TrackId,
mensajes, fecha de confirmación; acción de reenvío manual para estados recuperables.

- Solo lectura + acción de reenvío con permiso de la matriz (doc 06).
- Estimado: 3–4 días.
- **Gate G3:** E2E por la UI real: venta → envío → consulta del estado → reenvío manual de un
  rechazado, con auditoría.

### 6.4 — Gate final

Informe consolidado (estilo doc 13) con build 0/0, suite completa en verde, arranque real
contra SQL Server y veredicto de la fase.

## 3. Riesgos y decisiones previas necesarias

| # | Decisión / riesgo | Dueño | Antes de |
|---|---|---|---|
| D1 | ~~¿Módulo de compras existe o el 606 se alimenta de otra fuente?~~ **RESUELTA:** el 606 se alimenta del kardex (EntradaCompra); sin módulo de compras formal | Propietario | ~~6.0~~ resuelta |
| D2 | Tipos de contingencia a soportar en 6.1 (DGII permite 1–5; recortar a los reales del negocio) | Propietario + código | 6.1 |
| D3 | La homologación testecf (doc 14) idealmente ANTES de 6.1–6.3: la evidencia real del cofre de contratos evita construir contingencia sobre supuestos | Operación | 6.1 |
| D4 | XSD de contingencia: confirmar que `MapaXsdComprobante` cubre el indicador usado | Código | 6.1 |

## 4. Orden recomendado

```text
Hoy:        trámite certificado A1 (doc 14)  +  6.0 (606, si D1 resuelta)
Tras 606:   6.1 → 6.2 → 6.3 → 6.4
En paralelo: homologación testecf cuando lleguen las credenciales
```

**Total estimado:** 10–15 días de código, condicionado a las decisiones D1–D2.
