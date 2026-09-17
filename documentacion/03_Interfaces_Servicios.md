# Interfaces y Contratos de Servicios — Módulo de Facturación Electrónica

**Versión:** 1.0  
**Fecha:** 16/09/2026  
**Stack:** .NET 10 / Clean Architecture

---

## 1. Resumen

Este documento define los contratos de interfaces (Application Layer) que permiten separar completamente la lógica de negocio de la infraestructura. Estos contratos son implementados por la capa de Infrastructure (DgiiElectronicInvoiceService, XmlSerializer, etc.) y consumidos por la capa de Application (Command Handlers).

---

## 2. Contratos Principales

### 2.1 IElectronicInvoiceService

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Servicio principal para la facturación electrónica DGII.
/// Abincula la capa de aplicación con la infraestructura (DGII, BD, firma digital).
/// </summary>
public interface IElectronicInvoiceService
{
    /// <summary>
    /// Emite una factura electrónica y la envía a DGII.
    /// Retrasa el resultado con el XML generado, hash y estado.
    /// </summary>
    Task<ElectronicInvoiceResult> SubmitAsync(
        ElectronicInvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anula uno o más NCFs en un rango especificado.
    /// </summary>
    Task<AnulacionResult> AnularAsync(
        AnulacionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta el estado de una factura electrónica por su eNCF.
    /// </summary>
    Task<ElectronicInvoiceStatus> ConsultarEstadoAsync(
        string eNCF,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera el XML de una factura sin enviar (para contingencia o local).
    /// </summary>
    Task<string> GenerateXmlAsync(
        ElectronicInvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera el XML de anulación de NCFs.
    /// </summary>
    Task<string> GenerateAnulacionXmlAsync(
        AnulacionRequest request,
        CancellationToken cancellationToken = default);
}
```

### 2.2 IElectronicInvoiceRepository

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Repositorio para persistencia de facturas electrónicas en BD local.
/// </summary>
public interface IElectronicInvoiceRepository
{
    /// <summary>
    /// Obtiene una factura electrónica por su ID.
    /// </summary>
    Task<ElectronicInvoiceEntity?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Obtiene una factura electrónica por su eNCF.
    /// </summary>
    Task<ElectronicInvoiceEntity?> GetByENCFAsync(string enf, CancellationToken ct = default);

    /// <summary>
    /// Crea una nueva factura electrónica en BD.
    /// </summary>
    Task<ElectronicInvoiceEntity> CreateAsync(ElectronicInvoiceEntity invoice, CancellationToken ct = default);

    /// <summary>
    /// Obtiene facturas por RNC del emisor (con filtro opcional de tipo).
    /// </summary>
    Task<IEnumerable<ElectronicInvoiceEntity>> GetByRNCAsync(
        string rnc,
        int? tipoeCF = null,
        CancellationToken ct = default);

    /// <summary>
    /// Obtiene facturas por rango de fechas.
    /// </summary>
    Task<IEnumerable<ElectronicInvoiceEntity>> GetByDateRangeAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);

    /// <summary>
    /// Obtiene facturas pendientes de envío.
    /// </summary>
    Task<IEnumerable<ElectronicInvoiceEntity>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Actualiza el estado de una factura.
    /// </summary>
    Task UpdateEstadoAsync(
        int id,
        ElectronicInvoiceStatus nuevoEstado,
        CancellationToken ct = default);

    /// <summary>
    /// Verifica si un eNCF ya existe en BD.
    /// </summary>
    Task<bool> ExistsENCFAsync(string enf, CancellationToken ct = default);

    /// <summary>
    /// Obtiene el último eNCF generado para una serie específica (ej: "B").
    /// </summary>
    Task<string?> GetLastENCFAsync(string serie, CancellationToken ct = default);
}
```

### 2.3 ISaleRepository

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Repositorio para acceder a las ventas del POS.
/// Necesario para construir la factura electrónica a partir de la venta.
/// </summary>
public interface ISaleRepository
{
    Task<SaleEntity?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IEnumerable<SaleEntity>> GetPendingElectronicInvoicingAsync(CancellationToken ct = default);
}
```

---

## 3. Interfaces de Infraestructura

### 3.1 IXmlValidator

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Valida XML contra esquemas XSD.
/// </summary>
public interface IXmlValidator
{
    /// <summary>
    /// Valida un XML contra un esquema XSD.
    /// </summary>
    /// <param name="xml">Contenido XML a validar.</param>
    /// <param name="xsdPath">Ruta al archivo XSD.</param>
    /// <returns>Resultado de validación con errores y warnings.</returns>
    Task<XmlValidationResult> ValidateAsync(
        string xml,
        string xsdPath,
        CancellationToken ct = default);
}

public record XmlValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}
```

### 3.2 IXmlSerializer

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Serializa objetos DTO a XML conforme a XSDs DGII.
/// </summary>
public interface IXmlSerializer
{
    /// <summary>
    /// Serializa una factura electrónica a XML según el tipo de XSD.
    /// Tipos soportados: "ECF32", "ECF31", "RFCE32", "ANECF", "ACECF", "ARECF"
    /// </summary>
    Task<string> SerializeAsync(
        ElectronicInvoiceRequest request,
        string xsdType,   // "ECF32", "ECF31", "RFCE32"
        CancellationToken ct = default);

    /// <summary>
    /// Serializa una anulación (ANECF) a XML.
    /// </summary>
    Task<string> SerializeAnulacionAsync(
        AnulacionRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Serializa un acuse de recibo (ARECF) a XML.
    /// </summary>
    Task<string> SerializeAcuseReciboAsync(
        AcuseReciboDTO acuse,
        CancellationToken ct = default);

    /// <summary>
    /// Serializa una aprobación comercial (ACECF) a XML.
    /// </summary>
    Task<string> SerializeAprobacionComercialAsync(
        AprobacionComercialDTO aprobacion,
        CancellationToken ct = default);
}
```

### 3.3 IHashGenerator

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Genera hash de seguridad para facturas electrónicas.
/// El CodigoSeguridadeCF es 6 caracteres (SHA256 → primeros 6 chars base64).
/// </summary>
public interface IHashGenerator
{
    /// <summary>
    /// Genera un hash de 6 caracteres a partir del XML de la factura.
    /// </summary>
    string Generate(string xmlContent);
}
```

### 3.4 IDigitalSignatureService

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Servicio de firma digital XML-DSig para facturas electrónicas.
/// </summary>
public interface IDigitalSignatureService
{
    /// <summary>
    /// Firma digitalmente un XML de factura.
    /// Retorna el XML firmado.
    /// </summary>
    /// <param name="unsignedXml">XML sin firmar.</param>
    /// <returns>XML con firma digital incluida.</returns>
    Task<string> SignXmlAsync(
        string unsignedXml,
        string enf,
        CancellationToken ct = default);

    /// <summary>
    /// Verifica si hay certificado digital disponible y válido.
    /// </summary>
    bool HasValidCertificate();

    /// <summary>
    /// Retorna la fecha de expiración del certificado.
    /// </summary>
    DateTime? CertificateExpirationDate { get; }
}
```

### 3.5 IInvoiceRequestGenerator

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Genera un ElectronicInvoiceRequest a partir de una venta del POS.
/// Convierte SaleEntity + SaleItems en el formato necesario para DGII.
/// </summary>
public interface IInvoiceRequestGenerator
{
    /// <summary>
    /// Genera una solicitud de factura electrónica a partir de una venta.
    /// </summary>
    ElectronicInvoiceRequest GenerateFromSale(
        SaleEntity sale,
        IEnumerable<SaleItemEntity> items,
        string? enf = null);
}
```

---

## 4. Contratos de Consulta

### 4.1 IInvoiceQueryService

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Servicio de consulta para facturas electrónicas.
/// </summary>
public interface IInvoiceQueryService
{
    Task<InvoiceSummaryDTO> GetSummaryAsync(
        int? year = null,
        int? month = null,
        CancellationToken ct = default);

    Task<IEnumerable<InvoiceListItemDTO>> GetListAsync(
        int page = 1,
        int pageSize = 50,
        ElectronicInvoiceStatus? estado = null,
        DateOnly? desde = null,
        DateOnly? hasta = null,
        string? rncComprador = null,
        string? enf = null,
        CancellationToken ct = default);

    Task<InvoiceDetailDTO> GetDetailAsync(int id, CancellationToken ct = default);
}
```

---

## 5. DTOs de Transferencia

### 5.1 ElectronicInvoiceRequest

```csharp
namespace POS.Application.DTOs;

/// <summary>
/// Solicitud para emitir una factura electrónica.
/// </summary>
public record ElectronicInvoiceRequest
{
    public int SaleId { get; init; }
    public TipoeCFType TipoeCF { get; init; } = TipoeCFType.FacturaConsumo;
    public string? eNCF { get; init; }
    public EmisorRequest Emisor { get; init; } = new();
    public CompradorRequest Comprador { get; init; } = new();
    public TipoPagoType TipoPago { get; init; } = TipoPagoType.Contado;
    public TipoIngresosType TipoIngresos { get; init; } = TipoIngresosType.IngresosOperaciones;
    public DateTime? FechaLimitePago { get; init; }
    public string? TerminoPago { get; init; }
    public TipoCuentaPagoType? TipoCuentaPago { get; init; }
    public string? NumeroCuentaPago { get; init; }
    public string? BancoPago { get; init; }
    public List<FormaDePagoRequest>? FormasDePago { get; init; }
    public IndicadorMontoGravadoType IndicadorMontoGravado { get; init; }
    public IndicadorEnvioDiferidoType? IndicadorEnvioDiferido { get; init; }
    public IndicadorServicioTodoIncluidoType? IndicadorServicioTodoIncluido { get; init; }
    public List<InvoiceLineRequest> Lineas { get; init; } = new();
    public List<InvoiceSubtotalRequest> Subtotales { get; init; } = new();
    public List<InvoiceDiscountRequest> DescuentosORecargos { get; init; } = new();
    public List<InvoicePageRequest> Paginas { get; init; } = new();
    public InvoiceReferenceRequest? InformacionReferencia { get; init; }
    public InvoiceOtherCurrencyRequest? OtraMoneda { get; init; }
}
```

### 5.2 EmisorRequest / CompradorRequest

```csharp
namespace POS.Application.DTOs;

public record EmisorRequest
{
    public string RNCEmisor { get; init; } = "";
    public string RazonSocial { get; init; } = "";
    public string? NombreComercial { get; init; }
    public string? Sucursal { get; init; }
    public string Direccion { get; init; } = "";
    public string? Municipio { get; init; }
    public string? Provincia { get; init; }
    public List<string>? Telefonos { get; init; }
    public string? Correo { get; init; }
    public string? WebSite { get; init; }
    public string? ActividadEconomica { get; init; }
    public string? CodigoVendedor { get; init; }
    public string? NumeroFacturaInterna { get; init; }
    public int? NumeroPedidoInterno { get; init; }
    public string? ZonaVenta { get; init; }
    public string? RutaVenta { get; init; }
    public string? InformacionAdicional { get; init; }
    public DateTime FechaEmision { get; init; } = DateTime.Today;
}

public record CompradorRequest
{
    public string? RNC { get; init; }
    public string? IdentificadorExtranjero { get; init; }
    public string? RazonSocial { get; init; }
    public string? Contacto { get; init; }
    public string? Correo { get; init; }
    public string? Direccion { get; init; }
    public string? Municipio { get; init; }
    public string? Provincia { get; init; }
    public DateTime? FechaEntrega { get; init; }
    public string? ContactoEntrega { get; init; }
    public string? DireccionEntrega { get; init; }
    public string? TelefonoAdicional { get; init; }
    public DateTime? FechaOrdenCompra { get; init; }
    public string? NumeroOrdenCompra { get; init; }
    public string? CodigoInterno { get; init; }
    public string? ResponsablePago { get; init; }
    public string? InformacionAdicional { get; init; }
}
```

### 5.3 ElectronicInvoiceResult

```csharp
namespace POS.Application.DTOs;

/// <summary>
/// Resultado de la emisión de una factura electrónica.
/// </summary>
public record ElectronicInvoiceResult
{
    public bool Success { get; init; }
    public string eNCF { get; init; } = "";
    public string XML { get; init; } = "";
    public string XMLHash { get; init; } = "";
    public ElectronicInvoiceStatus Estado { get; init; } = ElectronicInvoiceStatus.Creado;
    public string? MotivoRechazo { get; init; }
    public DateTime? FechaAprobacion { get; init; }
    public DateTime FechaEnvio { get; init; } = DateTime.Now;
    public string? ARECFXML { get; init; }
    public string? ACECFXML { get; init; }
    public string? Error { get; init; }
}

public enum ElectronicInvoiceStatus
{
    Creado = 0,
    Enviado = 1,
    Aprobado = 2,
    Rechazado = 3,
    Anulado = 4,
    NoRecibido = 5
}
```

### 5.4 InvoiceLineRequest

```csharp
namespace POS.Application.DTOs;

public record InvoiceLineRequest
{
    public int NumeroLinea { get; init; }
    public string Nombre { get; init; } = "";
    public string? Descripcion { get; init; }
    public IndicadorFacturacionType IndicadorFacturacion { get; init; }
    public IndicadorBienoServicioType IndicadorBienoServicio { get; init; }
    public decimal Cantidad { get; init; }
    public int? UnidadMedida { get; init; }
    public decimal? CantidadReferencia { get; init; }
    public int? UnidadReferencia { get; init; }
    public decimal PrecioUnitario { get; init; }
    public decimal? DescuentoMonto { get; init; }
    public decimal? RecargoMonto { get; init; }
    public List<InvoiceCodeRequest> Codigos { get; init; } = new();
    public List<InvoiceSubDiscountRequest> SubDescuentos { get; init; } = new();
    public List<InvoiceSubRecargoRequest> SubRecargos { get; init; } = new();
    public decimal Monto { get; init; }
}

public record InvoiceCodeRequest
{
    public string TipoCodigo { get; init; } = "";
    public string Codigo { get; init; } = "";
}

public record InvoiceSubDiscountRequest
{
    public TipoDescuentoRecargoType Tipo { get; init; }
    public decimal? Porcentaje { get; init; }
    public decimal? Monto { get; init; }
}

public record InvoiceSubRecargoRequest
{
    public TipoDescuentoRecargoType Tipo { get; init; }
    public decimal? Porcentaje { get; init; }
    public decimal? Monto { get; init; }
}
```

### 5.5 InvoiceSubtotalRequest / InvoiceDiscountRequest / InvoicePageRequest

```csharp
namespace POS.Application.DTOs;

public record InvoiceSubtotalRequest
{
    public int? NumeroSubTotal { get; init; }
    public string? Descripcion { get; init; }
    public int? Orden { get; init; }
    public decimal? MontoGravadoTotal { get; init; }
    public decimal? MontoGravadoI1 { get; init; }
    public decimal? MontoGravadoI2 { get; init; }
    public decimal? MontoGravadoI3 { get; init; }
    public decimal? ITBIS { get; init; }
    public decimal? ITBIS1 { get; init; }
    public decimal? ITBIS2 { get; init; }
    public decimal? ITBIS3 { get; init; }
    public decimal? ImpuestoAdicional { get; init; }
    public decimal? Exento { get; init; }
    public decimal? Monto { get; init; }
    public int? Lineas { get; init; }
}

public record InvoiceDiscountRequest
{
    public int NumeroLinea { get; init; }
    public TipoAjusteType TipoAjuste { get; init; }
    public IndicadorNorma1007Type? IndicadorNorma1007 { get; init; }
    public string? Descripcion { get; init; }
    public TipoDescuentoRecargoType? TipoValor { get; init; }
    public decimal? Valor { get; init; }
    public decimal? Monto { get; init; }
    public decimal? MontoOtraMoneda { get; init; }
    public IndicadorFacturacionDRType? IndicadorFacturacion { get; init; }
}

public record InvoicePageRequest
{
    public int? PaginaNo { get; init; }
    public int? NoLineaDesde { get; init; }
    public int? NoLineaHasta { get; init; }
    public decimal? MontoGravado { get; init; }
    public decimal? MontoGravado1 { get; init; }
    public decimal? MontoGravado2 { get; init; }
    public decimal? MontoGravado3 { get; init; }
    public decimal? Exento { get; init; }
    public decimal? ITBIS { get; init; }
    public decimal? ITBIS1 { get; init; }
    public decimal? ITBIS2 { get; init; }
    public decimal? ITBIS3 { get; init; }
    public decimal? ImpuestoAdicional { get; init; }
    public decimal? MontoSubtotal { get; init; }
    public decimal? MontoNoFacturable { get; init; }
}
```

### 5.6 InvoiceReferenceRequest / InvoiceOtherCurrencyRequest

```csharp
namespace POS.Application.DTOs;

public record InvoiceReferenceRequest
{
    public string? NCFModificado { get; init; }
    public string? RNCOtroContribuyente { get; init; }
    public DateTime? FechaNCFModificado { get; init; }
    public CodigoModificacionType? CodigoModificacion { get; init; }
}

public record InvoiceOtherCurrencyRequest
{
    public TipoMoneda? TipoMoneda { get; init; }
    public decimal? TipoCambio { get; init; }
    public decimal? MontoGravadoTotal { get; init; }
    public decimal? MontoGravado1 { get; init; }
    public decimal? MontoGravado2 { get; init; }
    public decimal? MontoGravado3 { get; init; }
    public decimal? MontoExento { get; init; }
    public decimal? TotalITBIS { get; init; }
    public decimal? TotalITBIS1 { get; init; }
    public decimal? TotalITBIS2 { get; init; }
    public decimal? TotalITBIS3 { get; init; }
    public decimal? MontoImpuestoAdicional { get; init; }
    public List<InvoiceOtherCurrencyTaxRequest> ImpuestosAdicionales { get; init; } = new();
    public decimal? MontoTotal { get; init; }
}

public record InvoiceOtherCurrencyTaxRequest
{
    public CodificacionTipoImpuestos TipoImpuesto { get; init; }
    public decimal Tasa { get; init; }
    public decimal? MontoSpecifico { get; init; }
    public decimal? MontoAdValorem { get; init; }
    public decimal? OtrosImpuestos { get; init; }
}
```

### 5.7 AnulacionRequest / AnulacionResult

```csharp
namespace POS.Application.DTOs;

public record AnulacionRequest
{
    public string RNCEmisor { get; init; } = "";
    public DateTime FechaHoraAnulacion { get; init; } = DateTime.Now;
    public List<RangoAnulacionRequest> Anulaciones { get; init; } = new();
}

public record RangoAnulacionRequest
{
    public int NoLinea { get; init; }
    public CFType TipoeCF { get; init; }
    public List<RangoSecuenciaRequest> Secuencias { get; init; } = new();
    public int CantidadNCFAnulados { get; init; }
}

public record RangoSecuenciaRequest
{
    public string Desde { get; init; } = "";
    public string Hasta { get; init; } = "";
}

public record AnulacionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<string> eNCFsAnulados { get; init; } = new();
}
```

### 5.8 InvoiceQuery DTOs

```csharp
namespace POS.Application.DTOs;

public record InvoiceSummaryDTO
{
    public int TotalFacturas { get; init; }
    public int TotalAprobadas { get; init; }
    public int TotalRechazadas { get; init; }
    public int TotalAnuladas { get; init; }
    public int TotalPendientes { get; init; }
    public decimal MontoTotalFacturado { get; init; }
    public decimal MontoTotalITBIS { get; init; }
}

public record InvoiceListItemDTO
{
    public int Id { get; init; }
    public string eNCF { get; init; } = "";
    public TipoeCFType TipoeCF { get; init; }
    public DateTime FechaEmision { get; init; }
    public string RNCComprador { get; init; } = "";
    public string RazonSocialComprador { get; init; } = "";
    public decimal MontoTotal { get; init; }
    public decimal TotalITBIS { get; init; }
    public ElectronicInvoiceStatus Estado { get; init; }
    public DateTime? FechaEnvio { get; init; }
    public DateTime? FechaAprobacion { get; init; }
    public string? MotivoRechazo { get; init; }
}

public record InvoiceDetailDTO
{
    public int Id { get; init; }
    public string eNCF { get; init; } = "";
    public TipoeCFType TipoeCF { get; init; }
    public string Version { get; init; } = "1.0";
    public DateTime FechaEmision { get; init; }
    public DateTime? FechaLimitePago { get; init; }
    public TipoPagoType TipoPago { get; init; }
    public TipoIngresosType TipoIngresos { get; init; }
    public string TerminoPago { get; init; } = "";
    public TipoCuentaPagoType? TipoCuentaPago { get; init; }
    public string? NumeroCuentaPago { get; init; }
    public string? BancoPago { get; init; }
    public List<FormaDePagoRequest> FormasDePago { get; init; } = new();
    public decimal MontoGravadoTotal { get; init; }
    public decimal MontoGravadoI1 { get; init; }
    public decimal MontoGravadoI2 { get; init; }
    public decimal MontoGravadoI3 { get; init; }
    public decimal MontoExento { get; init; }
    public decimal TotalITBIS { get; init; }
    public decimal TotalITBIS1 { get; init; }
    public decimal TotalITBIS2 { get; init; }
    public decimal TotalITBIS3 { get; init; }
    public decimal? MontoImpuestoAdicional { get; init; }
    public List<ImpuestoAdicionalRequest>? ImpuestosAdicionales { get; init; }
    public decimal MontoTotal { get; init; }
    public List<InvoiceLineDTO> Lineas { get; init; } = new();
    public List<InvoiceSubtotalDTO> Subtotales { get; init; } = new();
    public List<InvoiceDiscountDTO> DescuentosORecargos { get; init; } = new();
    public ElectronicInvoiceStatus Estado { get; init; }
    public DateTime FechaEnvio { get; init; }
    public DateTime? FechaAprobacion { get; init; }
    public string? MotivoRechazo { get; init; }
    public string? ARECFXML { get; init; }
    public string? ACECFXML { get; init; }
    public string? XMLContent { get; init; }
    public string? XMLHash { get; init; }
}
```

---

## 6. Enumeraciones Completas

### 6.1 Estado de Factura

```csharp
public enum ElectronicInvoiceStatus
{
    Creado = 0,       // Factura creada pero no enviada a DGII
    Enviado = 1,      // Enviado a DGII, esperando respuesta
    Aprobado = 2,    // DGII aprobó la factura
    Rechazado = 3,   // DGII rechazó la factura
    Anulado = 4,     // Factura anulada
    NoRecibido = 5   // Acuse ARECF indica no recibido
}
```

### 6.2 Estado de Acuse (ARECF)

```csharp
public enum EstadoAcuse
{
    Recibido = 0,
    NoRecibido = 1
}
```

### 6.3 Estado de Aprobación (ACECF)

```csharp
public enum EstadoAprobacion
{
    Aceptado = 1,
    Rechazado = 2
}
```

### 6.4 Código de Motivo de No Recibido

```csharp
public enum CodigoMotivoNoRecibido
{
    ErrorEspecificacion = 1,
    ErrorFirmaDigital = 2,
    EnvioDuplicado = 3,
    RNCCompradornoCorresponde = 4
}
```

### 6.5 Tipos de Modificación

```csharp
public enum CodigoModificacion
{
    AnulaNCF = 1,
    CorrigeTexto = 2,
    CorrigeMontos = 3,
    ReemplazoContingencia = 4,
    ReferenciaFacturaConsumo = 5
}
```

---

## 7. Relación de Interfaces → Implementaciones

| Interfaz | Implementación (Infrastructure) | Propósito |
|----------|--------------------------------|-----------|
| IElectronicInvoiceService | DgiiElectronicInvoiceService | Emisión, anulación, consulta de facturas DGII |
| IElectronicInvoiceRepository | SqlInvoiceRepository | Persistencia de facturas en SQL Server |
| IXmlValidator | XmlValidator | Validación XML contra XSDs DGII |
| IXmlSerializer | XmlSerializer | Serialización de objetos a XML conforme a XSDs |
| IHashGenerator | HashGenerator | Generación de hash CodigoSeguridadeCF |
| IDigitalSignatureService | XmlDigitalSigner | Firma digital XML-DSig (pendiente certificado) |
| IInvoiceRequestGenerator | InvoiceRequestGenerator | Conversión de ventas POS → requests DGII |
| IInvoiceQueryService | InvoiceQueryService | Consultas y reportes de facturas |

---

## 8. Patrón de Inyección de Dependencias

### 8.1 Registro de servicios en Program.cs

```csharp
// Application Layer - servicios de aplicación
services.AddScoped<IElectronicInvoiceService, DgiiElectronicInvoiceService>();
services.AddScoped<IInvoiceRequestGenerator, InvoiceRequestGenerator>();

// Infrastructure - validación y serialización
services.AddSingleton<IXmlValidator, XmlValidator>();
services.AddSingleton<IXmlSerializer, XmlSerializer>();
services.AddSingleton<IHashGenerator, HashGenerator>();

// Infrastructure - firma digital (si hay certificado)
services.AddSingleton<IDigitalSignatureService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var certPath = config["DGII:CertificatePath"];
    var certPassword = config["DGII:CertificatePassword"];

    if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath))
        return new XmlDigitalSigner(certPath, certPassword);

    return new NoOpDigitalSignatureService(); // stub sin certificado
});

