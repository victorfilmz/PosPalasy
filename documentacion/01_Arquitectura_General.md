# Documentación Técnica - Sistema POS con Facturación Electrónica DGII

**Versión:** 1.0  
**Fecha:** 16/09/2026  
**Stack:** .NET 10 / ASP.NET Core MVC / Clean Architecture / SQL Server / EF Core

---

## 1. Visión General

El sistema es un POS local para punto de venta con módulo de facturación electrónica conforme a los esquemas XSD oficiales de la Dirección General de Impuestos (DGII) de la República Dominicana.

Los esquemas XSD provistos cubren:

| Tipo | Archivo | Uso |
|---|---|---|
| e-CF tipo 32 | `e-CF 32 v.1.0.xsd` | Factura de Consumo Electrónica (venta al cliente final) — esquema completo |
| e-CF tipo 31 | `e-CF 31 v.1.0.xsd` | Factura de Crédito Fiscal Electrónica (ventas a empresas con derecho de crédito) |
| e-CF tipo 41 | `e-CF 41 v.1.0.xsd` | Compras Electrónicas |
| e-CF tipo 43-47 | `e-CF 43..47 v.1.0.xsd` | Gastos menores, regímenes especiales, gubernamental, exportaciones, pagos al exterior |
| RFCE tipo 32 | `RFCE 32 v.1.0.xsd` | Factura de consumo simplificada (para contingencia) |
| ANECF | `ANECF v.1.0.xsd` | Anulación de NCF |
| ACECF | `ACECF v.1.0.xsd` | Aprobación comercial |
| ARECF | `ARECF v1.0.xsd` | Acuse de recibo |
| Semilla | `Semilla v.1.0.xsd` | Contenedor base XML |

---

## 2. Arquitectura del Sistema

### 2.1 Estructura Clean Architecture

```
POS/
├── src/
│   ├── POS.Domain/              — Reglas de negocio, entidades, validaciones
│   ├── POS.Application/        — Casos de uso, DTOs, interfaces
│   ├── POS.Infrastructure/     — DB, DGII API, impresoras, hardware
│   └── POS.Web/                — ASP.NET Core MVC, Razor Views
├── tests/
│   ├── POS.Domain.Tests/
│   ├── POS.Application.Tests/
│   └── POS.IntegrationTests/
└── docs/
    ├── documentacion/          — Esta documentación
    └── documentacion xsd/     — XSDs originales de DGII
```

### 2.2 Flujo de Dependencias

```
         ┌──────────────┐
         │    DOMAIN    │  ← ninguna dependencia externa
         └──────▲───────┘
                │
         ┌──────┴───────┐
         │ APPLICATION  │  ← depende de Domain, define interfaces
         └──────▲───────┘
                │
     ┌─────────┴─────────┐
     │                   │
┌────┴───────┐    ┌──────┴──────┐
│INFRASTRUCTURE│   │     WEB     │
│  DGII, DB,   │   │  MVC, Views │
│  Hardware    │   │             │
└─────────────┘   └─────────────┘
```

---

## 3. Módulo de Facturación Electrónica - Diseño

### 3.1 Posición en la Arquitectura

El módulo de facturación electrónica se integra así:

```
Application
    │
    ├── ElectronicInvoicing/
    │   ├── Commands/
    │   │   ├── CreateElectronicInvoiceCommand
    │   │   ├── AnularElectronicInvoiceCommand
    │   │   └── ConsultarEstadoCommand
    │   ├── Queries/
    │   │   ├── GetElectronicInvoiceQuery
    │   │   └── GetInvoiceHistoryQuery
    │   ├── DTOs/
    │   │   ├── EInvoiceDTO
    │   │   ├── EInvoiceLineDTO
    │   │   └── InvoiceStatusDTO
    │   └── Validators/
    │       └── CreateElectronicInvoiceValidator
    │
    └── Interfaces/
        ├── IElectronicInvoiceService.cs
        ├── IInvoiceRepository.cs
        └── IHashGenerator.cs

Infrastructure
    │
    ├── ElectronicInvoicing/
    │   ├── DgiiElectronicInvoiceService.cs  ← implementa IElectronicInvoiceService
    │   ├── XmlSerializer.cs                ← serializa objetos a XML según XSD
    │   ├── XmlValidator.cs                 ← valida XML contra XSD
    │   ├── HashGenerator.cs                ← genera hash de seguridad (CodigoSeguridadeCF)
    │   └── DigitalSignatureService.cs      ← firma digital (pendiente integración)
    │
    └── Repositories/
        └── SqlInvoiceRepository.cs        ← persistencia local de facturas
```

