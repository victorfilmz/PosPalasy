# Documentación Técnica - Sistema POS Facturación Electrónica DGII

**Versión:** 1.1  
**Fecha:** 16/09/2026  
**Stack:** .NET 10 / ASP.NET Core MVC / Clean Architecture / SQL Server / EF Core
**Estado:** Revisado y corregido — Exclusivo República Dominicana

---

## 1. Visión General

El sistema es un POS local para punto de venta con módulo de facturación electrónica conforme a los **esquemas XSD oficiales de la Dirección General de Impuestos (DGII) de la República Dominicana**.

### 1.1 Marco Legal

- **Ley No. 32-23** de Facturación Electrónica de la República Dominicana — promulgada el 16 de mayo de 2023
- **Decreto No. 587-24** — Reglamento para la aplicación de la Ley 32-23
- **Norma General No. 01-2020** — regula emisión y uso de Comprobantes Fiscales Electrónicos (e-CF)

**Importante:** La facturación electrónica es OBLIGATORIA para contribuyentes según calendario de implementación:
- Grandes Contribuyentes Nacionales: 15 de mayo de 2024 (12 meses desde ley)
- Grandes Contribuyentes Locales y Medianos: 15 de mayo de 2025 (24 meses)  
- Pequeños, Micro y no Clasificados: 15 de mayo de 2026 (36 meses)
- Instituciones del Estado: según clasificación (hasta 15 de mayo de 2026)

### 1.2 Esquemas XSD Provistos

| Tipo | Archivo | Descripción |
|------|---------|-------------|
| e-CF tipo 32 | `e-CF 32 v.1.0.xsd` | Factura de Consumo Electrónica (cliente final) — esquema más completo |
| e-CF tipo 31 | `e-CF 31 v.1.0.xsd` | Factura de Crédito Fiscal Electrónica (ventas con derecho de crédito) |
| e-CF tipo 41 | `e-CF 41 v.1.0.xsd` | Compras Electrónicas |
| e-CF tipo 43-47 | `e-CF 43..47 v.1.0.xsd` | Gastos menores, regímenes especiales, gubernamental, exportaciones, pagos al exterior |
| RFCE tipo 32 | `RFCE 32 v.1.0.xsd` | Factura de consumo simplificada (contingencia) |
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

```
Application
    │
    ├── ElectronicInvoicing/
    │   ├── Commands/
    │   │   ├── EmitirFacturaElectronicaCommand.cs
    │   │   ├── AnularFacturaElectronicaCommand.cs
    │   │   └── ConsultarEstadoFacturaCommand.cs
    │   ├── Handlers/
    │   │   ├── EmitirFacturaElectronicaHandler.cs
    │   │   ├── AnularFacturaElectronicaHandler.cs
    │   │   └── ConsultarEstadoFacturaHandler.cs
    │   ├── DTOs/
    │   │   ├── EInvoiceDTO.cs
    │   │   ├── EInvoiceLineDTO.cs
    │   │   └── InvoiceStatusDTO.cs
    │   └── Validators/
    │       └── EmitirFacturaElectronicaValidator.cs
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
ElectronicInvoiceService.EmitirAsync(invoice)
    │
    ├── 1. Construir objeto EInvoiceDTO desde Sale
    ├── 2. Validar reglas DGII (RNC, montos, impuestos)
    ├── 3. Serializar a XML conforme e-CF 32 XSD
    ├── 4. Validar XML contra XSD (XmlValidator)
    ├── 5. Calcular Hash de seguridad (CodigoSeguridadeCF)
    ├── 6. Firmar digitalmente (con certificado A1/A3)
    ├── 7. Enviar a DGII mediante REST API (JSON)
    ├── 8. Recibir TrackId de DGII
    ├── 9. Consultar resultado con TrackId (Aceptado/Rechazado/En Proceso)
    ├── 10. Recibir ARECF (acuse de recibo)
    ├── 11. Recibir ACECF (aprobación comercial)
    └── 12. Persistir resultado + XML en BD
    │
    ▼
Venta finalizada, factura impresa, XML guardado
```

---

## 4. Mapeo XSD → Entidades C#

### 4.1 Tipos de Comprobante (TipoeCFType / CFType)