// Repositories - persistencia
services.AddScoped<IInvoiceRepository, SqlInvoiceRepository>();
services.AddScoped<ISaleRepository, SqlSaleRepository>();
services.AddScoped<IInvoiceQueryService, InvoiceQueryService>();
```

### 8.2 XmlDigitalSigner (implementación base)

```csharp
public class XmlDigitalSigner : IDigitalSignatureService
{
    private readonly X509Certificate2 _certificate;

    public XmlDigitalSigner(string certificatePath, string password)
    {
        _certificate = new X509Certificate2(certificatePath, password);
        if (_certificate.HasPrivateKey == false)
            throw new InvalidOperationException("El certificado no tiene clave privada");
    }

    public async Task<string> SignXmlAsync(string unsignedXml, string enf, CancellationToken ct)
    {
        var xmlDoc = new XmlDocument();
        xmlDoc.PreserveWhitespace = true;
        xmlDoc.LoadXml(unsignedXml);

        var signedXml = new SignedXml(xmlDoc);
        signedXml.SigningKey = _certificate.PrivateKey;

        var reference = new Reference();
        reference.Uri = ""; // firma todo el documento
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        signedXml.AddReference(reference);

        signedXml.ComputeSignature();

        var signature = signedXml.GetXml();
        var ns = xmlDoc.DocumentElement?.NamespaceURI;

        var sigElement = xmlDoc.CreateElement("Signature", ns ?? "");
        sigElement.InnerXml = signature.OuterXml;
        xmlDoc.DocumentElement?.AppendChild(sigElement);

        return await Task.FromResult(xmlDoc.OuterXml);
    }