### 3.2 Flujo de Emisión de Factura

```
Usuario cierra venta en POS
    │
    ▼
CreateSaleHandler (Application)
    │  - Valida productos, stock, precios
    │  - Calcula ITBIS (18%, 16%, 0%, Exento)
    │  - Calcula totales
    │  - Registra Sale + SaleItems en BD
    │
    ▼
[Generar factura electrónica]
    │
    ▼
ElectronicInvoiceService.SubmitAsync(invoice)
    │
    ├── 1. Construir objeto EInvoiceDTO desde Sale
    ├── 2. Validar reglas DGII (RNC, montos, impuestos)
    ├── 3. Serializar a XML conforme e-CF 32 XSD
    ├── 4. Validar XML contra XSD (XmlValidator)
    ├── 5. Calcular Hash de seguridad (CodigoSeguridadeCF)
    ├── 6. Firmar digitalmente (pendiente certificado digital)
    ├── 7. Enviar a DGII (SOAP/HTTP)
    ├── 8. Recibir ARECF (acuse de recibo)
    ├── 9. Recibir ACECF (aprobación comercial)
    └── 10. Persistir resultado + XML en BD
    │
    ▼
Venta finalizada, factura impresa, XML guardado
```

---

## 4. Mapeo XSD → Entidades C#

### 4.1 Tipos de Comprobante (TipoeCFType)

```csharp
public enum TipoeCFType
{
    FacturaCreditoFiscal = 31,      // e-CF 31 - Ventas con derecho de crédito
    FacturaConsumo = 32,            // e-CF 32 - Factura de consumo (cliente final)
    NotaDebito = 33,                // e-CF 33
    NotaCredito = 34,               // e-CF 34
    Compras = 41,                   // e-CF 41
    GastosMenores = 43,             // e-CF 43
    RegimenesEspeciales = 44,       // e-CF 44
    Gubernamental = 45,             // e-CF 45
    Exportaciones = 46,             // e-CF 46
    PagosExterior = 47              // e-CF 47
}
```

### 4.2 Encabezado de Factura

| Campo XSD | Tipo C# | Obligatorio | Descripción |
|---|---|---|---|
| `Version` | `decimal` | Sí | 1.0 |
| `TipoeCF` | `TipoeCFType` | Sí | Tipo de comprobante |
| `eNCF` | `string` (13 chars) | Sí | Número de comprobante fiscal (ej: B0000000000001) |
| `IndicadorEnvioDiferido` | `int?` | No | 1 = envío diferido autorizado |
| `IndicadorMontoGravado` | `int?` | No | 0 = ITBIS no incluido en línea, 1 = ITBIS incluido |
| `IndicadorServicioTodoIncluido` | `int?` | No | 1 = servicio todo incluido |
| `TipoIngresos` | `TipoIngresosType` | Sí | 01-06 según tipo de ingreso |
| `TipoPago` | `TipoPagoType` | Sí | 1=Contado, 2=Crédito, 3=Gratuito |
| `FechaLimitePago` | `DateOnly?` | No | Fecha límite de pago (crédito) |
| `TerminoPago` | `string?` | No | Término de pago (max 15 chars) |
| `TablaFormasPago` | `List<FormaPago>` | No | Hasta 7 formas de pago |
| `TipoCuentaPago` | `string?` | No | CT/AH/OT |
| `NumeroCuentaPago` | `string?` | No | Número de cuenta (max 28) |
| `BancoPago` | `string?` | No | Banco (max 75) |
| `FechaDesde` | `DateOnly?` | No | Fecha desde (para periodos) |
| `FechaHasta` | `DateOnly?` | No | Fecha hasta |
| `TotalPaginas` | `int?` | No | Total de páginas (> 1) |