```csharp
public enum TipoeCFType : int
{
    FacturaCreditoFiscal = 31,      // e-CF 31 - Ventas con derecho de crédito fiscal
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
|-----------|---------|-------------|-------------|
| `Version` | `decimal` (1.0) | Sí | Versión del formato |
| `TipoeCF` | `TipoeCFType` | Sí | Tipo de comprobante (31-47) |
| `eNCF` | `string` (13 chars alfanuméricos) | Sí | Número de Comprobante Fiscal |
| `IndicadorEnvioDiferido` | `int?` (1) | No | 1 = envío diferido autorizado |
| `IndicadorMontoGravado` | `int?` (0 o 1) | No | 0 = ITBIS no incluido, 1 = ITBIS incluido |
| `IndicadorServicioTodoIncluido` | `int?` (1) | No | 1 = servicio todo incluido |
| `TipoIngresos` | `string` (01-06) | Sí | 01=Ingresos Operaciones, 02=Ingresos Financieros, 03=Ingresos Extraordinarios, 04=Ingresos Arrendamientos, 05=Venta Activo Depreciable, 06=Otros Ingresos |
| `TipoPago` | `int` (1-3) | Sí | 1=Contado, 2=Crédito, 3=Gratuito |
| `FechaLimitePago` | `DateOnly?` | No | Fecha límite de pago (crédito) |
| `TerminoPago` | `string?` (max 15) | No | Término de pago |
| `TablaFormasPago` | `List<FormaDePagoDTO>` | No | Hasta 7 formas de pago |
| `TipoCuentaPago` | `string?` (CT/AH/OT) | No | Tipo de cuenta: CT=Cuenta Corriente, AH=Ahorro, OT=Otra |
| `NumeroCuentaPago` | `string?` (max 28) | No | Número de cuenta |
| `BancoPago` | `string?` (max 75) | No | Banco |
| `FechaDesde` | `DateOnly?` | No | Fecha desde |
| `FechaHasta` | `DateOnly?` | No | Fecha hasta |
| `TotalPaginas` | `int?` (>1) | No | Total de páginas |

### 4.3 Emisor

| Campo XSD | Tipo C# | Obligatorio | Notas |
|-----------|---------|-------------|-------|
| `RNCEmisor` | `string` (9 o 11 dígitos) | **Sí** | RNC del emisor (empresa=11 dígitos, persona física=9 dígitos) |
| `RazonSocialEmisor` | `string` (max 150) | **Sí** | Razón social del emisor |
| `NombreComercial` | `string` (max 150) | No | Nombre comercial |
| `Sucursal` | `string` (max 20) | No | Sucursal |
| `DireccionEmisor` | `string` (max 100) | **Sí** | Dirección del emisor |
| `Municipio` | `ProvinciaMunicipioType` | No | Código municipio (6 dígitos) |
| `Provincia` | `ProvinciaMunicipioType` | No | Código provincia (6 dígitos) |
| `TelefonoEmisor` | `List<string>` (hasta 3) | No | Formato XXX-XXX-XXXX |
| `CorreoEmisor` | `string` (email, max 80) | No | Correo electrónico |
| `WebSite` | `string` (max 50) | No | Sitio web |
| `ActividadEconomica` | `string` (max 100) | No | Actividad económica |
| `CodigoVendedor` | `string` (max 60) | No | Código de vendedor |
| `NumeroFacturaInterna` | `string` (max 20) | No | Número de factura interna |
| `NumeroPedidoInterno` | `int?` (max 20 dígitos) | No | Número de pedido interno |
| `ZonaVenta` | `string` (max 20) | No | Zona de venta |
| `RutaVenta` | `string` (max 20) | No | Ruta de venta |
| `InformacionAdicionalEmisor` | `string` (max 250) | No | Información adicional |
| `FechaEmision` | `DateOnly` | **Sí** | Fecha de emisión (formato DD-MM-AAAA) |

### 4.4 Comprador

| Campo XSD | Tipo C# | Obligatorio | Notas |
|-----------|---------|-------------|-------|
| `RNCComprador` | `string` (9/11 dígitos) | Depende | Obligatorio para e-CF 32 a empresa (RNC válido) |
| `IdentificadorExtranjero` | `string` (max 20) | Depende | Si comprador es extranjero sin RNC |
| `RazonSocialComprador` | `string` (max 150) | Depende | Nombre/razón social |
| `ContactoComprador` | `string` (max 80) | No | Persona de contacto |
| `CorreoComprador` | `string` (email) | No | Correo electrónico |
| `DireccionComprador` | `string` (max 100) | No | Dirección |
| `MunicipioComprador` | `ProvinciaMunicipioType` | No | Código municipio |
| `ProvinciaComprador` | `ProvinciaMunicipioType` | No | Código provincia |
| `FechaEntrega` | `DateOnly?` | No | Fecha de entrega |
| `ContactoEntrega` | `string` (max 100) | No | Contacto para entrega |
| `DireccionEntrega` | `string` (max 100) | No | Dirección de entrega |
| `TelefonoAdicional` | `string` (teléfono) | No | Teléfono adicional |
| `FechaOrdenCompra` | `DateOnly?` | No | Fecha de orden de compra |
| `NumeroOrdenCompra` | `string` (max 20) | No | Número de orden de compra |
| `CodigoInternoComprador` | `string` (max 20) | No | Código interno del comprador |
| `ResponsablePago` | `string` (max 20, alfa) | No | Responsable de pago |
| `InformacionAdicionalComprador` | `string` (max 150) | No | Información adicional |

**Regla:** Para e-CF tipo 32 (Factura de Consumo) el comprador es obligatorio y debe tener RNC si es empresa dominicana. Si es persona física sin RNC, se usa otra identificación.

### 4.5 Items / Líneas de Producto

Cada item en `DetallesItems/Item`:

| Campo XSD | Tipo C# | Obligatorio | Notas |
|-----------|---------|-------------|-------|
| `NumeroLinea` | `int` (1-1000) | **Sí** | Número de línea secuencial |
| `TablaCodigosItem` | `List<CodigoItemDTO>` | No | Hasta 5 códigos por item |
| `IndicadorFacturacion` | `int` (0-4) | **Sí** | 0=NoFacturable18%, 1=ITBIS18%, 2=ITBIS16%, 3=ITBIS0%, 4=Exento |
| `NombreItem` | `string` (max 80) | **Sí** | Nombre del producto/servicio |
| `IndicadorBienoServicio` | `int` (1=Biens, 2=Servicio) | **Sí** | 1 = Bien, 2 = Servicio |
| `DescripcionItem` | `string` (max 1000) | No | Descripción |
| `CantidadItem` | `decimal` (≥0) | **Sí** | Cantidad |
| `UnidadMedida` | `int` (1-62) | No | Código DGII de unidad de medida |
| `CantidadReferencia` | `decimal` (≥0) | No | Cantidad de referencia |
| `UnidadReferencia` | `int` | No | Unidad de medida de referencia |
| `TablaSubcantidad` | `List<SubcantidadDTO>` | No | Hasta 5 subcantidades |
| `GradosAlcohol` | `decimal` (≥0) | No | Para bebidas alcohólicas |
| `FechaElaboracion` | `DateOnly?` | No | Fecha de elaboración |
| `FechaVencimientoItem` | `DateOnly?` | No | Fecha de vencimiento |
| `Mineria` | `MineriaDTO?` | No | Sector minero |
| `PrecioUnitarioItem` | `decimal` (≥0) | **Sí** | Precio unitario |
| `DescuentoMonto` | `decimal` (≥0) | No | Descuento global del item |
| `TablaSubDescuento` | `List<SubDescuentoDTO>` | No | Hasta 12 subdescuentos |
| `RecargoMonto` | `decimal` (≥0) | No | Recargo |
| `TablaSubRecargo` | `List<SubRecargoDTO>` | No | Hasta 12 subrecargos |
| `TablaImpuestoAdicional` | `List<ImpuestoAdicionalDTO>` | No | Hasta 2 por item |
| `OtraMonedaDetalle` | `OtraMonedaDetalleDTO?` | No | Si se usa moneda extranjera |
| `MontoItem` | `decimal` (≥0) | **Sí** | Monto total del item |

### 4.6 Totales

| Campo XSD | Tipo C# | Obligatorio | Notas |
|-----------|---------|-------------|-------|
| `MontoGravadoTotal` | `decimal` (≥0) | No | Total gravado |
| `MontoGravadoI1` | `decimal` (≥0) | No | Gravado ITBIS 18% |
| `MontoGravadoI2` | `decimal` (≥0) | No | Gravado ITBIS 16% |
| `MontoGravadoI3` | `decimal` (≥0) | No | Gravado ITBIS 0% |
| `MontoExento` | `decimal` (≥0) | No | Monto exento |
| `ITBIS1` | `int` | No | Porcentaje ITBIS 18% (18) |
| `ITBIS2` | `int` | No | Porcentaje ITBIS 16% (16) |
| `ITBIS3` | `int` | No | Porcentaje ITBIS 0% (0) |
| `TotalITBIS` | `decimal` (≥0) | No | Total ITBIS |
| `TotalITBIS1` | `decimal` (≥0) | No | ITBIS 18% total |
| `TotalITBIS2` | `decimal` (≥0) | No | ITBIS 16% total |
| `TotalITBIS3` | `decimal` (≥0) | No | ITBIS 0% total |
| `MontoImpuestoAdicional` | `decimal` (≥0) | No | Monto de impuestos adicionales |
| `ImpuestosAdicionales` | `List<ImpuestoAdicionalDTO>` | No | Hasta 20 impuestos adicionales |
| `MontoTotal` | `decimal` (≥0) | **Sí** | Total de la factura |
| `MontoNoFacturable` | `decimal` | No | Monto no facturable |
| `MontoPeriodo` | `decimal` | No | Monto de periodo |
| `SaldoAnterior` | `decimal` | No | Saldo anterior |
| `MontoAvancePago` | `decimal` (≥0) | No | Monto de avance de pago |
| `ValorPagar` | `decimal` | No | Valor a pagar |

### 4.7 Variaciones por Tipo de Comprobante

**e-CF 31 (Factura de Crédito Fiscal):**
- Agrega `FechaVencimientoSecuencia` obligatorio
- No tiene `IndicadorServicioTodoIncluido`

**e-CF 41 (Compras):**
- Comprador es obligatorio siempre
- No tiene `TablaSubDescuento` ni `TablaSubRecargo` en items

**e-CF 47 (Pagos al Exterior):**
- Comprador no es obligatorio
- Agrega `PaisDestino` en Transporte

---

## 5. Flujo de Envío a DGII (CORRECTO - República Dominicana)

**Documentación oficial DGII:** https://dgii.gov.do/cicloContribuyente/facturacion/comprobantesFiscalesElectronicosE-CF/

### 5.1 Proceso de Certificación DGII (Requerido)

Antes de emitir facturas electrónicas, el contribuyente DEBE completar el proceso de certificación:

1. Estar inscrito en el RNC
2. Estar al día en obligaciones tributarias
3. Contar con certificado digital para procesos tributarios
4. Contar con software para emisión de e-CF
5. Completar Formulario de Solicitud vía OFV (Oficina Virtual de DGII)
6. Aprobar proceso de certificación en Portal de Certificación

El proceso incluye:
- Registro de URLs de servicios (recepción, aprobación, autenticación)
- Set de pruebas con sandbox DGII
- Validación de certificados digitales
- Recepción de e-CF de prueba
- Aprobación final como emisor electrónico

### 5.2 Servicios Web DGII (REST API - JSON)

La DGII utiliza RESTful API con JSON para los servicios de facturación electrónica:

```
Servicio de Recepción (Emisor envía e-CF):
  POST https://[host]/fe/recepcion/api/ecf
  Content-Type: application/json
  