    public bool HasValidCertificate() => _certificate != null && !_certificate.HasExpired;

    public DateTime? CertificateExpirationDate =>
        _certificate?.NotAfter;
}

public class NoOpDigitalSignatureService : IDigitalSignatureService
{
    public Task<string> SignXmlAsync(string unsignedXml, string enf, CancellationToken ct)
        => Task.FromResult(unsignedXml); // retorna sin firmar

    public bool HasValidCertificate() => false;
    public DateTime? CertificateExpirationDate => null;
}
```

---

## 9. Validaciones Pre-Envío

### 9.1 Lista de validaciones del servicio

Antes de enviar una factura a DGII, el servicio debe validar:

```csharp
public static class InvoiceValidationRules
{
    public static List<string> ValidateRequest(ElectronicInvoiceRequest request)
    {
        var errors = new List<string>();

        // 1. RNC emisor válido
        if (!RNC.IsValid(request.Emisor.RNCEmisor))
            errors.Add("RNC del emisor inválido");

        // 2. RNC comprador válido (si aplica)
        if (!string.IsNullOrEmpty(request.Comprador.RNC) &&
            !RNC.IsValid(request.Comprador.RNC))
            errors.Add("RNC del comprador inválido");

        // 3. eNCF válido o generable
        if (string.IsNullOrEmpty(request.eNCF))
        {
            request.eNCF = GenerateENCF(); // genera secuencial B + 12 dígitos
        }
        else if (!ENCF.IsValid(request.eNCF))
        {
            errors.Add($"eNCF inválido: '{request.eNCF}'");
        }

        // 4. Fecha de emisión válida (noFuture)
        if (request.Emisor.FechaEmision.Date > DateTime.Today)
            errors.Add("La fecha de emisión no puede ser futura");

        // 5. Lineas válidas
        if (request.Lineas == null || request.Lineas.Count == 0)
            errors.Add("La factura debe tener al menos 1 línea");

        if (request.Lineas.Count > 1000)
            errors.Add("La factura no puede tener más de 1000 líneas");

        foreach (var line in request.Lineas)
        {
            if (line.Nombre.IsNullOrWhiteSpace())
                errors.Add($"Línea {line.NumeroLinea}: Nombre requerido");

            if (line.Cantidad <= 0)
                errors.Add($"Línea {line.NumeroLinea}: Cantidad debe ser > 0");

            if (line.PrecioUnitario < 0)
                errors.Add($"Línea {line.NumeroLinea}: Precio unitario debe ser >= 0");

            if (line.Monto < 0)
                errors.Add($"Línea {line.NumeroLinea}: Monto debe ser >= 0");

            // Modulo deciría ITBIS el cálculo del monto es consistente con el indicador
            ValidateLineITBIS(line, errors);
        }

        // 6. MontoTotal consistente
        var (gravados, exentos, itbis, adicionales) = CalculateTotals(request);
        var expectedTotal = gravados + exentos + itbis + adicionales;

        if (Math.Abs(request.Totales.MontoTotal - expectedTotal) > 0.01m)
            errors.Add($"MontoTotal ({request.Totales.MontoTotal}) no coincide con cálculo ({expectedTotal})");

        // 7. ITBIS consistentes
        if (request.Totales.ITBIS1.HasValue && request.Totales.ITBIS1 != 18)
            errors.Add("ITBIS1 debe ser 18");

        if (request.Totales.ITBIS2.HasValue && request.Totales.ITBIS2 != 16)
            errors.Add("ITBIS2 debe ser 16");

        if (request.Totales.ITBIS3.HasValue && request.Totales.ITBIS3 != 0)
            errors.Add("ITBIS3 debe ser 0");

        // 8. Formas de pago máximas 7
        if (request.FormasDePago != null && request.FormasDePago.Count > 7)
            errors.Add("Máximo 7 formas de pago");

        // 9. Impuestos adicionales máximos 20
        if (request.ImpuestosAdicionales != null && request.ImpuestosAdicionales.Count > 20)
            errors.Add("Máximo 20 impuestos adicionales");

        // 10. SubDescuentos máximos 12 por línea
        foreach (var line in request.Lineas)
        {
            if (line.SubDescuentos.Count > 12)
                errors.Add($"Línea {line.NumeroLinea}: Máximo 12 subdescuentos");

            if (line.SubRecargos.Count > 12)
                errors.Add($"Línea {line.NumeroLinea}: Máximo 12 subrecargos");
        }

        return errors;
    }
}
```

---

## 10. Contratos para Contingencia

### 10.1 IContingencyService

```csharp
namespace POS.Application.Interfaces;