### 4.3 Emisor

| Campo XSD | Tipo C# | Obligatorio |
|---|---|---|
| `RNCEmisor` | `string` (9 o 11 dígitos) | Sí |
| `RazonSocialEmisor` | `string` (max 150) | Sí |
| `NombreComercial` | `string` (max 150) | No |
| `Sucursal` | `string` (max 20) | No |
| `DireccionEmisor` | `string` (max 100) | Sí |
| `Municipio` | `ProvinciaMunicipioCode` | No |
| `Provincia` | `ProvinciaMunicipioCode` | No |
| `TelefonoEmisor` | `List<string>` (hasta 3, formato XXX-XXX-XXXX) | No |
| `CorreoEmisor` | `string` (max 80, email válido) | No |
| `WebSite` | `string` (max 50) | No |
| `ActividadEconomica` | `string` (max 100) | No |
| `CodigoVendedor` | `string` (max 60) | No |
| `NumeroFacturaInterna` | `string` (max 20) | No |
| `NumeroPedidoInterno` | `int?` (max 20 dígitos) | No |
| `ZonaVenta` | `string` (max 20) | No |
| `RutaVenta` | `string` (max 20) | No |
| `InformacionAdicionalEmisor` | `string` (max 250) | No |
| `FechaEmision` | `DateOnly` | Sí |

### 4.4 Comprador

| Campo XSD | Tipo C# | Obligatorio | Notas |
|---|---|---|---|
| `RNCComprador` | `string` (9/11 dígitos) | Depende | Obligatorio para e-CF 32 a empresas |
| `IdentificadorExtranjero` | `string` (max 20) | Depende | Si comprador es extranjero sin RNC |
| `RazonSocialComprador` | `string` (max 150) | Depende | |
| `ContactoComprador` | `string` (max 80) | No | |
| `CorreoComprador` | `string` (email) | No | |
| `DireccionComprador` | `string` (max 100) | No | |
| `MunicipioComprador` | `ProvinciaMunicipioCode` | No | |
| `ProvinciaComprador` | `ProvinciaMunicipioCode` | No | |
| `FechaEntrega` | `DateOnly?` | No | |
| `ContactoEntrega` | `string` (max 100) | No | |
| `DireccionEntrega` | `string` (max 100) | No | |
| `TelefonoAdicional` | `string` (formato teléfono) | No | |
| `FechaOrdenCompra` | `DateOnly?` | No | |
| `NumeroOrdenCompra` | `string` (max 20) | No | |
| `CodigoInternoComprador` | `string` (max 20) | No | |
| `ResponsablePago` | `string` (max 20, alfa) | No | |
| `InformacionAdicionalComprador` | `string` (max 150) | No | |

**Regla:** Para e-CF tipo 32 (Factura de Consumo), el comprador es obligatorio y debe tener RNC si es empresa dominicana. Si es persona física sin RNC, se usa otra identificación.

### 4.5 Items / Líneas de Producto

Cada item en `DetallesItems/Item`:

