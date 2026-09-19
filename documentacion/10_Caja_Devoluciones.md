# 10 · Caja y devoluciones (FASE 4)

> **Alcance:** movimientos de caja con referencia a la venta, devoluciones transaccionales con
> tope de reembolso, arqueo y cierre atómico, verificados con pruebas de integración (SQLite real),
> pruebas E2E por el pipeline HTTP y arranque real contra SQL Server con migración de datos.

---

## 1. Resumen

| Capacidades nuevas | Dónde |
|---|---|
| Devolución total/parcial de una venta con reembolso por la forma de pago original | `RegistrarDevolucionHandler` |
| Tope de reembolso serializado: N devoluciones concurrentes nunca devuelven más de lo pagado | `DevolucionRepository.AnclarVentaParaDevolucionAsync` |
| Idempotencia de devolución (una clave → un reembolso) | índice único `IX_Devoluciones_ClaveIdempotencia` |
| Movimiento de caja de salida referenciado a la venta y con usuario | `MovimientoCaja.VentaId`, `MovimientoCaja.Usuario` |
| Acumulado de devoluciones del turno y arqueo | `CajaTurno.TotalDevoluciones`, `CajaTurnoRepository.AcumularDevolucionAsync` |
| Devolución restringida a roles de supervisión; cierre y movimientos solo del turno propio | `CajaController`, `Politicas.Supervision` |

Regresión completa: **232/232 pruebas en verde** (63 dominio + 70 ventas/caja + 99 seguridad/E2E),
build 0 errores / 0 advertencias, arranque real verificado (DDL aplicado sin errores sobre la base existente).

---

## 2. Modelo de la devolución

```text
Devolucion                       DevolucionItem
├─ ClaveIdempotencia (única)     ├─ VentaItemId (línea vendida, snapshot)
├─ VentaId (FK, NO ACTION)       ├─ ProductoId
├─ CajaTurnoId                   ├─ Cantidad
├─ Usuario / Motivo / Fecha      ├─ MontoBase / MontoITBIS / MontoTotal  (prorrateo 2 dec.)
└─ TotalDevuelto + desglose
   (Efectivo / Tarjeta / Transferencia)
```

- **Prorrateo por línea** (`Devolucion.CrearItem`): cada renglón devuelto toma del original la parte
  proporcional de base, ITBIS y total, redondeado a 2 decimales *por línea* (`MidpointRounding.AwayFromZero`).
  El total devuelto es la suma de los renglones, nunca un número suelto escrito a mano.
- **Sin detalle = línea pendiente completa**: pedir devolución de una venta ya parcialmente devuelta
  devuelve lo que queda pendiente, no la cantidad original (evita sobre-devoluciones por accidente).
- **El servidor es la autoridad**: cantidades, montos y desglose se calculan dentro del caso de uso
  a partir de la venta persistida; el formulario solo expresa intención (`VentaId`, `Motivo`, cantidades).

## 3. Reembolso por la forma de pago original

`Devolucion.CalcularReembolso(totalDevuelto, venta)`:

```text
tarjeta        = min(lo pagado con tarjeta,        totalDevuelto)
transferencia  = min(lo pagado por transferencia,  restante)
efectivo       = restante  (sale de la gaveta)
```

- El reembolso vuelve **por el medio con que se pagó**: tarjeta y transferencia son movimientos del
  procesador, no de la gaveta. Solo el efectivo genera un **movimiento de caja de salida**
  referenciado a la venta (`VentaId`) y con el usuario que lo ejecutó.
- Si el reembolso es 100 % tarjeta/transferencia **no se crea movimiento de caja**: un movimiento de
  RD$ 0.00 es ruido en el arqueo.
- El turno acumula **el total devuelto** (todas las formas) en `TotalDevoluciones`, para que el
  arqueo cuadre contra la actividad del turno aunque la gaveta no se haya movido.

## 4. Unidad transaccional y serialización del tope

Toda la devolución es **una transacción** (`IUnidadDeTrabajo.EnTransaccionAsync`):