Servicio de Aprobación Comercial (DGII envía ACECF):
  POST https://[host]/fe/aprobacioncomercial/api/ecf
  Content-Type: application/json

Servicio de Autenticación:
  POST https://[host]/fe/autenticacion/api/[semilla]/[validacioncertificado]
  Content-Type: application/json
```

Las URLs se registran durante el proceso de certificación del contribuyente.

### 5.3 Flujo de Envío Completo

```
1. Serializar factura → XML e-CF 32 (conforme XSD)
2. Validar XML contra XSD (XmlValidator)
3. Calcular Hash de seguridad (CodigoSeguridadeCF - SHA256 → 6 chars base64)
4. Firmar XML digitalmente (XML-DSig con certificado A1/A3 INDOTEL)
5. Enviar a DGII vía REST API (JSON):
   POST /fe/recepcion/api/ecf
   Body: { "xml": "<base64 del XML>", "hash": "ABCD12" }
6. Recibir respuesta de DGII con TrackId y estado
7. Consultar resultado con TrackId ( polling ):
   GET /fe/recepcion/api/ecf/resultado/{trackId}
   Estados: Aceptado, Rechazado, Aceptado Condicional, En Proceso
8. Si aprobado → generar ACECF (Aprobación Comercial) automáticamente
9. ARECF (Acuse de Recibo) se recibe como respuesta inicial
10. Persistir resultados en BD
```

### 5.4 Estados del e-CF según DGII

| Estado | Descripción |
|--------|-------------|
| **En Proceso** | Factura recibida, en validación |
| **Aceptado** | Cumple con características e-CF, es válido |
| **Aceptado Condicional** | Irregularidades menores, corregir en futuras |
| **Rechazado** | No cumple requisitos o tiene errores |
| **Anulado** | Anulado mediante ANECF |

### 5.5 Ejemplo de JSON Request a DGII

```json
{
  "xml": "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0iVVRGLTgiPz4...",
  "hash": "ABCD12"
}
```

### 5.6 Ejemplo de JSON Response de DGII

```json
{
  "trackId": "TRACK-123456789",
  "estado": "En Proceso",
  "fechaHora": "16-09-2026 14:30:00",
  "mensaje": "Documento recibido satisfactoriamente"
}
```

### 5.7 Consulta de Resultado (con TrackId)

```json
GET /fe/recepcion/api/ecf/resultado/TRACK-123456789