/// <summary>
/// Servicio para manejar facturación en modo contingencia
/// cuando DGII no está disponible.
/// </summary>
public interface IContingencyService
{
    /// <summary>
    /// Guarda una factura en modo contingencia (RFCE).
    /// </summary>
    Task GuardarContingenciaAsync(
        ElectronicInvoiceRequest request,
        string xmlContingencia,
        CancellationToken ct = default);

    /// <summary>
    /// Obtiene todas las facturas pendientes de envío por contingencia.
    /// </summary>
    Task<IEnumerable<PendingContingencyInvoice>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Reintenta enviar una factura de contingencia a DGII.
    /// </summary>
    Task<ElectronicInvoiceResult> ReintentarEnviarAsync(
        int idContingencia,
        CancellationToken ct = default);

    /// <summary>
    /// Elimina una factura de contingencia que ya fue enviada exitosamente.
    /// </summary>
    Task EliminarContingenciaAsync(int idContingencia, CancellationToken ct = default);
}

public record PendingContingencyInvoice
{
    public int Id { get; init; }
    public int SaleId { get; init; }
    public string eNCF { get; init; } = "";
    public DateTime FechaGuardado { get; init; }
    public string XmlContent { get; init; } = "";
    public int Reintentos { get; init; }
    public string? UltimoError { get; init; }
}
```

---

## 11. Patrón de Command/Handler para Facturación

### 11.1 EmitirFacturaCommand

```csharp
namespace POS.Application.ElectronicInvoicing.Commands;