| Campo XSD | Tipo C# | Obligatorio | Notas |
|---|---|---|---|
| `NumeroLinea` | `int` (1-1000) | Sí | Número de línea secuencial |
| `TablaCodigosItem` | `List<CodigoItem>` | No | Hasta 5 códigos por item |
| `IndicadorFacturacion` | `int` (0-4) | Sí | 0=No facturable 18%, 1=ITBIS 18%, 2=ITBIS 16%, 3=ITBIS 0%, 4=Exento |
| `NombreItem` | `string` (max 80) | Sí | |
| `IndicadorBienoServicio` | `int` (1=Biens, 2=Servicio) | Sí | |
| `DescripcionItem` | `string` (max 1000) | No | |
| `CantidadItem` | `decimal` (≥0) | Sí | |
| `UnidadMedida` | `int` (1-62, tabla DGII) | No | Código de unidad de medida |
| `CantidadReferencia` | `decimal` (≥0) | No | |
| `UnidadReferencia` | `int` | No | |
| `TablaSubcantidad` | `List<Subcantidad>` | No | Hasta 5 subcalidades |
| `GradosAlcohol` | `decimal` (≥0) | No | Para bebidas alcohólicas |
| `FechaElaboracion` | `DateOnly?` | No | |
| `FechaVencimientoItem` | `DateOnly?` | No | |
| `Mineria` | `MineriaDTO?` | No | Sectores mineros |
| `PrecioUnitarioItem` | `decimal` (≥0) | Sí | Precio unitario |
| `DescuentoMonto` | `decimal` (≥0) | No | Descuento global del item |
| `TablaSubDescuento` | `List<SubDescuento>` | No | Hasta 12 subdescuentos |
| `RecargoMonto` | `decimal` (≥0) | No | |
| `TablaSubRecargo` | `List<SubRecargo>` | No | Hasta 12 subrecargos |
| `TablaImpuestoAdicional` | `List<ImpuestoAdicional>` | No | Hasta 2 por item |
| `OtraMonedaDetalle` | `OtraMonedaDetalle?` | No | Si se usa moneda extranjera |
| `MontoItem` | `decimal` (≥0) | Sí | Monto total del item (precio × cantidad - descuentos + recargos) |

### 4.6 Totales

| Campo XSD | Tipo C# | Obligatorio |
|---|---|---|
| `MontoGravadoTotal` | `decimal` (≥0) | No |
| `MontoGravadoI1` | `decimal` (≥0) | No |
| `MontoGravadoI2` | `decimal` (≥0) | No |
| `MontoGravadoI3` | `decimal` (≥0) | No |
| `MontoExento` | `decimal` (≥0) | No |
| `ITBIS1` | `int` | No — porcentaje 18 |
| `ITBIS2` | `int` | No — porcentaje 16 |
| `ITBIS3` | `int` | No — porcentaje 0 |
| `TotalITBIS` | `decimal` (≥0) | No |
| `TotalITBIS1` | `decimal` (≥0) | No |
| `TotalITBIS2` | `decimal` (≥0) | No |
| `TotalITBIS3` | `decimal` (≥0) | No |
| `MontoImpuestoAdicional` | `decimal` (>0) | No |
| `ImpuestosAdicionales` | `List<ImpuestoAdicional>` | No — hasta 20 |
| `MontoTotal` | `decimal` (≥0) | **Sí** — total de la factura |
| `MontoNoFacturable` | `decimal` | No |
| `MontoPeriodo` | `decimal` | No |
| `SaldoAnterior` | `decimal` | No |
| `MontoAvancePago` | `decimal` (≥0) | No |
| `ValorPagar` | `decimal` | No |

### 4.7 Facturas Adicionales

**e-CF 31 (Factura de Crédito Fiscal):** Igual que e-CF 32 pero agrega `FechaVencimientoSecuencia` obligatorio y no tiene `IndicadorServicioTodoIncluido`.

**e-CF 41 (Compras):** Comprador es obligatorio siempre. No tiene `TablaSubDescuento` ni `TablaSubRecargo` en items.

**e-CF 47 (Pagos al Exterior):** Comprador no es obligatorio. Agrega `PaisDestino` en Transporte.

---

## 5. Otros Documentos DGII

### 5.1 ANECF — Anulación de NCF