```text
1. Anclar la fila de la venta      UPDATE Ventas SET Fecha = Fecha WHERE Id = @venta
2. Leer turno abierto del usuario  (0 filas → SIN_TURNO_DE_CAJA)
3. Leer lo ya devuelto y construir renglones (exceso → CANTIDAD_EXCEDE_VENDIDA / DEVOLUCION_VACIA)
4. Calcular desglose de reembolso
5. Insertar Devolucion + Items
6. Reingresar stock + kardex (TipoMovimientoInventario.DevolucionVenta, referencia DEV-<id>)
7. Insertar movimiento de caja (solo si efectivo > 0)
8. Acumular en el turno (0 filas actualizadas → TURNO_CERRADO: se revierte TODO)
```

**El paso 1 es la barrera de concurrencia** (mismo patrón que la numeración eNCF, Fase 3): sobre
SQL Server, la escritura nula toma el bloqueo exclusivo de la fila de la venta hasta el fin de la
transacción, de modo que dos devoluciones con claves distintas se **serializan** y la segunda ve el
tope consumido por la primera. Sin ese ancla, ambas leerían "0 devuelto" y el reembolso se duplicaría
(sin él, la integridad dependería de la suerte del plan de ejecución). En SQLite (pruebas) la primera
escritura ocupa el archivo: la prueba `DevolucionesConcurrentesSobreLaMismaVenta_NuncaExcedenLoPagado`
verifica que de dos intentos simultáneos exactamente **uno** triunfa y el otro recibe
`DEVOLUCION_VACIA` con **cero** efectos laterales.

**Sin venta no hay devolución por esta vía**: una venta sin comprobante fiscal registrado se rechaza
(`VENTA_SIN_COMPROBANTE`); la devolución fiscal parte de un documento emitido. *(La generación del
e-CF de crédito / ANECF ante la DGII pertenece a las fases fiscales: no se adeló.)*

## 5. Idempotencia

- La clave la genera la vista (`RegistrarDevolucionForm.ClaveIdempotencia`) y el controlador completa
  una si falta; el **índice único** de la base es la barrera definitiva.
- Reintento con la misma clave → respuesta de la devolución original marcada `Duplicada`, sin
  segundo reembolso, sin segundo movimiento y sin doble reingreso de stock.
- Prueba: `DobleSolicitudConLaMismaClave_RegistraUnaSolaDevolucion`.

## 6. Autorización y titularidad

| Operación | Política | Regla adicional |
|---|---|---|
| Ver / registrar devolución | `Supervision` (SuperAdmin, Administrador, Supervisor) | exige turno propio abierto (el reembolso sale de SU gaveta) |
| Cierre de turno | `OperacionPos` | solo el **turno propio** (`id` ajeno se rechaza antes de tocar la base) |
| Movimiento manual de caja | `OperacionPos` | solo el turno propio |

El cajero sin supervisión recibe **403 / redirección a acceso denegado** y ni una fila cambia
(prueba E2E `DevolucionPorHttp_CajeroSinSupervision_Recibe403YNoRegistraNada`).

## 7. Arqueo y cierre

- **Corte X** (parcial) y **Reporte Z** (definitivo) muestran fondo inicial, ventas, devoluciones,
  otros movimientos, efectivo esperado y la diferencia del conteo real.
- El cierre es **atómico y de un solo uso**: `CerrarTurnoAsync` actualiza el turno
  (`Estado = Cerrado`, monto real, `Diferencia = esperado − real`) solo si sigue abierto; un segundo
  intento no altera el arqueo (pruebas `CierreAtomico_SoloElPrimerCierreAlteraElArqueo` y E2E
  `CierrePorHttp_DobleEnvio_ElArqueoSoloRegistraElPrimero`).
- Una devolución que llega cuando el turno acaba de cerrarse se **revierte por completo** (el
  acumulado de 0 filas dispara el rollback): `DevolucionConTurnoCerradoSEReviertePorCompleto`.
- Bajo concurrencia, las N devoluciones simultáneas de un turno quedan todas acumuladas y cada
  movimiento referenciado a su venta, sin mezclas
  (`ArqueoBajoConcurrencia_TodasLasDevolucionesAcumulanYQuedanReferenciadas`).