public record EmitirFacturaCommand
{
    public int SaleId { get; init; }
    public TipoeCFType TipoeCF { get; init; } = TipoeCFType.FacturaConsumo;
    public string? eNCF { get; init; }
    public string? RNCCompradorOverride { get; init; }
    public string? RazonSocialCompradorOverride { get; init; }
    public FormaPagoType? FormaPagoPrincipalOverride { get; init; }
    public TipoPagoType? TipoPagoOverride { get; init; }
    public DateTime? FechaLimitePagoOverride { get; init; }
}

public class EmitirFacturaCommandHandler : IRequestHandler<EmitirFacturaCommand, EmitirFacturaResult>
{
    private readonly IElectronicInvoiceService _electronicInvoiceService;
    private readonly ISaleRepository _saleRepository;
    private readonly IElectronicInvoiceRepository _invoiceRepository;
    private readonly IInvoiceRequestGenerator _requestGenerator;
    private readonly ILogger<EmitirFacturaCommandHandler> _logger;

    public EmitirFacturaCommandHandler(
        IElectronicInvoiceService electronicInvoiceService,
        ISaleRepository saleRepository,
        IElectronicInvoiceRepository invoiceRepository,
        IInvoiceRequestGenerator requestGenerator,
        ILogger<EmitirFacturaCommandHandler> logger)
    {
        _electronicInvoiceService = electronicInvoiceService;
        _saleRepository = saleRepository;
        _invoiceRepository = invoiceRepository;
        _requestGenerator = requestGenerator;
        _logger = logger;
    }

