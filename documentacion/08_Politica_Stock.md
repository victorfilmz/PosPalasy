# Política de existencias (Fase 3)

## Decisión

**Un solo nivel: `Enterprise.PoliticaStock`, tres estados**, impuesto por el servidor en cada venta.
No se modela política por sucursal ni por producto: no hay requisito operativo que lo exija y la
política de empresa es el punto de extensión natural si el negocio lo pide algún día.

| Valor | Nombre | Comportamiento real (verificado por pruebas HTTP) |
|---|---|---|
| 0 | `Permitir` | Se vende aunque la existencia no alcance o no esté registrada. Stock negativo explícito con kardex exacto. Defecto del sistema (minorista con reposición inmediata). |
| 1 | `Advertir` | La venta **prosigue**, pero la venta y el kardex quedan marcados (`RequiereRevisionStock` / `RequiereRevision`, concepto `ADVERTENCIA de stock…`) para revisión. Una venta normal **no** se marca. |
| 2 | `Bloquear` | La venta se rechaza con `STOCK_INSUFICIENTE` **antes** de tocar inventario, caja, venta, eNCF u outbox: `0 cambios` comprobado. |

## Autoridad

- La decisión la toma `ProcesarVentaHandler` (POS.Application) leyendo la política de la empresa dentro
  de la transacción. La UI (`Pos/Index.cshtml`) solo la usa para una mejor experiencia del cajero.
- Nunca se confía en un `if (stock >= cantidad)` del navegador: una petición manipulada que pida 3
  unidades con 1 disponible recibe 400 `STOCK_INSUFICIENTE` (prueba `VentaPorHttp_PoliticaBloquear_…`).

## Auditoría del cambio (mínima, Fase 3)

- Tabla `AuditoriaCambios`: `Usuario`, `FechaUtc`, `Entidad`, `Campo`, `ValorAnterior`, `ValorNuevo`,
  `Motivo` (+ índice por entidad/campo/fecha).
- El cambio se hace desde **Configuración → Empresa** (rol `Configuracion`: SuperAdmin/Administrador).
  El formulario ofrece el selector de los tres estados y un campo de motivo opcional.
- El registro se escribe en la misma operación del cambio; la prueba
  `CambioDePoliticaStock_QuedaAuditadoConUsuarioValoresYMotivo` verifica usuario, valores y motivo.
- No es un sistema de auditoría general: documentar quién cambió qué y por qué, nada más.

## Migración de bases existentes

El DDL idempotente añade `Enterprises.PoliticaStock` (DEFAULT 0) y **migra el valor anterior**:
`PermitirVentaSinStock = true → Permitir (0)`, `false → Bloquear (2)`. La columna y su migración
están en lotes DDL distintos porque SQL Server compila el lote completo antes de ejecutarlo.
En bases nuevas el modelo EF crea la columna directamente (defecto `Permitir`).

## Dónde vive cada cosa

| Pieza | Archivo |
|---|---|
| Enum `PoliticaStock` | `src/POS.Domain/Enums/PoliticaStock.cs` |
| Propiedad de la empresa | `src/POS.Domain/Entities/Enterprise.cs` |
| Imposición en la venta | `src/POS.Application/CasosDeUso/Ventas/ProcesarVentaHandler.cs` |
| Descuento condicional (ya existente, Fase 2) | `src/POS.Infrastructure/Persistence/Repositories/InventarioAlmacenRepository.cs` |
| Pantalla + auditoría del cambio | `src/POS.UI/Controllers/ConfiguracionController.cs` |
| Pruebas | `tests/POS.UI.SecurityTests/Fase3Tests.cs` |