```xml
<ANECF>
  <Encabezado>
    <Version>1.0</Version>
    <RncEmisor>1234567890</RncEmisor>
    <CantidadeNCFAnulados>5</CantidadeNCFAnulados>
    <FechaHoraAnulacioneNCF>15-09-2026 14:30:00</FechaHoraAnulacioneNCF>
  </Encabezado>
  <DetalleAnulacion>
    <Anulacion>
      <NoLinea>1</NoLinea>
      <TipoeCF>32</TipoeCF>
      <TablaRangoSecuenciasAnuladaseNCF>
        <Secuencias>
          <SecuenciaeNCFDesde>B0000000000001</SecuenciaeNCFDesde>
          <SecuenciaeNCFHasta>B0000000000003</SecuenciaeNCFHasta>
        </Secuencias>
      </TablaRangoSecuenciasAnuladaseNCF>
      <CantidadeNCFAnulados>3</CantidadeNCFAnulados>
    </Anulacion>
  </DetalleAnulacion>
</ANECF>
```

**Tipos de factura anulable:** 31, 32, 33, 34, 41, 43, 44, 45, 46, 47

### 5.2 ACECF — Aprobación Comercial

```xml
<ACECF>
  <DetalleAprobacionComercial>
    <Version>1.0</Version>
    <RNCEmisor>1234567890</RNCEmisor>
    <eNCF>B0000000000001</eNCF>
    <FechaEmision>15-09-2026</FechaEmision>
    <MontoTotal>15000.00</MontoTotal>
    <RNCComprador>0987654321</RNCComprador>
    <Estado>1</Estado>          <!-- 1=Aceptado, 2=Rechazado -->
    <DetalleMotivoRechazo>...</DetalleMotivoRechazo>   <!-- Solo si rechazado -->
    <FechaHoraAprobacionComercial>15-09-2026 14:35:00</FechaHoraAprobacionComercial>
  </DetalleAprobacionComercial>
</ACECF>
```

### 5.3 ARECF — Acuse de Recibo

```xml
<ARECF>
  <DetalleAcusedeRecibo>
    <Version>1.0</Version>
    <RNCEmisor>1234567890</RNCEmisor>
    <RNCComprador>0987654321</RNCComprador>
    <eNCF>B0000000000001</eNCF>
    <Estado>0</Estado>          <!-- 0=Recibido, 1=No Recibido -->
    <CodigoMotivoNoRecibido>...</CodigoMotivoNoRecibido>  <!-- Si no recibido -->
    <FechaHoraAcuseRecibo>15-09-2026 14:32:00</FechaHoraAcuseRecibo>
  </DetalleAcusedeRecibo>
</ARECF>
```

Códigos de motivo de no recibido:
- 1 = Error de Especificación
- 2 = Error de Firma Digital
- 3 = Envío Duplicado
- 4 = RNC Comprador no Corresponde

---

## 6. Cálculo de ITBIS

### 6.1 Tablas de Impuestos

| Código | Descripción | Tasa |
|---|---|---|
| ITBIS 1 | Tasa general | 18% |
| ITBIS 2 | Artículos de primera necesidad | 16% |
| ITBIS 3 | Exentos de ITBIS | 0% |
| Exento | No sujeto a ITBIS | 0% |

### 6.2 Lógica de Cálculo