## 8. Cobertura de pruebas de la fase

| Tipo | Prueba | Verifica |
|---|---|---|
| Integración | `DevolucionTotal_ReembolsaPorLasFormasDePagoReingresaStockYDejaCajaExacta` | desglose, stock, kardex, movimiento |
| Integración | `DevolucionParcial_DeUnaDeDosUnidades_ReembolsaLaParteProporcional` | prorrateo por línea |
| Integración | `DevolucionQueExcedeLoVendido_EsRechazadaYSinEfectos` | tope + 0 efectos |
| Integración | `SegundaDevolucionTotal_NoPuedeExcederLoPendiente` | tope entre intentos sucesivos |
| Integración | `DevolucionConPagoMixto_ReembolsaPorCadaMedioYEfectivoDelResto` | tarjeta→tarjeta, resto→gaveta |
| Integración | `DobleSolicitudConLaMismaClave_RegistraUnaSolaDevolucion` | idempotencia |
| Integración | `DevolucionConTurnoCerradoSEReviertePorCompleto` | 0 efectos ante turno cerrado |
| Integración | `DevolucionDeVentaSinComprobante_EsRechazada` | partida fiscal |
| Integración | `DevolucionesConcurrentesSobreLaMismaVenta_NuncaExcedenLoPagado` | **serialización del tope** |
| Integración | `ArqueoBajoConcurrencia_TodasLasDevolucionesAcumulanYQuedanReferenciadas` | arqueo exacto bajo concurrencia |
| Integración | `ArqueoDelTurno_CuadraConVentasYDevoluciones`, `CierreAtomico_...` | arqueo y cierre |
| E2E HTTP | `DevolucionPorHttp_SupervisorRegistra_...` | pipeline real: autorización, caja, stock |
| E2E HTTP | `DevolucionPorHttp_CajeroSinSupervision_...` | 403 y ninguna fila |
| E2E HTTP | `CierrePorHttp_TurnoAjeno_...`, `CierrePorHttp_DobleEnvio_...` | titularidad y cierre idempotente |

## 9. Migración de esquema (DDL idempotente, SQL Server y SQLite)

```sql
CREATE TABLE [Devoluciones]      (... FK [VentaId] → [Ventas] ON DELETE NO ACTION ...);
CREATE TABLE [DevolucionItems]   (... FK [DevolucionId] CASCADE, [ProductoId] NO ACTION ...);
CREATE UNIQUE INDEX [IX_Devoluciones_ClaveIdempotencia] ...;
ALTER TABLE [CajaTurnos]      ADD [TotalDevoluciones] decimal(18,2) NOT NULL DEFAULT 0;
ALTER TABLE [MovimientosCaja] ADD [Usuario] nvarchar(100) NULL;
ALTER TABLE [MovimientosCaja] ADD [VentaId] int NULL;  -- + FK a [Ventas] ON DELETE NO ACTION (guardada)
```

> **Corrección de la fase:** la primera versión usaba `ON DELETE RESTRICT` (sintaxis SQLite, no
> T-SQL). Las pruebas SQLite no la detectaban; el **arranque real contra SQL Server** sí. Las FK
> usan `NO ACTION` (equivalente estándar y válido en ambos motores). Lección registrada: el DDL se
> valida con un arranque real, no solo con la suite.

## 10. Deuda técnica declarada

1. **Concurrencia devuelta verificada sobre SQLite**: el ancla de serialización usa el mismo patrón
   probado sobre SQL Server en Fase 3 (eNCF, 100/100 sin duplicados); falta repetir la prueba de
   carga de devoluciones sobre SQL Server/LocalDB. *(FASE futura de carga)*
2. **La devolución no genera aún el e-CF de crédito / ANECF** ni notifica a la DGII: el registro
   interno queda trazado; la parte fiscal corresponde a las fases fiscales. *(FASES 5–6)*
3. **La consulta de la venta a devolver** en la vista usa búsqueda por id; una búsqueda por número
   de ticket/eNCF mejorará la operación en tienda. *(mejora UX)*