    public async Task<EmitirFacturaResult> Handle(
        EmitirFacturaCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Obtener la venta
        var sale = await _saleRepository.GetByIdAsync(command.SaleId, cancellationToken)
            ?? throw new SaleNotFoundException(command.SaleId);

        // 2. Obtener items de la venta
        var items = await _saleRepository.GetItemsBySaleIdAsync(command.SaleId, cancellationToken);

        // 3. Verificar que no tenga factura electrónica asignada
        var existing = await _invoiceRepository.GetByENCFAsync(
            sale.NCF, cancellationToken);
        if (existing != null)
            throw new InvoiceAlreadyExistsException(sale.NCF);

        // 4. Generar request a partir de la venta
        var request = _requestGenerator.GenerateFromSale(sale, items, command.eNCF);

        // Sobrescribir valores desde el command si están presentes
        if (command.RNCCompradorOverride != null)
            request.Comprador.RNC = command.RNCCompradorOverride;

        if (command.RazonSocialCompradorOverride != null)
            request.Comprador.RazonSocial = command.RazonSocialCompradorOverride;

        if (command.FormaPagoPrincipalOverride.HasValue)
        {
            request.FormasDePago = new List<FormaDePagoRequest>
            {
                new FormaDePagoRequest
                {
                    Tipo = command.FormaPagoPrincipalOverride.Value,
                    Monto = sale.Total
                }
            };
        }

        if (command.TipoPagoOverride.HasValue)
            request.TipoPago = command.TipoPagoOverride.Value;

        if (command.FechaLimitePagoOverride.HasValue)
            request.FechaLimitePago = command.FechaLimitePagoOverride;

        // 5. Validar antes de enviar
        var validationErrors = InvoiceValidationRules.ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            _logger.LogWarning("Validación fallida para factura {SaleId}: {Errors}",
                command.SaleId, string.Join(", ", validationErrors));
            throw new InvoiceValidationException(validationErrors);
        }