```csharp
public class TaxCalculator
{
    public TaxBreakdown Calculate(IEnumerable<InvoiceLine> lines)
    {
        decimal montoGravadoI1 = 0; // 18%
        decimal montoGravadoI2 = 0; // 16%
        decimal montoGravadoI3 = 0; // 0% ITBIS pero gravado
        decimal montoExento = 0;    // Exento total
        decimal totalITBIS = 0;

        foreach (var line in lines)
        {
            decimal montoLinea = line.PrecioUnitario * line.Cantidad
                                 - line.DescuentoMonto
                                 + line.RecargoMonto;

            switch (line.IndicadorFacturacion)
            {
                case 0: // No facturable (18% sobre margen)
                    // Se agrega al total pero el ITBIS se calcula sobre el margen
                    break;
                case 1: // ITBIS 18%
                    montoGravadoI1 += montoLinea;
                    totalITBIS += montoLinea * 0.18m;
                    break;
                case 2: // ITBIS 16%
                    montoGravadoI2 += montoLinea;
                    totalITBIS += montoLinea * 0.16m;
                    break;
                case 3: // ITBIS 0% (gravado pero tasa 0)
                    montoGravadoI3 += montoLinea;
                    break;
                case 4: // Exento
                    montoExento += montoLinea;
                    break;
            }
        }

        return new TaxBreakdown
        {
            MontoGravadoTotal = montoGravadoI1 + montoGravadoI2 + montoGravadoI3,
            MontoGravadoI1 = montoGravadoI1,
            MontoGravadoI2 = montoGravadoI2,
            MontoGravadoI3 = montoGravadoI3,
            MontoExento = montoExento,
            TotalITBIS = totalITBIS,
            TotalITBIS1 = montoGravadoI1 * 0.18m,
            TotalITBIS2 = montoGravadoI2 * 0.16m,
            TotalITBIS3 = 0m,
            MontoTotal = montoGravadoI1 + montoGravadoI2 + montoGravadoI3
                       + montoExento + totalITBIS
                       + impuestosAdicionales
        };
    }
}
```

### 6.3 Validaciones de ITBIS

- ITBIS1, ITBIS2, ITBIS3 son porcentajes enteros (18, 16, 0)
- Los totales de ITBIS deben coincidir con el cálculo
- MontoTotal = Suma de todos los gravados + exentos + ITBIS + impuestos adicionales
- Si `IndicadorMontoGravado = 1`, los montos en las líneas ya incluyen ITBIS (se debe desglosar)

---

## 7. Codificación de Impuestos Adicionales

Impuestos selectivos al consumo (ISC) según el XSD:

| Código | Descripción | Aplica a |
|---|---|---|
| 001 | Propina Legal | |
| 002 | Contribución Telecomunicaciones | |
| 003 | ISC Servicios Seguros | |
| 004 | ISC Servicios Telecomunicaciones | |
| 005 | ISC Expedición primera placa | |
| 006-010 | ISC Bebidas (específico) | Cerveza, vinos, etc. |
| 011-018 | ISC Alcohol y Licores (específico) | |
| 019-022 | ISC Cigarrillos (específico) | |
| 023-039 | ISC (AdValorem) | Cerveza, vinos, alcohol, licores, cigarrillos |

**Cálculo:** `MontoISC = Tasa × BaseImponible` (para AdValorem) o `MontoISC = TasaEspecifica` (para específico)

---

## 8. Unidades de Medida

Códigos DGII (1-62). Los más usados en POS:

| Código | Descripción | Abreviatura |
|---|---|---|
| 21 | Kilogramo | KG |
| 24 | Litro | LT |
| 34 | Pieza | PZA |
| 43 | Unidad | UND |
| 13 | Docena | DOC |
| 45 | Millar | ML |
| 6 | Caja/Cajón | CAJ |
| 7 | Cajetilla | CAJET |
| 8 | Centímetro | CM |
| 23 | Libra | LB |
| 26 | Metro | M |
| 27 | Metro cuadrado | M2 |
| 28 | Metro cúbico | M3 |
| 17 | Gramo | GR |
| 55 | Pulgadas | PULG |
| 12 | Día | DÍA |
| 19 | Hora | HOR |
| 30 | Minuto | MIN |

---

## 9. Provincias y Municipios de República Dominicana

El XSD incluye todos los códigos de la tabla de provincias y municipios (61 provincias). Formato: `PP0000` (provincia) o `PP0000` (municipio específico).

Ejemplos:
- `010000` = Distrito Nacional
- `010100` = Municipio Santo Domingo de Guzmán
- `010101` = Santo Domingo de Guzmán (D. M.)
- `100000` = Provincia Independencia
- `100100` = Municipio Jimaní

La lista completa está en el XSD `e-CF 32 v.1.0.xsd` (líneas 783-1707).

---

## 10. Formas de Pago