Response:
{
  "trackId": "TRACK-123456789",
  "estado": "Aceptado",
  "fechaHora": "16-09-2026 14:35:00",
  "acuseRecibo": "<xml ARECF>"
}
```

---

## 6. Certificado Digital y Firma XML

### 6.1 Certificado Digital DGII

Para emitir e-CF se requiere un certificado digital válido:
- **Tipo A1**: Archivo .pfx/.p12 con clave privada (software-based, fácil de implementar)
- **Tipo A3**: Tarjeta inteligente + lector (hardware-based, más seguro)

El certificado debe ser emitido por una entidad certificadora autorizada por **INDOTEL** (Instituto Dominicano de las Telecomunicaciones).

### 6.2 Firma XML-DSig (XML Digital Signature)

La firma digital se aplica sobre el XML del e-CF usando el estándar XML-DSig:

```csharp
// Ejemplo básico de firma con .NET
var xmlDoc = new XmlDocument();
xmlDoc.LoadXml(xmlContent);

var signedXml = new SignedXml(xmlDoc);
signedXml.SigningKey = certificate.PrivateKey;

var reference = new Reference();
reference.Uri = ""; // firma todo el documento
reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
signedXml.AddReference(reference);

signedXml.ComputeSignature();
var signature = signedXml.GetXml();