        // 7. Enviar a DGII mediante REST API JSON (DGiiApiClient)
        var result = await _electronicInvoiceService.SubmitAsync(request, cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Error al emitir factura {SaleId}: {Error}",
                command.SaleId, result.Error);
            throw new InvoiceSubmissionException(result.Error);
        }

        // 7. Persistir en BD
        await _invoiceRepository.CreateAsync(new ElectronicInvoiceEntity
        {
            SaleId = command.SaleId,
            eNCF = result.eNCF,
            XMLContent = result.XML,
            XMLHash = result.XMLHash,
            Estado = result.Estado,
            FechaEnvio = result.FechaEnvio,
            FechaAprobacion = result.FechaAprobacion,
            MotivoRechazo = result.MotivoRechazo
        }, cancellationToken);

        _logger.LogInformation("Factura electrónica emitida: {ENCF} para venta {SaleId}",
            result.eNCF, command.SaleId);

        return new EmitirFacturaResult
        {
            eNCF = result.eNCF,
            XML = result.XML,
            XMLHash = result.XMLHash,
            Estado = result.Estado,
            FechaEnvio = result.FechaEnvio,
            FechaAprobacion = result.FechaAprobacion,
            ARECFXML = result.ARECFXML,
            ACECFXML = result.ACECFXML
        };
    }
}
```

### 11.2 AnularFacturaCommand

```csharp
namespace POS.Application.ElectronicInvoicing.Commands;

public record AnularFacturaCommand
{
    public string eNCF { get; init; } = "";
    public string? MotivoAnulacion { get; init; }
}

public class AnularFacturaCommandHandler : IRequestHandler<AnularFacturaCommand, AnularFacturaResult>
{
    private readonly IElectronicInvoiceService _electronicInvoiceService;
    private readonly IElectronicInvoiceRepository _invoiceRepository;
    private readonly ILogger<AnularFacturaCommandHandler> _logger;

    public async Task<AnularFacturaResult> Handle(
        AnularFacturaCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Validar que la factura existe y está aprobada
        var invoice = await _invoiceRepository.GetByENCFAsync(command.eNCF, cancellationToken)
            ?? throw new InvoiceNotFoundException(command.eNCF);

        if (invoice.Estado != ElectronicInvoiceStatus.Aprobado)
            throw new InvoiceNotApprovedException(
                $"La factura {command.eNCF} no está aprobada (estado: {invoice.Estado})");

        // 2. Crear request de anulación
        var request = new AnulacionRequest
        {
            RNCEmisor = invoice.RNCEmisor,
            FechaHoraAnulacion = DateTime.Now,
            Anulaciones = new List<RangoAnulacionRequest>
            {
                new RangoAnulacionRequest
                {
                    NoLinea = 1,
                    TipoeCF = invoice.TipoeCF,
                    Secuencias = new List<RangoSecuenciaRequest>
                    {
                        new RangoSecuenciaRequest
                        {
                            Desde = command.eNCF,
                            Hasta = command.eNCF
                        }
                    },
                    CantidadNCFAnulados = 1
                }
            }
        };

        // 3. Enviar anulación a DGII
        var result = await _electronicInvoiceService.AnularAsync(request, cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Error al anular factura {ENCF}: {Error}",
                command.eNCF, result.Error);
            throw new InvoiceAnulacionException(result.Error);
        }

        // 4. Actualizar estado en BD
        await _invoiceRepository.UpdateEstadoAsync(
            invoice.Id,
            ElectronicInvoiceStatus.Anulado,
            cancellationToken);

        _logger.LogInformation("Factura {ENCF} anulada exitosamente", command.eNCF);

        return new AnularFacturaResult
        {
            eNCF = command.eNCF,
            Success = true,
            eNCFsAnulados = result.eNCFsAnulados
        };
    }
}
```

### 11.3 ConsultarEstadoFacturaCommand

```csharp
namespace POS.Application.ElectronicInvoicing.Commands;

public record ConsultarEstadoFacturaCommand
{
    public string eNCF { get; init; } = "";
}

public class ConsultarEstadoFacturaCommandHandler
    : IRequestHandler<ConsultarEstadoFacturaCommand, InvoiceStatusResult>
{
    private readonly IElectronicInvoiceService _electronicInvoiceService;
    private readonly IElectronicInvoiceRepository _invoiceRepository;

    public async Task<InvoiceStatusResult> Handle(
        ConsultarEstadoFacturaCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Obtener de BD local primero
        var local = await _invoiceRepository.GetByENCFAsync(command.eNCF, cancellationToken);
        if (local != null)
        {
            return new InvoiceStatusResult
            {
                eNCF = command.eNCF,
                Estado = local.Estado,
                FechaEmision = local.FechaEmision,
                FechaEnvio = local.FechaEnvio,
                FechaAprobacion = local.FechaAprobacion,
                MotivoRechazo = local.MotivoRechazo,
                Source = "Local"
            };
        }

        // 2. Consultar en DGII
        var dgiiStatus = await _electronicInvoiceService.ConsultarEstadoAsync(
            command.eNCF, cancellationToken);

        return new InvoiceStatusResult
        {
            eNCF = command.eNCF,
            Estado = dgiiStatus.Estado,
            FechaEmision = dgiiStatus.FechaEmision,
            FechaEnvio = dgiiStatus.FechaEnvio,
            FechaAprobacion = dgiiStatus.FechaAprobacion,
            MotivoRechazo = dgiiStatus.MotivoRechazo,
            Source = "DGII"
        };
    }
}
```

---

## 12. Relación entre capas

```
┌─────────────────────────────────────────────────────────────┐
│                        POS.Web (MVC)                         │
│  FacturacionController                                      │
│  └── EmitirFactura(int saleId) → emitirFacturaCommand      │
└───────────────────────────┬─────────────────────────────────┘
                            │ Handle(command)