| Código | Descripción |
|---|---|
| 1 | Efectivo |
| 2 | Cheque / Transferencia / Depósito |
| 3 | Tarjeta de Débito / Crédito |
| 4 | Venta a Crédito |
| 5 | Bonos o Certificados de regalo |
| 6 | Permuta |
| 7 | Nota de crédito |
| 8 | Otras formas de pago |

---

## 11. Tipos de Pago

| Código | Descripción |
|---|---|
| 1 | Contado |
| 2 | Crédito |
| 3 | Gratuito |

---

## 12. Tipos de Ingresos

| Código | Descripción |
|---|---|
| 01 | Ingresos por operaciones (No financieros) |
| 02 | Ingresos Financieros |
| 03 | Ingresos Extraordinarios |
| 04 | Ingresos por Arrendamientos |
| 05 | Ingresos por Venta de Activo Depreciable |
| 06 | Otros Ingresos |

---

## 13. Interfaces Clave

### IElectronicInvoiceService.cs

```csharp
public interface IElectronicInvoiceService
{
    Task<ElectronicInvoiceResult> SubmitAsync(
        Invoice invoice,
        CancellationToken cancellationToken = default);

    Task<AnulacionResult> AnularAsync(
        string eNCF,
        string tipoeCF,
        IEnumerable<string> secuenciasAnular,
        CancellationToken cancellationToken = default);

    Task<AcuseRecibo> GetAcuseReciboAsync(string eNCF, CancellationToken cancellationToken);
    Task<AprobacionComercial> GetAprobacionComercialAsync(string eNCF, CancellationToken cancellationToken);
}
```

### IInvoiceRepository.cs

```csharp
public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(int id);
    Task<Invoice> SaveAsync(Invoice invoice);
    Task<IEnumerable<Invoice>> GetByRNCAsync(string rnc, int? tipoeCF = null);
    Task<IEnumerable<Invoice>> GetByDateRangeAsync(DateOnly from, DateOnly to);
    Task<IEnumerable<Invoice>> GetPendingAsync();
}
```

### IXmlValidator.cs

```csharp
public interface IXmlValidator
{
    XmlValidationResult ValidateAgainstSchema(string xml, string xsdPath);
    Task<XmlValidationResult> ValidateAsync(string xml, string xsdPath);
}
```

---

## 14. Estructura de Base de Datos

### Tables del Módulo de Facturación Electrónica

```sql
CREATE TABLE ElectronicInvoices (
    Id INT IDENTITY PRIMARY KEY,
    InvoiceId INT NOT NULL,                    -- FK a Invoices (venta)
    TipoeCF INT NOT NULL,                      -- 31, 32, 33, etc.
    eNCF VARCHAR(13) NOT NULL,                 -- Número de comprobante fiscal
    Version DECIMAL(2,1) NOT NULL,             -- 1.0
    RNCBinary VARCHAR(11) NOT NULL,           -- RNC del emisor (con 0 inicial)
    RNCComprador VARCHAR(11) NULL,
    RazonSocialComprador VARCHAR(150) NULL,
    FechaEmision DATE NOT NULL,
    TipoPago INT NOT NULL,
    TipoIngresos VARCHAR(2) NOT NULL,
    MontoGravadoTotal DECIMAL(18,2) NOT NULL,
    MontoExento DECIMAL(18,2) NOT NULL,
    TotalITBIS DECIMAL(18,2) NOT NULL,
    MontoTotal DECIMAL(18,2) NOT NULL,
    XMLContent NVARCHAR(MAX) NOT NULL,         -- XML completo generado
    XMLHash VARCHAR(64) NOT NULL,              -- Hash del XML
    Estado INT NOT NULL,                       -- 0=Enviado, 1=Aprobado, 2=Rechazado, 3=Anulado
    FechaEnvio DATETIME NOT NULL,
    FechaAprobacion DATETIME NULL,
    FechaAnulacion DATETIME NULL,
    MotivoAnulacion VARCHAR(250) NULL,
    ARECFXML NVARCHAR(MAX) NULL,
    ACECFXML NVARCHAR(MAX) NULL,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL
);

CREATE TABLE ElectronicInvoiceItems (
    Id INT IDENTITY PRIMARY KEY,
    ElectronicInvoiceId INT NOT NULL,
    NumeroLinea INT NOT NULL,
    IndicadorFacturacion INT NOT NULL,
    IndicadorBienoServicio INT NOT NULL,
    NombreItem VARCHAR(80) NOT NULL,
    DescripcionItem VARCHAR(1000) NULL,
    Cantidad DECIMAL(18,2) NOT NULL,
    UnidadMedida INT NULL,
    PrecioUnitario DECIMAL(20,4) NOT NULL,
    DescuentoMonto DECIMAL(18,2) NULL,
    RecargoMonto DECIMAL(18,2) NULL,
    MontoItem DECIMAL(18,2) NOT NULL,
    FOREIGN KEY (ElectronicInvoiceId) REFERENCES ElectronicInvoices(Id)
);
```