xmlDoc.DocumentElement.AppendChild(xmlDoc.ImportNode(signature, true));
return xmlDoc.OuterXml;
```

### 6.3 CodigoSeguridadeCF (Hash de Seguridad)

El hash de seguridad es obligatorio y se calcula así:
1. Tomar el XML del e-CF completo
2. Aplicar SHA-256 al XML (compactado, sin espacios)
3. Convertir a base64
4. Tomar los primeros 6 caracteres

Ejemplo (C#):
```csharp
using var sha256 = SHA256.Create();
var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(xmlCompact));
var base64 = Convert.ToBase64String(bytes);
var hash = base64[..6].ToUpperInvariant(); // 6 caracteres
```

### 6.4 QR Code

Las facturas electrónicas deben incluir un QR Code que permita verificar la autenticidad del comprobante en el portal de la DGII.

---

## 7. Mapeo XSD → C# (Documentación Detallada en 02_Mapeo_XSD_CSharp.md)

### 7.1 Tipos Simples (ValueObjects)

| XSD Type | C# Type | Descripción |
|----------|---------|-------------|
| RNC (9/11 dígitos) | `RNC` (record) | ValueObject con validación |
| eNCF (13 alfanuméricos) | `eNCF` (record) | ValueObject con validación |
| Fecha (DD-MM-AAAA) | `DateOnly` | Fecha de emisión |
| ProvinciaMunicipio (6 dígitos) | `string` | Código de provincia/municipio |
| TipoIngresos (01-06) | `string` | Código de tipo de ingreso |

### 7.2 Tipos Decimales Especiales

| XSD Type | C# Type | Restricción |
|----------|---------|-------------|
| Decimal18D1 or2MayorIgualCero | `decimal` | ≥ 0, max 18 dígitos, 1-2 decimales |
| Decimal18D2MayorIgualCero | `decimal` | ≥ 0, max 18 dígitos, 2 decimales |
| Decimal20D1or4MayorIgualCero | `decimal` | ≥ 0, max 20 dígitos, 1 o 4 decimales |
| Decimal19D1or3MayorIgualCero | `decimal` | ≥ 0, max 19 dígitos, 1 o 3 decimales |
| Decimal7D1or4MayorIgualCero | `decimal` | ≥ 0, 1 o 4 decimales |
| DecimalMontoNegativoPositivo | `decimal` | Puede ser negativo o positivo |

### 7.3 Tipos Alfanuméricos

| Tipo | Longitud | Descripción |
|------|----------|-------------|
| AlfaNum150 | max 150 | Razón social, nombre comercial |
| AlfaNum100 | max 100 | Dirección, actividad económica |
| AlfaNum80 | max 80 | NombreProducto, contacto |
| AlfaNum60 | max 60 | Código vendedor |
| AlfaNum50 | max 50 | Sitio web |
| AlfaNum250 | max 250 | Información adicional |
| AlfaNum20 | max 20 | Sucursal, zona, ruta, código interno |
| AlfaNum25 | max 25 | Término de pago |
| AlfaNum7 | max 7 | Teléfono (XXX-XXX) |
| AlfaNum10 | max 10 | Teléfono (XXX-XXXX) |
| AlfaNum13 | 13 exactos | eNCF |
| AlfaNum11 | 9 o 11 | RNC |

### 7.4 Tipos de Entidad Complejos

| XSD ComplexType | C# Class | Propósito |
|-----------------|----------|-----------|
| Encabezado | `EncabezadoECF32Dto` | Encabezado de factura |
| Emisor | `EmisorDto` | Datos del emisor |
| Comprador | `CompradorDto` | Datos del comprador |
| Totales | `TotalesDto` | Totales e impuestos |
| Item | `ItemDto` | Línea de producto |
| DetallesItems | `DetallesItemsDto` | Colección de items |
| TablaFormasPago | `List<FormaDePagoDto>` | Formas de pago |
| Transporte | `TransporteDto` | Información de transporte |
| InformacionesAdicionales | `InformacionesAdicionalesDto` | Info adicional de embarque |
| OtraMoneda | `OtraMonedaDto` | Moneda extranjera |
| Subtotales | `SubtotalesDto` | Subtotales |
| DescuentosORecargos | `DescuentosORecargosDto` | Descuentos/recargos globales |
| Paginacion | `PaginacionDto` | Paginación |
| InformacionReferencia | `InformacionReferenciaDto` | Referencia a otro comprobante |

### 7.5 Clases Auxiliares (DTOs)

| Clase | Propósito |
|-------|-----------|
| `CodigoItemDto` | Código de producto (hasta 5 por item) |
| `SubcantidadDto` | Subcantidad (hasta 5 por item) |
| `SubDescuentoDto` | Subdescuento (hasta 12 por item) |
| `SubRecargoDto` | Subrecargo (hasta 12 por item) |
| `ImpuestoAdicionalDto` | Impuesto adicional (hasta 2 por item o 20 global) |
| `FormaDePagoDto` | Forma de pago (hasta 7) |
| `MineriaDto` | Datos mineros (si aplica) |
| `OtraMonedaDetalleItemDto` | Detalle moneda extranjera por item |

---

## 8. Cálculo de ITBIS

### 8.1 Tablas de Impuestos DGII

| Concepto | Tasa | Aplica a |
|----------|------|----------|
| ITBIS General (Tasa 1) | 18% | La mayoría de bienes y servicios |
| ITBIS Primera Necesidad (Tasa 2) | 16% | Artículos de primera necesidad |
| ITBIS Tasa Cero (Tasa 3) | 0% | Gravados pero con tasa 0% |
| Exento | 0% | No sujeto al ITBIS |

### 8.2 Indicadores de Facturación (por Item)

| Código | Descripción | Calculation |
|--------|-------------|------------|
| 0 | No facturable (ITBIS 18% sobre margen) | No facturable pero ITBIS calculado sobre margen |
| 1 | ITBIS 18% | Gravado con tasa 18% |
| 2 | ITBIS 16% | Artículos de primera necesidad |
| 3 | ITBIS 0% (gravado) | Gravado pero con tasa 0% |
| 4 | Exento | No sujeto al ITBIS |

### 8.3 Lógica de Cálculo

```csharp
public class TaxCalculator
{
    public TaxBreakdown Calculate(IEnumerable<InvoiceLine> lines)
    {
        decimal montoGravadoI1 = 0; // 18% ITBIS
        decimal montoGravadoI2 = 0; // 16% ITBIS
        decimal montoGravadoI3 = 0; // 0% ITBIS (gravado)
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

### 8.4 Validaciones de ITBIS

- ITBIS1, ITBIS2, ITBIS3 son porcentajes enteros (18, 16, 0)
- Los totales de ITBIS deben coincidir con el cálculo
- MontoTotal = Suma de todos los gravados + exentos + ITBIS + impuestos adicionales
- Si `IndicadorMontoGravado = 1`, los montos en las líneas ya incluyen ITBIS (se debe desglosar)

---

## 9. Impuestos Adicionales (ISC - Impuesto Selectivo al Consumo)

### 9.1 Tipos de ISC según DGII

Los impuestos adicionales (ISC) se codifican con 3 dígitos:

| Código | Descripción | Tipo | Aplica a |
|--------|-------------|------|----------|
| 001 | Propina Legal | Específico | Propina legal |
| 002 | Contribución Telecomunicaciones | Específico | Servicios de telecomunicaciones |
| 003 | ISC Servicios Seguros | Específico | Servicios de seguros |
| 004 | ISC Servicios Telecomunicaciones | Específico | Servicios de telecomunicaciones |
| 005 | ISC Expedición primera placa | Específico | Expedición de primera placa |
| 006-018 | Bebidas y Alcoholes (específico) | Específico | Cerveza, vinos, alcohol, etc. |
| 019-022 | Cigarrillos (específico) | Específico | Cigarrillos |
| 023-039 | Bebidas y Alcoholes / Cigarrillos (AdValorem) | AdValorem | Cerveza, vinos, alcohol, licores, cigarrillos |

**Cálculo:**
- **AdValorem:** `MontoISC = Tasa × BaseImponible`
- **Específico:** `MontoISC = TasaEspecifica` (montos fijos por unidad o por cantidad)

---

## 10. Unidades de Medida (DGII)

Códigos 1-62 según DGII. Los más usados en POS:

| Código | Descripción | Abreviatura |
|--------|-------------|-------------|
| 21 | Kilogramo | KG |
| 24 | Litro | LT |
| 34 | Pieza | PZA |
| 43 | Unidad | UND |
| 13 | Docena | DOC |
| 45 | Millar | ML |
| 6 | Caja | CAJ |
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

La lista completa está en el XSD `e-CF 32 v.1.0.xsd`.

---

## 11. Provincias y Municipios de República Dominicana

El XSD incluye todos los códigos de la tabla de provincias y municipios (61 provincias). Formato: 6 dígitos `PP0000` (provincia) o `PP0000` (municipio específico).

Ejemplos:
- `010000` = Distrito Nacional
- `010100` = Municipio Santo Domingo de Guzmán
- `010101` = Santo Domingo de Guzmán (Distrito Municipio)
- `100000` = Provincia Independencia
- `100100` = Municipio Jimaní
- `240000` = Provincia Santiago
- `240100` = Municipio Santiago

---

## 12. Formas de Pago

| Código | Descripción |
|--------|-------------|
| 1 | Efectivo |
| 2 | Cheque / Transferencia / Depósito |
| 3 | Tarjeta de Débito / Crédito |
| 4 | Venta a Crédito |
| 5 | Bonos o Certificados de regalo |
| 6 | Permuta |
| 7 | Nota de crédito |
| 8 | Otras formas de pago |

---

## 13. Tipos de Pago

| Código | Descripción |
|--------|-------------|
| 1 | Contado |
| 2 | Crédito |
| 3 | Gratuito |

---

## 14. Tipos de Ingresos (según DGII)

| Código | Descripción |
|--------|-------------|
| 01 | Ingresos por operaciones (No financieros) |
| 02 | Ingresos Financieros |
| 03 | Ingresos Extraordinarios |
| 04 | Ingresos por Arrendamientos |
| 05 | Ingresos por Venta de Activo Depreciable |
| 06 | Otros Ingresos |

---

## 15. Documentos Secundarios DGII

### 15.1 ANECF — Anulación de NCF

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

### 15.2 ACECF — Aprobación Comercial

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

### 15.3 ARECF — Acuse de Recibo

```xml
<ARECF>
  <DetalleAcusedeRecibo>
    <Version>1.0</Version>
    <RNCEmisor>1234567890</RNCEmisor>
    <RNCComprador>0987654321</RNCComprador>
    <eNCF>B0000000000001</eNCF>
    <Estado>0</Estado>          <!-- 0=Recibido, 1=NoRecibido -->
    <CodigoMotivoNoRecibido>...</CodigoMotivoNoRecibido>  <!-- Si no recibido -->
    <FechaHoraAcuseRecibo>15-09-2026 14:32:00</FechaHoraAcuseRecibo>
  </DetalleAcusedeRecibo>
</ARECF>
```

**Códigos de motivo de no recibido:**
- 1 = Error de Especificación
- 2 = Error de Firma Digital
- 3 = Envío Duplicado
- 4 = RNC Comprador no Corresponde

---

## 16. Estructura de Base de Datos

### 16.1 Tablas del Módulo de Facturación Electrónica

```sql
CREATE TABLE ElectronicInvoices (
    Id INT IDENTITY PRIMARY KEY,
    SaleId INT NOT NULL,                    -- FK a ventas/Ordenes
    TipoeCF INT NOT NULL,                   -- 31, 32, 33, 34, 41, etc.
    eNCF VARCHAR(13) NOT NULL UNIQUE,      -- Número de Comprobante Fiscal
    Version DECIMAL(2,1) NOT NULL,         -- 1.0
    RNCEmisor VARCHAR(11) NOT NULL,        -- RNC del emisor
    RNCComprador VARCHAR(11) NULL,         -- RNC del comprador
    RazonSocialComprador VARCHAR(150) NULL,-- Razón social del comprador
    FechaEmision DATE NOT NULL,            -- Fecha de emisión
    TipoPago INT NOT NULL,                 -- 1=Contado, 2=Crédito, 3=Gratuito
    TipoIngresos CHAR(2) NOT NULL,         -- 01-06
    FechaLimitePago DATE NULL,             -- Fecha límite de pago (crédito)
    TerminoPago VARCHAR(15) NULL,          -- Término de pago
    TipoCuentaPago CHAR(2) NULL,           -- CT/AH/OT
    NumeroCuentaPago VARCHAR(28) NULL,     -- Número de cuenta
    BancoPago VARCHAR(75) NULL,            -- Banco
    IndicadorEnvioDiferido INT NULL,       -- 1 = envío diferido
    IndicadorMontoGravado INT NULL,        -- 0 o 1
    IndicadorServicioTodoIncluido INT NULL,-- 1 = todo incluido
    MontoGravadoTotal DECIMAL(18,2) NOT NULL,
    MontoGravadoI1 DECIMAL(18,2) NOT NULL,
    MontoGravadoI2 DECIMAL(18,2) NOT NULL,
    MontoGravadoI3 DECIMAL(18,2) NOT NULL,
    MontoExento DECIMAL(18,2) NOT NULL,
    ITBIS1 INT NULL,                       -- 18
    ITBIS2 INT NULL,                       -- 16
    ITBIS3 INT NULL,                       -- 0
    TotalITBIS DECIMAL(18,2) NOT NULL,
    TotalITBIS1 DECIMAL(18,2) NOT NULL,
    TotalITBIS2 DECIMAL(18,2) NOT NULL,
    TotalITBIS3 DECIMAL(18,2) NOT NULL,
    MontoImpuestoAdicional DECIMAL(18,2) NULL,
    MontoTotal DECIMAL(18,2) NOT NULL,    -- Total de la factura
    XMLContent NVARCHAR(MAX) NOT NULL,     -- XML completo del e-CF
    XMLHash CHAR(6) NOT NULL,              -- CodigoSeguridadeCF (6 chars)
    Estado INT NOT NULL,                   -- 0=EnProceso, 1=Aceptado, 2=Rechazado, 3=Anulado
    FechaEnvio DATETIME NOT NULL,
    FechaAprobacion DATETIME NULL,         -- Fecha de aprobación DGII
    FechaAnulacion DATETIME NULL,          -- Fecha de anulación
    MotivoRechazo NVARCHAR(250) NULL,      -- Motivo si rechazada
    MotivoAnulacion NVARCHAR(250) NULL,    -- Motivo si anulada
    ARECFXML NVARCHAR(MAX) NULL,           -- XML de acuse de recibo
    ACECFXML NVARCHAR(MAX) NULL,           -- XML de aprobación comercial
    CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_ElectronicInvoices_Sales FOREIGN KEY (SaleId) REFERENCES Sales(Id)
);

CREATE TABLE ElectronicInvoiceItems (
    Id INT IDENTITY PRIMARY KEY,
    ElectronicInvoiceId INT NOT NULL,
    NumeroLinea INT NOT NULL,
    IndicadorFacturacion INT NOT NULL,     -- 0-4
    IndicadorBienoServicio INT NOT NULL,   -- 1=Biens, 2=Servicio
    NombreItem VARCHAR(80) NOT NULL,
    DescripcionItem VARCHAR(1000) NULL,
    CantidadItem DECIMAL(18,2) NOT NULL,
    UnidadMedida INT NULL,
    CantidadReferencia DECIMAL(18,2) NULL,
    UnidadReferencia INT NULL,
    PrecioUnitarioItem DECIMAL(20,4) NOT NULL,
    DescuentoMonto DECIMAL(18,2) NULL,
    RecargoMonto DECIMAL(18,2) NULL,
    MontoItem DECIMAL(18,2) NOT NULL,
    GradosAlcohol DECIMAL(5,2) NULL,
    FechaElaboracion DATE NULL,
    FechaVencimientoItem DATE NULL,
    FOREIGN KEY (ElectronicInvoiceId) REFERENCES ElectronicInvoices(Id) ON DELETE CASCADE
);

CREATE TABLE ElectronicInvoiceContingency (
    Id INT IDENTITY PRIMARY KEY,
    SaleId INT NOT NULL,
    TipoeCF INT NOT NULL,
    eNCF VARCHAR(13) NOT NULL,
    Version DECIMAL(2,1) NOT NULL,
    RNCEmisor VARCHAR(11) NOT NULL,
    RNCComprador VARCHAR(11) NULL,
    RazonSocialComprador VARCHAR(150) NULL,
    FechaEmision DATE NOT NULL,
    TipoPago INT NOT NULL,
    TipoIngresos CHAR(2) NOT NULL,
    XMLContent NVARCHAR(MAX) NOT NULL,
    XMLHash CHAR(6) NOT NULL,
    FechaGuardado DATETIME NOT NULL,
    Reintentos INT NOT NULL DEFAULT 0,
    UltimoError NVARCHAR(500) NULL,
    FechaProximoReintento DATETIME NULL
);

CREATE INDEX IX_ElectronicInvoices_eNCF ON ElectronicInvoices(eNCF);
CREATE INDEX IX_ElectronicInvoices_Estado ON ElectronicInvoices(Estado);
CREATE INDEX IX_ElectronicInvoices_FechaEmision ON ElectronicInvoices(FechaEmision);
CREATE INDEX IX_ElectronicInvoices_RNC ON ElectronicInvoices(RNCEmisor);
CREATE INDEX IX_ElectronicInvoiceItems_ElectronicInvoiceId ON ElectronicInvoiceItems(ElectronicInvoiceId);
```

---

## 17. Pruebas Requeridas

### 17.1 Domain Tests (Unit Tests)

- ✅ Cálculo correcto de ITBIS 18% sobre monto gravado
- ✅ Cálculo correcto de ITBIS 16% sobre artículos de primera necesidad
- ✅ Cálculo correcto de total (gravados + exentos + ITBIS + adicionales)
- ✅ Validación RNC formato (9 o 11 dígitos)
- ✅ Validación eNCF formato (13 caracteres alfanuméricos)
- ✅ Validación fecha emisión formato DD-MM-AAAA
- ✅ Validación código de unidad de medida válido
- ✅ Validación código de provincia/municipio válido
- ✅ Validación hash de seguridad (CodigoSeguridadeCF) genera 6 caracteres

### 17.2 Application Tests

- ✅ Crear factura electrónica → XML correcta
- ✅ Validación de que el XML cumple con el XSD
- ✅ Cálculo de hash de seguridad (CodigoSeguridadeCF)
- ✅ Enviar factura → recibir TrackId y estado
- ✅ Anular factura → XML de ANECF correcto
- ✅ Consultar estado → ACECF con estado Aceptado/Rechazado

### 17.3 Integration Tests

- ✅ Serialización completa de Sale → XML e-CF 32 válido
- ✅ Serialización de Sale → XML e-CF 31 (crédito fiscal)
- ✅ Persistencia de factura electrónica en BD
- ✅ Escenario completo: crear venta → emitir factura → recibir acuse

---

## 18. Consideraciones de Implementación

### 18.1 Generación de eNCF

El eNCF tiene formato: `B` + 12 dígitos secuenciales.
Ejemplo: B0000000000001, B0000000000002, etc.

El sistema debe:
1. Almacenar el último eNCF usado por serie
2. Generar nuevos eNCF secuencialmente
3. Evitar duplicados (verificar en BD antes de usar)

La serie puede cambiar según tipo de contribuyente, pero para el POS local se usa serie "B" por defecto.

### 18.2 Firma Digital

Los XSD indican que el documento debe estar firmado digitalmente con un certificado oficial de la DGII.

**Para la versión inicial:**
- Generar XML sin firma (para desarrollo/test)
- Validación estructural contra XSD
- Hash de seguridad calculado
- Firma digital como mejora cuando esté disponible el certificado

**Para producción:**
- Certificado digital A1/A3 de INDOTEL
- Librería de firma XML-DSig
- Integración completa con el servicio de firma

### 18.3 Envío a DGII (REST API JSON)

El envío se realiza mediante REST API con JSON, no SOAP:

```
POST /fe/recepcion/api/ecf
{
  "xml": "<base64>",
  "hash": "ABCD12"
}
```

La infraestructura debe:
1. Serializar el XML
2. Firmarlo digitalmente (si hay certificado)
3. Enviarlo mediante HTTP POST con JSON
4. Recibir TrackId de DGII
5. Polling para obtener estado final
6. Persistir resultados en BD

### 18.4 Contingencia (RFCE 32)

Si DGII no está disponible, el sistema debe:
- Permitir emitir factura en formato "contingencia" (RFCE 32)
- Guardar factura pendiente para envío posterior
- Marcar como "pendiente de envío" en el estado
- Reintentar envío automáticamente cuando DGII vuelva a estar disponible

Los XSD de RFCE 32 están provistos para este caso.

### 18.5 Proceso de Certificación DGII (Importante)

Antes de poder emitir e-CF en producción, se requiere:
1. Estar inscrito en RNC
2. Estar al día con obligaciones tributarias
3. Contar con certificado digital (A1 o A3) de INDOTEL
4. Completar formulario de solicitud vía OFV
5. Aprobar proceso de certificación en Portal de Certificación DGII
6. Registrar URLs de servicios (recepción, aprobación, autenticación)

### 18.6 Seguridad

- Credenciales DGII almacenadas de forma segura (no en texto plano)
- Logs de toda comunicación con DGII (para auditoría)
- Protección de certificados digitales
- Backup de todos los XMLs generados (nunca perder factura emitida)

---

## 19. Documentación Relacionada

- [01_Resumen_General.md](01_Resumen_General.md) — Este documento (ervisión general)
- [02_Mapeo_XSD_CSharp.md](02_Mapeo_XSD_CSharp.md) — Mapeo completo de tipos XSD a C#
- [03_Interfaces_Servicios.md](03_Interfaces_Servicios.md) — Contratos de interfaces
- [04_Diseno_Infraestructura_DGII.md](04_Diseno_Infraestructura_DGII.md) — Diseño de infraestructura DGII
- [05_Plan_Implementacion.md](05_Plan_Implementacion.md) — Plan de implementación

---

## 20. Notas Finales

Este documento es una visión general del sistema, revisado y corregido para Republica Dominicana exclusivamente. Los detalles técnicos completos están en los otros documentos. La documentación está VIVA y debe actualizarse conforme avance la implementación y conforme DGII publique nuevas versiones de los esquemas XSD o cambie la API.

**Referencia oficial DGII:** https://dgii.gov.do/cicloContribuyente/facturacion/comprobantesFiscalesElectronicosE-CF/