┌───────────────────────────▼─────────────────────────────────┐
│                   POS.Application                             │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ EmitirFacturaCommandHandler                              │ │
│  │   ├── Validar request                                     │ │
│  │   ├── Generar request desde Sale                          │ │
│  │   └── Invocar _electronicInvoiceService.SubmitAsync()    │ │
│  └─────────────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ IElectronicInvoiceService (interface)                   │ │
│  │ IInvoiceRequestGenerator (interface)                    │ │
│  │ IElectronicInvoiceRepository (interface)                │ │
│  │ ISaleRepository (interface)                              │ │
│  └─────────────────────────────────────────────────────────┘ │
└───────────────────────────┬─────────────────────────────────┘
                            │ Implementación
┌───────────────────────────▼─────────────────────────────────┐
│                POS.Infrastructure                             │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ DgiiElectronicInvoiceService                            │ │
│  │   ├── XmlSerializer.SerializeAsync()                    │ │
│  │   ├── XmlValidator.ValidateAsync()                     │ │
│  │   ├── HashGenerator.Generate()                         │ │
│  │   ├── DigitalSignatureService.SignXmlAsync()          │ │
│  │   └── DgiiApiClient.SendAsync() (REST API JSON)            │ │
│  └─────────────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ SqlInvoiceRepository                                    │ │
│  │   └── EF Core → SQL Server                              │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 13. Manejo de Errores

### 13.1 Excepciones Personalizadas

```csharp
namespace POS.Application.Exceptions;

public class InvoiceException : Exception
{
    public InvoiceException(string message) : base(message) { }
    public InvoiceException(string message, Exception inner) : base(message, inner) { }
}

public class SaleNotFoundException : InvoiceException
{
    public int SaleId { get; }
    public SaleNotFoundException(int saleId)
        : base($"Venta no encontrada: {saleId}") { SaleId = saleId; }
}

public class InvoiceNotFoundException : InvoiceException
{
    public string eNCF { get; }
    public InvoiceNotFoundException(string enf)
        : base($"Factura no encontrada: {enf}") { eNCF = enf; }
}

public class InvoiceAlreadyExistsException : InvoiceException
{
    public string eNCF { get; }
    public InvoiceAlreadyExistsException(string enf)
        : base($"Factura ya existe: {enf}") { eNCF = enf; }
}

public class InvoiceNotApprovedException : InvoiceException
{
    public InvoiceNotApprovedException(string message) : base(message) { }
}

public class InvoiceValidationException : InvoiceException
{
    public List<string> Errors { get; }
    public InvoiceValidationException(List<string> errors)
        : base($"Validación fallida: {string.Join(", ", errors)}")
    {
        Errors = errors;
    }
}

public class InvoiceSubmissionException : InvoiceException
{
    public InvoiceSubmissionException(string message) : base(message) { }
}

public class InvoiceAnulacionException : InvoiceException
{
    public InvoiceAnulacionException(string message) : base(message) { }
}

public class DGIIException : InvoiceException
{
    public string? ResponseXml { get; }
    public DGIIException(string message, string? responseXml = null)
        : base(message) { ResponseXml = responseXml; }
}

public class CertificateException : InvoiceException
{
    public CertificateException(string message) : base(message) { }
}

public class XmlValidationException : InvoiceException
{
    public List<string> Errors { get; }
    public XmlValidationException(List<string> errors)
        : base($"XML inválido: {string.Join(", ", errors)}")
    {
        Errors = errors;
    }
}
```

### 13.2 Manejo de Excepciones en Global

```csharp
// En Program.cs o Startup
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var response = context.Response;

        response.ContentType = "application/json";

        if (exception is InvoiceValidationException invEx)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new
            {
                error = "Validación",
                message = invEx.Message,
                errors = invEx.Errors
            });
        }
        else if (exception is InvoiceNotFoundException || SaleNotFoundException)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsJsonAsync(new
            {
                error = "No encontrado",
                message = exception.Message
            });
        }
        else if (exception is DGIIException)
        {
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await response.WriteAsJsonAsync(new
            {
                error = "DGII no disponible",
                message = exception.Message
            });
        }
        else
        {
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsJsonAsync(new
            {
                error = "Error interno",
                message = exception.Message
            });
        }
    });
});
```

---

## 14. Documentación Relacionada

- [01_Resumen_General.md](01_Resumen_General.md) — Visión general del sistema
- [02_Mapeo_XSD_CSharp.md](02_Mapeo_XSD_CSharp.md) — Mapeo completo de tipos XSD a C#
- [04_Diseno_Infraestructura_DGII.md](04_Diseno_Infraestructura_DGII.md) — Diseño de la infraestructura DGII
- [05_Plan_Implementacion.md](05_Plan_Implementacion.md) — Plan de implementación paso a paso

---

## 15. Próximos Pasos

1. Aprobar los contratos de interfaces definidos
2. Empezar la implementación de la capa de Infrastructure
3. Primero implementar XmlSerializer + XmlValidator (sin dependencias externas)
4. Luego implementar HashGenerator
5. Luego implementar DgiiElectronicInvoiceService (incluyendo envío SOAP)
6. Implementar digital signature cuando esté disponible el certificado
7. Implementar repositorios SQL
8. Implementar Command Handlers en Application Layer
9. Exposing a UI para configuración y consulta de facturas