---

## 15. Pruebas Requeridas

### Domain Tests
- ✓ Cálculo correcto de ITBIS 18% sobre monto gravado
- ✓ Cálculo correcto de ITBIS 16% sobre artículos de primera necesidad
- ✓ Cálculo correcto de total (gravados + exentos + ITBIS + adicionales)
- ✓ Validación RNC formato (9 o 11 dígitos)
- ✓ Validación eNCF formato (13 caracteres alfanuméricos)
- ✓ Validación fecha emisión formato DD-MM-AAAA
- ✓ Validación código de unidad de medida válido
- ✓ Validación código de provincia/municipio válido

### Application Tests
- ✓ CreateElectronicInvoiceCommand → constructora XML correcta
- ✓ Validación de que el XML cumple con el XSD
- ✓ Cálculo de hash de seguridad (CodigoSeguridadeCF)
- ✓ Enviar factura → recibir ARECF con estado "Recibido"
- ✓ Anular factura → XML de ANECF correcto
- ✓ Consultar estado → ACECF con estado Aceptado/Rechazado

### Integration Tests
- ✓ Serialización completa de Sale → XML e-CF 32 válido
- ✓ Serialización de Sale → XML e-CF 31 (crédito fiscal)
- ✓ Persistencia de factura electrónica en BD
- ✓ Escenario completo: crear venta → emitir factura → recibir acuse

---

## 16. Consideraciones de Implementación

### 16.1 Generación de eNCF (Número de Comprobante)

El eNCF tiene formato: `[Serie][6 dígitos][checksum]` = 13 caracteres.

Series disponibles: B, C, D, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z

Para el POS local, se genera secuencialmente por día o por sucursal. El sistema debe:
1. Obtener el último eNCF usado para la serie actual
2. Incrementar secuencia
3. Generar el nuevo eNCF

### 16.2 Firma Digital

Los XSD indican que el documento debe estar firmado digitalmente con un certificado oficial de la DGII. Este es un paso pendiente que requiere:

1. Certificado digital A1/A3 de la DGII
2. Librería de firma (puede ser .NET estándar con XMLDSig)
3. Integración con el servicio de firma

Para la versión inicial, se puede implementar:
- Generación del XML sin firma
- Validación estructural contra XSD
- Hash de seguridad calculado
- Firma digital como mejora futura

### 16.3 Envío a DGII

El envío se realiza mediante el servicio web de la DGII. La infraestructura debe:

1. Serializar el XML
2. Firmarlo digitalmente
3. Enviarlo vía SOAP o HTTP POST al endpoint DGII
4. Recibir el ARECF (acuse de recibo inmediato)
5. Polling para obtener ACECF (aprobación comercial)
6. Persistir ambos documentos

### 16.4 Contingencia

Si DGII no está disponible, el sistema debe:
- Permitir emitir factura en formato "contingencia" (RFCE)
- Guardar factura pendiente para envío posterior
- Marcar como "pendiente de envío" en el estado

Los XSD de RFCE 32 están provistos para este caso.
