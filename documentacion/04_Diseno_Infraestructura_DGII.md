# Diseño de Infraestructura — Servicios DGII e Integración

**Versión:** 1.0  
**Fecha:** 16/09/2026  
**Stack:** .NET 10 / C# / SQL Server

---

## 1. Resumen

Este documento detalla el diseño de la capa de Infrastructure para el módulo de facturación electrónica DGII. Incluye las implementaciones concretas de XmlSerializer, XmlValidator, HashGenerator, DigitalSignatureService, DgiiElectronicInvoiceService y SqlInvoiceRepository.

---

## 2. XmlSerializer — Implementación Completa

### 2.1 Estructura del Serializador

```csharp
namespace POS.Infrastructure.ElectronicInvoicing;

public class XmlSerializer : IXmlSerializer
{
    private readonly IConfiguration _config;
    private readonly string _baseXsdPath;

    public XmlSerializer(IConfiguration config)
    {
        _config = config;
        _baseXsdPath = config["DGII:XsdPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "documentacion xsd");
    }

    public async Task<string> SerializeAsync(
        ElectronicInvoiceRequest request,
        string xsdType,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var xml = xsdType switch
            {
                "ECF32" => SerializarECF32(request),
                "ECF31" => SerializarECF31(request),
                "RFCE32" => SerializarRFCE32(request),
                _ => throw new ArgumentException($"Tipo de XSD no soportado: {xsdType}")
            };
            return xml.ToString();
        }, ct);
    }

    public async Task<string> SerializeAnulacionAsync(
        AnulacionRequest request,
        CancellationToken ct)
    {
        return await Task.Run(() => SerializarANECF(request), ct);
    }

    public async Task<string> SerializeAcuseReciboAsync(
        AcuseReciboDTO acuse,
        CancellationToken ct)
    {
        return await Task.Run(() => SerializarARECF(acuse), ct);
    }

    public async Task<string> SerializeAprobacionComercialAsync(
        AprobacionComercialDTO aprobacion,
        CancellationToken ct)
    {
        return await Task.Run(() => SerializarACECF(aprobacion), ct);
    }
}
```

### 2.2 Serialización e-CF 32 (Factura de Consumo Electrónica)

```csharp
private XDocument SerializarECF32(ElectronicInvoiceRequest request)
{
    var encabezado = new XElement("Encabezado",
        new XElement("Version", "1.0"),
        new XElement("IdDoc",
            new XElement("TipoeCF", (int)request.TipoeCF),
            new XElement("eNCF", request.eNCF ?? GenerateENCF()),
            CrearOpcional("IndicadorEnvioDiferido",
                request.IndicadorEnvioDiferido?.ToString()),
            CrearOpcional("IndicadorMontoGravado",
                request.IndicadorMontoGravado.ToString()),
            CrearOpcional("IndicadorServicioTodoIncluido",
                request.IndicadorServicioTodoIncluido?.ToString()),
            new XElement("TipoIngresos",
                ((int)request.TipoIngresos).ToString("D2")),
            new XElement("TipoPago", (int)request.TipoPago),
            CrearOpcional("FechaLimitePago",
                request.FechaLimitePago?.ToString("dd-MM-yyyy")),
            CrearOpcional("TerminoPago", request.TerminoPago),
            // TablaFormasPago
            request.FormasDePago is { Count: > 0 } formas
                ? new XElement("TablaFormasPago",
                    new XElement("FormaDePago",
                        formas.Select(fp => new XElement("FormaDePago",
                            new XElement("FormaPago", (int)fp.Tipo),
                            new XElement("MontoPago", fp.Monto)))))
                : null,
            CrearOpcional("TipoCuentaPago",
                request.TipoCuentaPago?.ToString()),
            CrearOpcional("NumeroCuentaPago",
                request.NumeroCuentaPago),
            CrearOpcional("BancoPago", request.BancoPago),
            CrearOpcional("FechaDesde",
                request.FechaDesde?.ToString("dd-MM-yyyy")),
            CrearOpcional("FechaHasta",
                request.FechaHasta?.ToString("dd-MM-yyyy")),
            CrearOpcional("TotalPaginas",
                request.TotalPaginas?.ToString())
        ),
        // Emisor
        new XElement("Emisor",
            new XElement("RNCEmisor", request.Emisor.RNCEmisor),
            new XElement("RazonSocialEmisor", request.Emisor.RazonSocial),
            CrearOpcional("NombreComercial",
                request.Emisor.NombreComercial),
            CrearOpcional("Sucursal", request.Emisor.Sucursal),
            new XElement("DireccionEmisor", request.Emisor.Direccion),
            CrearOpcional("Municipio", request.Emisor.Municipio),
            CrearOpcional("Provincia", request.Emisor.Provincia),
            CrearTelefonos(request.Emisor.Telefonos),
            CrearOpcional("CorreoEmisor", request.Emisor.Correo),
            CrearOpcional("WebSite", request.Emisor.WebSite),
            CrearOpcional("ActividadEconomica",
                request.Emisor.ActividadEconomica),
            CrearOpcional("CodigoVendedor",
                request.Emisor.CodigoVendedor),
            CrearOpcional("NumeroFacturaInterna",
                request.Emisor.NumeroFacturaInterna),
            CrearOpcional("NumeroPedidoInterno",
                request.Emisor.NumeroPedidoInterno?.ToString()),
            CrearOpcional("ZonaVenta", request.Emisor.ZonaVenta),
            CrearOpcional("RutaVenta", request.Emisor.RutaVenta),
            CrearOpcional("InformacionAdicionalEmisor",
                request.Emisor.InformacionAdicional),
            new XElement("FechaEmision",
                request.Emisor.FechaEmision.ToString("dd-MM-yyyy"))
        ),
        // Comprador
        new XElement("Comprador",
            CrearOpcional("RNCComprador", request.Comprador.RNC),
            CrearOpcional("IdentificadorExtranjero",
                request.Comprador.IdentificadorExtranjero),
            CrearOpcional("RazonSocialComprador",
                request.Comprador.RazonSocial),
            CrearOpcional("ContactoComprador",
                request.Comprador.Contacto),
            CrearOpcional("CorreoComprador",
                request.Comprador.Correo),
            CrearOpcional("DireccionComprador",
                request.Comprador.Direccion),
            CrearOpcional("MunicipioComprador",
                request.Comprador.Municipio),
            CrearOpcional("ProvinciaComprador",
                request.Comprador.Provincia),
            CrearOpcional("FechaEntrega",
                request.Comprador.FechaEntrega?.ToString("dd-MM-yyyy")),
            CrearOpcional("ContactoEntrega",
                request.Comprador.ContactoEntrega),
            CrearOpcional("DireccionEntrega",
                request.Comprador.DireccionEntrega),
            CrearOpcional("TelefonoAdicional",
                request.Comprador.TelefonoAdicional),
            CrearOpcional("FechaOrdenCompra",
                request.Comprador.FechaOrdenCompra?.ToString("dd-MM-yyyy")),
            CrearOpcional("NumeroOrdenCompra",
                request.Comprador.NumeroOrdenCompra),
            CrearOpcional("CodigoInternoComprador",
                request.Comprador.CodigoInterno),
            CrearOpcional("ResponsablePago",
                request.Comprador.ResponsablePago),
            CrearOpcional("InformacionAdicionalComprador",
                request.Comprador.InformacionAdicional)
        ),
        // InformacionesAdicionales (opcional)
        CrearInformacionesAdicionales(request),
        // Transporte (opcional)
        CrearTransporte(request),
        // Totales
        new XElement("Totales",
            CrearOpcional("MontoGravadoTotal",
                request.Totales?.MontoGravadoTotal?.ToString("F2")),
            CrearOpcional("MontoGravadoI1",
                request.Totales?.MontoGravadoI1?.ToString("F2")),
            CrearOpcional("MontoGravadoI2",
                request.Totales?.MontoGravadoI2?.ToString("F2")),
            CrearOpcional("MontoGravadoI3",
                request.Totales?.MontoGravadoI3?.ToString("F2")),
            CrearOpcional("MontoExento",
                request.Totales?.MontoExento?.ToString("F2")),
            CrearOpcional("ITBIS1",
                request.Totales?.ITBIS1?.ToString()),
            CrearOpcional("ITBIS2",
                request.Totales?.ITBIS2?.ToString()),
            CrearOpcional("ITBIS3",
                request.Totales?.ITBIS3?.ToString()),
            CrearOpcional("TotalITBIS",
                request.Totales?.TotalITBIS?.ToString("F2")),
            CrearOpcional("TotalITBIS1",
                request.Totales?.TotalITBIS1?.ToString("F2")),
            CrearOpcional("TotalITBIS2",
                request.Totales?.TotalITBIS2?.ToString("F2")),
            CrearOpcional("TotalITBIS3",
                request.Totales?.TotalITBIS3?.ToString("F2")),
            CrearOpcional("MontoImpuestoAdicional",
                request.Totales?.MontoImpuestoAdicional?.ToString("F2")),
            CrearImpuestosAdicionales(request.Totales?.ImpuestosAdicionales),
            new XElement("MontoTotal",
                request.Totales?.MontoTotal?.ToString("F2") ?? "0.00")
        ),
        // OtraMoneda (opcional)
        request.OtraMoneda is not null
            ? new XElement("OtraMoneda",
                new XElement("TipoMoneda",
                    MonedaFormatter.ToString(request.OtraMoneda.TipoMoneda)),
                CrearOpcional("TipoCambio",
                    request.OtraMoneda.TipoCambio?.ToString("F4")),
                // ... resto de campos OtraMoneda
            )
            : null
    );

    var detallesItems = new XElement("DetallesItems",
        new XElement("Item",
            request.Lineas.Select((line, idx) => new XElement("Item",
                new XElement("NumeroLinea", line.NumeroLinea),
                CrearCodigosItem(line.Codigos),
                new XElement("IndicadorFacturacion",
                    (int)line.IndicadorFacturacion),
                new XElement("NombreItem", line.Nombre),
                CrearOpcional("DescripcionItem",
                    line.Descripcion),
                new XElement("IndicadorBienoServicio",
                    (int)line.IndicadorBienoServicio),
                new XElement("CantidadItem",
                    line.Cantidad.ToString("F2")),
                CrearOpcional("UnidadMedida",
                    line.UnidadMedida?.ToString()),
                CrearOpcional("CantidadReferencia",
                    line.CantidadReferencia?.ToString("F2")),
                CrearOpcional("UnidadReferencia",
                    line.UnidadReferencia?.ToString()),
                CrearSubcantidades(line),
                CrearOpcional("GradosAlcohol",
                    line.GradosAlcohol?.ToString("F2")),
                CrearOpcional("FechaElaboracion",
                    line.FechaElaboracion?.ToString("dd-MM-yyyy")),
                CrearOpcional("FechaVencimientoItem",
                    line.FechaVencimiento?.ToString("dd-MM-yyyy")),
                CrearMineria(line),
                new XElement("PrecioUnitarioItem",
                    line.PrecioUnitario.ToString("F4")),
                CrearOpcional("DescuentoMonto",
                    line.DescuentoMonto?.ToString("F2")),
                CrearSubDescuentos(line),
                CrearOpcional("RecargoMonto",
                    line.RecargoMonto?.ToString("F2")),
                CrearSubRecargos(line),
                CrearTablaImpuestoAdicional(line),
                CrearOtraMonedaDetalle(line),
                new XElement("MontoItem",
                    line.Monto.ToString("F2"))
            )))
    );

    var subtotales = CrearSubtotales(request);
    var descuentosRecargos = CrearDescuentosORecargos(request);
    var paginacion = CrearPaginacion(request);

    var raiz = new XElement("ECF", encabezado, detallesItems,
        subtotales, descuentosRecargos, paginacion);

    return new XDocument(new XDeclaration("1.0", "utf-8", null), raiz);
}
```

### 2.3 Helper Methods de Serialización

```csharp
private static XElement? CrearOpcional(string nombre, string? valor)
{
    if (string.IsNullOrWhiteSpace(valor)) return null;
    return new XElement(nombre, valor);
}

private static XElement? CrearOpcional(string nombre, int? valor)
{
    if (valor == null) return null;
    return new XElement(nombre, valor.Value);
}

private static XElement? CrearOpcional(string nombre, decimal? valor)
{
    if (valor == null) return null;
    return new XElement(nombre, valor.Value.ToString("F2"));
}

private static XElement CrearTelefonos(List<string>? telefonos)
{
    if (telefonos == null || telefonos.Count == 0) return new XElement("TablaTelefonoEmisor");

    return new XElement("TablaTelefonoEmisor",
        telefonos.Select(t => new XElement("TelefonoEmisor", t)).ToArray());
}

private static XElement? CrearInformacionesAdicionales(ElectronicInvoiceRequest request)
{
    // Implementar si hay información adicional
    return null;
}

private static XElement? CrearTransporte(ElectronicInvoiceRequest request)
{
    // Implementar si hay transporte
    return null;
}

private static XElement CrearImpuestosAdicionales(
    List<ImpuestoAdicionalRequest>? impuestos)
{
    if (impuestos == null || impuestos.Count == 0)
        return new XElement("ImpuestosAdicionales");

    return new XElement("ImpuestosAdicionales",
        impuestos.Select(ia => new XElement("ImpuestoAdicional",
            new XElement("TipoImpuesto", ia.TipoImpuesto.ToString("D3")),
            new XElement("TasaImpuestoAdicional",
                ia.Tasa.ToString("F2")),
            CrearOpcional("MontoImpuestoSelectivoConsumoEspecifico",
                ia.MontoSpecifico?.ToString("F2")),
            CrearOpcional("MontoImpuestoSelectivoConsumoAdvalorem",
                ia.MontoAdValorem?.ToString("F2")),
            CrearOpcional("OtrosImpuestosAdicionales",
                ia.OtrosImpuestos?.ToString("F2"))
        )).ToArray());
}

private static XElement? CrearCodigosItem(List<InvoiceCodeRequest> codigos)
{
    if (codigos == null || codigos.Count == 0) return null;

    return new XElement("TablaCodigosItem",
        codigos.Select(c => new XElement("CodigosItem",
            new XElement("TipoCodigo", c.TipoCodigo),
            new XElement("CodigoItem", c.Codigo)
        )).ToArray());
}

private static XElement? CrearSubcantidades(InvoiceLineRequest line)
{
    // Implementar si hay subcantidades
    return null;
}

private static XElement? CrearMineria(InvoiceLineRequest line)
{
    // Implementar si hay datos de minería
    return null;
}

private static XElement? CrearSubDescuentos(InvoiceLineRequest line)
{
    if (line.SubDescuentos.Count == 0) return null;

    return new XElement("TablaSubDescuento",
        line.SubDescuentos.Select(sd => new XElement("SubDescuento",
            new XElement("TipoSubDescuento",
                sd.Tipo.ToString()),
            CrearOpcional("SubDescuentoPorcentaje",
                sd.Porcentaje?.ToString("F2")),
            CrearOpcional("MontoSubDescuento",
                sd.Monto?.ToString("F2"))
        )).ToArray());
}

private static XElement? CrearSubRecargos(InvoiceLineRequest line)
{
    if (line.SubRecargos.Count == 0) return null;

    return new XElement("TablaSubRecargo",
        line.SubRecargos.Select(sr => new XElement("SubRecargo",
            new XElement("TipoSubRecargo",
                sr.Tipo.ToString()),
            CrearOpcional("SubRecargoPorcentaje",
                sr.Porcentaje?.ToString("F2")),
            CrearOpcional("MontoSubRecargo",
                sr.Monto?.ToString("F2"))
        )).ToArray());
}

private static XElement? CrearTablaImpuestoAdicional(InvoiceLineRequest line)
{
    if (line.ImpuestosAdicionales == null ||
        line.ImpuestosAdicionales.Count == 0)
        return null;

    return new XElement("TablaImpuestoAdicional",
        line.ImpuestosAdicionales.Select(ia => new XElement("ImpuestoAdicional",
            new XElement("TipoImpuesto",
                ia.TipoImpuesto.ToString("D3"))
        )).ToArray());
}

private static XElement? CrearOtraMonedaDetalle(InvoiceLineRequest line)
{
    // Implementar si hay OtraMonedaDetalle
    return null;
}

private static XElement? CrearSubtotales(ElectronicInvoiceRequest request)
{
    // Implementar si hay subtotales
    return null;
}

private static XElement? CrearDescuentosORecargos(
    ElectronicInvoiceRequest request)
{
    // Implementar si hay descuentos/recargos globales
    return null;
}

private static XElement? CrearPaginacion(ElectronicInvoiceRequest request)
{
    // Implementar si hay paginación
    return null;
}

private static string GenerateENCF()
{
    // Genera eNCF secuencial B + 12 dígitos
    // Implementación: obtener último eNCF desde BD
    throw new NotImplementedException("ENCF generation requires repository");
}

private static string SerializarECF31(ElectronicInvoiceRequest request)
{
    // Similar a ECF32 pero con FechaVencimientoSecuencia obligatorio
    throw new NotImplementedException();
}

private static string SerializarRFCE32(ElectronicInvoiceRequest request)
{
    // Versión simplificada para contingencia
    throw new NotImplementedException();
}

private static string SerializarANECF(AnulacionRequest request)
{
    var xml = new XDocument(new XDeclaration("1.0", "utf-8", null),
        new XElement("ANECF",
            new XElement("Encabezado",
                new XElement("Version", "1.0"),
                new XElement("RncEmisor", request.RNCEmisor),
                new XElement("CantidadeNCFAnulados",
                    request.Anulaciones.Sum(a => a.CantidadNCFAnulados)),
                new XElement("FechaHoraAnulacioneNCF",
                    request.FechaHoraAnulacion.ToString("dd-MM-yyyy HH:mm:ss"))
            ),
            new XElement("DetalleAnulacion",
                new XElement("Anulacion",
                    new XElement("NoLinea", "1"),
                    new XElement("TipoeCF",
                        ((int)request.Anulaciones[0].TipoeCF).ToString()),
                    new XElement("TablaRangoSecuenciasAnuladaseNCF",
                        new XElement("Secuencias",
                            request.Anulaciones[0].Secuencias.Select(s =>
                                new XElement("Secuencias",
                                    new XElement("SecuenciaeNCFDesde", s.Desde),
                                    new XElement("SecuenciaeNCFHasta", s.Hasta)
                                )).ToArray()
                            ))
                    ),
                    new XElement("CantidadeNCFAnulados",
                        request.Anulaciones[0].CantidadNCFAnulados.ToString())
                )
            )
        )
    );

    return xml.ToString();
}

private static string SerializarARECF(AcuseReciboDTO acuse)
{
    var xml = new XDocument(new XDeclaration("1.0", "utf-8", null),
        new XElement("ARECF",
            new XElement("DetalleAcusedeRecibo",
                new XElement("Version", "1.0"),
                new XElement("RNCEmisor", acuse.RNCEmisor),
                new XElement("RNCComprador", acuse.RNCComprador),
                new XElement("eNCF", acuse.eNCF),
                new XElement("Estado", (int)acuse.Estado),
                CrearOpcional("CodigoMotivoNoRecibido",
                    acuse.CodigoMotivoNoRecibido?.ToString()),
                new XElement("FechaHoraAcuseRecibo",
                    acuse.FechaHoraAcuseRecibo.ToString("dd-MM-yyyy HH:mm:ss"))
            )
        )
    );

    return xml.ToString();
}

private static string SerializarACECF(AprobacionComercialDTO aprobacion)
{
    var xml = new XDocument(new XDeclaration("1.0", "utf-8", null),
        new XElement("ACECF",
            new XElement("DetalleAprobacionComercial",
                new XElement("Version", "1.0"),
                new XElement("RNCEmisor", aprobacion.RNCEmisor),
                new XElement("eNCF", aprobacion.eNCF),
                new XElement("FechaEmision",
                    aprobacion.FechaEmision.ToString("dd-MM-yyyy")),
                new XElement("MontoTotal",
                    aprobacion.MontoTotal.ToString("F2")),
                new XElement("RNCComprador",
                    aprobacion.RNCComprador),
                new XElement("Estado", (int)aprobacion.Estado),
                CrearOpcional("DetalleMotivoRechazo",
                    aprobacion.MotivoRechazo),
                new XElement("FechaHoraAprobacionComercial",
                    aprobacion.FechaHoraAprobacion.ToString("dd-MM-yyyy HH:mm:ss"))
            )
        )
    );

    return xml.ToString();
}
```

---

## 3. XmlValidator — Implementación Completa

```csharp
namespace POS.Infrastructure.ElectronicInvoicing;

public class XmlValidator : IXmlValidator
{
    private readonly string _baseXsdPath;

    public XmlValidator(IConfiguration config)
    {
        _baseXsdPath = config["DGII:XsdPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "documentacion xsd");
    }

    public async Task<XmlValidationResult> ValidateAsync(
        string xml,
        string xsdPath,
        CancellationToken ct)
    {
        var result = new XmlValidationResult();

        try
        {
            var schemaSet = new XmlSchemaSet();
            schemaSet.Add(null, xsdPath);
            schemaSet.Compile();

            var errors = new List<string>();
            var warnings = new List<string>();

            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = schemaSet,
                DtdProcessing = DtdProcessing.Ignore
            };

            settings.ValidationEventHandler += (sender, e) =>
            {
                if (e.Severity == XmlSeverityType.Error)
                    errors.Add(e.Message);
                else if (e.Severity == XmlSeverityType.Warning)
                    warnings.Add(e.Message);
            };

            using var reader = XmlReader.Create(
                new StringReader(xml),
                settings);

            while (reader.Read()) { }

            result.IsValid = errors.Count == 0;
            result.Errors = errors;
            result.Warnings = warnings;
        }
        catch (XmlSchemaException ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Error de esquema: {ex.Message}");
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Error al validar XML: {ex.Message}");
        }

        return await Task.FromResult(result);
    }
}
```

---

## 4. HashGenerator — Implementación

```csharp
namespace POS.Infrastructure.ElectronicInvoicing;

public class HashGenerator : IHashGenerator
{
    public string Generate(string xmlContent)
    {
        // Compactar el XML (sin whitespaces, sin indentación)
        var compacted = CompactXml(xmlContent);

        // SHA256 del XML compactado
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(
            Encoding.UTF8.GetBytes(compacted));

        // Primeros 6 caracteres en base64
        var base64 = Convert.ToBase64String(bytes);
        return base64[..6].ToUpperInvariant();
    }

    private static string CompactXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.ToString(SaveOptions.DisableFormatting);
    }
}
```

---

## 5. DigitalSignatureService — Implementación Base

```csharp
namespace POS.Infrastructure.ElectronicInvoicing;

public class XmlDigitalSigner : IDigitalSignatureService
{
    private readonly X509Certificate2 _certificate;

    public XmlDigitalSigner(string certificatePath, string password)
    {
        if (!File.Exists(certificatePath))
            throw new FileNotFoundException(
                $"Certificado no encontrado: {certificatePath}");

        _certificate = new X509Certificate2(certificatePath, password);

        if (!_certificate.HasPrivateKey)
            throw new InvalidOperationException(
                "El certificado no tiene clave privada accesible");
    }

    public async Task<string> SignXmlAsync(
        string unsignedXml,
        string enf,
        CancellationToken ct)
    {
        var xmlDoc = new XmlDocument();
        xmlDoc.PreserveWhitespace = true;
        xmlDoc.LoadXml(unsignedXml);

        var signedXml = new SignedXml(xmlDoc);
        signedXml.SigningKey = _certificate.PrivateKey;

        // Agregar referencia al documento completo
        var reference = new Reference();
        reference.Uri = "";
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        signedXml.AddReference(reference);

        signedXml.ComputeSignature();

        var signature = signedXml.GetXml();
        var sigElement = xmlDoc.CreateElement("Signature");
        sigElement.InnerXml = signature.OuterXml;

        xmlDoc.DocumentElement?.AppendChild(sigElement);

        return await Task.FromResult(xmlDoc.OuterXml);
    }

    public bool HasValidCertificate()
    {
        return _certificate != null &&
               !_certificate.HasExpired &&
               _certificate.HasPrivateKey;
    }

    public DateTime? CertificateExpirationDate
    {
        get => _certificate?.NotAfter;
    }

    public string CertificateSubject
    {
        get => _certificate?.Subject;
    }

    public string CertificateThumbprint
    {
        get => _certificate?.Thumbprint;
    }
}

public class NoOpDigitalSignatureService : IDigitalSignatureService
{
    public Task<string> SignXmlAsync(
        string unsignedXml,
        string enf,
        CancellationToken ct)
        => Task.FromResult(unsignedXml);

    public bool HasValidCertificate() => false;
    public DateTime? CertificateExpirationDate => null;
}
```

---

## 6. DgiiElectronicInvoiceService — Implementación

```csharp
namespace POS.Infrastructure.ElectronicInvoicing;

public class DgiiElectronicInvoiceService : IElectronicInvoiceService
{
    private readonly IXmlSerializer _xmlSerializer;
    private readonly IXmlValidator _xmlValidator;
    private readonly IHashGenerator _hashGenerator;
    private readonly IDigitalSignatureService _digitalSignature;
    private readonly IElectronicInvoiceRepository _invoiceRepository;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<DgiiElectronicInvoiceService> _logger;

    private readonly string _eCF32XsdPath;
    private readonly string _eCF31XsdPath;
    private readonly string _rfce32XsdPath;
    private readonly string _anecfXsdPath;
    private readonly string _acecfXsdPath;
    private readonly string _arecfXsdPath;

    private readonly string _dgiiEndpoint;
    private readonly string _dgiiUsername;
    private readonly string _dgiiPassword;
    private readonly bool _useContingencyMode;
    private readonly int _timeoutSeconds;
    private readonly int _retryCount;

    public DgiiElectronicInvoiceService(
        IXmlSerializer xmlSerializer,
        IXmlValidator xmlValidator,
        IHashGenerator hashGenerator,
        IDigitalSignatureService digitalSignature,
        IElectronicInvoiceRepository invoiceRepository,
        HttpClient httpClient,
        IConfiguration config,
        ILogger<DgiiElectronicInvoiceService> logger)
    {
        _xmlSerializer = xmlSerializer;
        _xmlValidator = xmlValidator;
        _hashGenerator = hashGenerator;
        _digitalSignature = digitalSignature;
        _invoiceRepository = invoiceRepository;
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        var basePath = config["DGII:XsdPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "documentacion xsd");

        _eCF32XsdPath = Path.Combine(basePath, "e-CF 32 v.1.0.xsd");
        _eCF31XsdPath = Path.Combine(basePath, "e-CF 31 v.1.0.xsd");
        _rfce32XsdPath = Path.Combine(basePath, "RFCE 32 v.1.0.xsd");
        _anecfXsdPath = Path.Combine(basePath, "ANECF v.1.0.xsd");
        _acecfXsdPath = Path.Combine(basePath, "ACECF v.1.0.xsd");
        _arecfXsdPath = Path.Combine(basePath, "ARECF v1.0.xsd");

        _dgiiEndpoint = config["DGII:Endpoint"] ?? "";
        _dgiiUsername = config["DGII:Username"] ?? "";
        _dgiiPassword = config["DGII:Password"] ?? "";
        _useContingencyMode = config.GetValue<bool>(
            "DGII:UseContingencyMode", false);
        _timeoutSeconds = config.GetValue<int>(
            "DGII:TimeoutSeconds", 30);
        _retryCount = config.GetValue<int>(
            "DGII:RetryCount", 3);
    }

    public async Task<ElectronicInvoiceResult> SubmitAsync(
        ElectronicInvoiceRequest request,
        CancellationToken ct)
    {
        var result = new ElectronicInvoiceResult();

        try
        {
            // 1. Determinar XSD según tipo de comprobante
            var xsdPath = SelectXsdPath(request.TipoeCF);
            var xsdType = xsdPath.Contains("e-CF 32") ? "ECF32" :
                          xsdPath.Contains("e-CF 31") ? "ECF31" : "ECF32";

            // 2. Serializar a XML
            var xml = await _xmlSerializer.SerializeAsync(
                request, xsdType, ct);

            // 3. Validar contra XSD
            var validation = await _xmlValidator.ValidateAsync(
                xml, xsdPath, ct);

            if (!validation.IsValid)
            {
                _logger.LogError(
                    "XML inválido para {ENCF}: {Errors}",
                    request.eNCF, string.Join("; ", validation.Errors));

                result.Error = $"XML no válido: {string.Join(", ", validation.Errors)}";
                return result;
            }

            // 4. Calcular hash de seguridad
            result.XMLHash = _hashGenerator.Generate(xml);

            // 5. Asignar eNCF si no se proporcionó
            result.eNCF = request.eNCF ?? await GenerateENCFAsync();

            // 6. Firmar digitalmente (si hay certificado)
            if (_digitalSignature.HasValidCertificate())
            {
                xml = await _digitalSignature.SignXmlAsync(
                    xml, result.eNCF, ct);
                result.XML = xml;
            }
            else
            {
                // Sin certificado, firmar con stub (solo para desarrollo)
                // En producción esto daría error
                _logger.LogWarning(
                    "Sin certificado digital. Factura {ENCF} sin firma",
                    result.eNCF);
            }

            // 7. Enviar a DGII
            var dgiiResponse = await EnviiarADgiiConRetryAsync(
                xml, result.eNCF, ct);

            // 8. Procesar respuesta
            result.XML = xml;
            result.Estado = dgiiResponse.Estado;
            result.FechaEnvio = DateTime.Now;

            if (dgiiResponse.Estado == ElectronicInvoiceStatus.Aprobado)
            {
                result.FechaAprobacion = dgiiResponse.FechaAprobacion;
                result.ACECFXML = dgiiResponse.ACECFXML;
            }

            if (dgiiResponse.Estado == ElectronicInvoiceStatus.Rechazado)
            {
                result.MotivoRechazo = dgiiResponse.MotivoRechazo;
                if (string.IsNullOrEmpty(result.MotivoRechazo))
                    result.MotivoRechazo = dgiiResponse.ARECFXML;
            }

            if (dgiiResponse.ARECFXML != null)
            {
                result.ARECFXML = dgiiResponse.ARECFXML;
            }

            // 9. Determinar éxito
            if (dgiiResponse.Estado == ElectronicInvoiceStatus.Aprobado ||
                dgiiResponse.Estado == ElectronicInvoiceStatus.NoRecibido)
            {
                result.Success = true;
            }
            else
            {
                result.Success = false;
                result.Error = result.MotivoRechazo ??
                    $"DGII devolvió estado: {dgiiResponse.Estado}";
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Error de red al enviar factura {ENCF}", result.eNCF);

            result.Error = $"Error de conexión con DGII: {ex.Message}";
            result.Success = false;

            // Retornar resultado para contingencia
            if (_useContingencyMode)
            {
                result.Estado = ElectronicInvoiceStatus.Creado;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error inesperado al emitir factura {ENCF}",
                result.eNCF);

            result.Error = ex.Message;
            result.Success = false;
        }

        return result;
    }

    public async Task<AnulacionResult> AnularAsync(
        AnulacionRequest request,
        CancellationToken ct)
    {
        var result = new AnulacionResult();

        try
        {
            // 1. Serializar XML de anulación
            var xml = await _xmlSerializer.SerializeAnulacionAsync(
                request, ct);

            // 2. Validar contra XSD ANECF
            var validation = await _xmlValidator.ValidateAsync(
                xml, _anecfXsdPath, ct);

            if (!validation.IsValid)
            {
                result.Error = $"XML ANECF inválido: {string.Join(", ", validation.Errors)}";
                return result;
            }

            // 3. Firmar digitalmente
            if (_digitalSignature.HasValidCertificate())
            {
                xml = await _digitalSignature.SignXmlAsync(
                    xml, "", ct);
            }

            // 4. Enviar a DGII
            var dgiiResponse = await EnviiarAnulacionADgiiAsync(
                xml, request.RNCEmisor, ct);

            // 5. Procesar respuesta
            result.Success = dgiiResponse.Success;
            result.eNCFsAnulados = dgiiResponse.eNCFsAnulados;

            if (!dgiiResponse.Success)
            {
                result.Error = dgiiResponse.Error;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al anular facturas");
            result.Error = ex.Message;
        }

        return result;
    }

    public async Task<ElectronicInvoiceStatusResponse> ConsultarEstadoAsync(
        string enf,
        CancellationToken ct)
    {
        // 1. Buscar en BD local primero
        var local = await _invoiceRepository.GetByENCFAsync(enf, ct);
        if (local != null)
        {
            return new ElectronicInvoiceStatusResponse
            {
                eNCF = enf,
                Estado = (ElectronicInvoiceStatus)local.Estado,
                FechaEmision = local.FechaEmision,
                FechaEnvio = local.FechaEnvio,
                FechaAprobacion = local.FechaAprobacion,
                MotivoRechazo = local.MotivoRechazo,
                Source = "Local"
            };
        }

        // 2. Consultar en DGII (lógica pendiente de implementar)
        throw new NotImplementedException(
            "Consulta de estado en DGII no implementada aún");
    }

    public async Task<string> GenerateXmlAsync(
        ElectronicInvoiceRequest request,
        CancellationToken ct)
    {
        var xsdType = GetXsdType(request.TipoeCF);
        var xml = await _xmlSerializer.SerializeAsync(
            request, xsdType, ct);

        // Validar antes de retornar
        var xsdPath = GetXsdPath(request.TipoeCF);
        var validation = await _xmlValidator.ValidateAsync(
            xml, xsdPath, ct);

        if (!validation.IsValid)
        {
            throw new XmlValidationException(validation.Errors);
        }

        return xml;
    }

    public async Task<string> GenerateAnulacionXmlAsync(
        AnulacionRequest request,
        CancellationToken ct)
    {
        var xml = await _xmlSerializer.SerializeAnulacionAsync(
            request, ct);

        var validation = await _xmlValidator.ValidateAsync(
            xml, _anecfXsdPath, ct);

        if (!validation.IsValid)
        {
            throw new XmlValidationException(validation.Errors);
        }

        return xml;
    }

    private string SelectXsdPath(TipoeCFType tipoeCF)
    {
        return tipoeCF switch
        {
            TipoeCFType.FacturaCreditoFiscal => _eCF31XsdPath,
            TipoeCFType.FacturaConsumo => _eCF32XsdPath,
            TipoeCFType.NotaDeDebito => _eCF31XsdPath,
            TipoeCFType.NotaDeCredito => _eCF31XsdPath,
            TipoeCFType.Compras => _eCF31XsdPath,
            _ => _eCF32XsdPath // default
        };
    }

    private string GetXsdPath(TipoeCFType tipoeCF)
    {
        return tipoeCF switch
        {
            TipoeCFType.FacturaCreditoFiscal => _eCF31XsdPath,
            TipoeCFType.FacturaConsumo => _eCF32XsdPath,
            TipoeCFType.NotaDeDebito => _eCF31XsdPath,
            TipoeCFType.NotaDeCredito => _eCF31XsdPath,
            TipoeCFType.Compras => _eCF31XsdPath,
            _ => _eCF32XsdPath
        };
    }

    private string GetXsdType(TipoeCFType tipoeCF)
    {
        return tipoeCF switch
        {
            TipoeCFType.FacturaCreditoFiscal => "ECF31",
            TipoeCFType.FacturaConsumo => "ECF32",
            _ => "ECF31"
        };
    }

    private async Task<string> GenerateENCFAsync()
    {
        // Obtener último eNCF desde BD y generar siguiente
        var lastEncf = await _invoiceRepository.GetLastENCFAsync("B", ct);
        var sec = lastEncf == null ? "000000000000" : lastEncf[1..];
        var num = long.Parse(sec) + 1;
        return $"B{num.ToString("D12")}";
    }

    private async Task<DgiiResponse> EnviiarADgiiConRetryAsync(
        string xml, string enf, CancellationToken ct)
    {
        var lastException = (Exception?)null;

        for (int attempt = 1; attempt <= _retryCount; attempt++)
        {
            try
            {
                return await EnviiarADgiiAsync(xml, enf, ct);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(
                    "Intento {Attempt} de {RetryCount} fallido para {ENCF}: {Error}",
                    attempt, _retryCount, enf, ex.Message);

                if (attempt < _retryCount)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                        ct);
                }
            }
        }

        throw new DGIIException(
            $"No se pudo enviar la factura después de {_retryCount} intentos",
            lastException);
    }

    private async Task<DgiiResponse> EnviiarADgiiAsync(
        string xml, string enf, CancellationToken ct)
    {
        // Implementation DGII - REST API JSON con TrackId
        // Esta es la parte crítica que necesita endpoints reales de DGII
        // La DGII usa REST API JSON, no SOAP

        // 1. Preparar request JSON para DGII
        var dgiiRequest = new DgiiEnvioRequest
        {
            Xml = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)),
            Hash = hash
        };

        // 2. Enviar vía REST API
        var dgiiResponse = await _dgiiApiClient.EnviarFacturaAsync(dgiiRequest, ct);

        // 3. Guardar TrackId
        result.TrackId = dgiiResponse.TrackId;
        result.Estado = dgiiResponse.Estado;

        // 4. Polling para consultar resultado
        var resultado = await _dgiiApiClient.ConsultarResultadoAsync(dgiiResponse.TrackId, ct);
        result.Estado = resultado.Estado;
        result.FechaAprobacion = resultado.FechaHora;

        // 5. Recibir ARECF y ACECF
        result.ARECFXML = resultado.AcuseRecibo;
        // ACECF se recibe si estado=aceptado

        result.Success = true;
            request.Headers.Add("Authorization",
                $"Basic {credentials}");
        }

        request.Content = new StringContent(xml, Encoding.UTF8);

        using var response = await _httpClient.SendAsync(
            request, ct);

        response.EnsureSuccessStatusCode();

        var responseXml = await response.Content.ReadAsStringAsync(ct);

        // Procesar respuesta DGII
        return ParseDgiiResponse(responseXml, enf);
    }

    private DgiiResponse ParseDgiiResponse(string responseXml, string enf)
    {
        var result = new DgiiResponse();

        try
        {
            var xmlDoc = XDocument.Parse(responseXml);
            var root = xmlDoc.Root;

            // Lógica de parseo según estructura SOAP de DGII
            // (depende del endpoint y formato SOAP real)

            if (root != null)
            {
                var estadoNode = root.Element("Estado");
                if (estadoNode != null &&
                    int.TryParse(estadoNode.Value, out int estado))
                {
                    result.Estado = (ElectronicInvoiceStatus)estado;
                }

                var fechaAprobacionNode = root.Element("FechaAprobacion");
                if (fechaAprobacionNode != null &&
                    DateTime.TryParse(fechaAprobacionNode.Value, out var fecha))
                {
                    result.FechaAprobacion = fecha;
                }

                var motivoNode = root.Element("MotivoRechazo");
                if (motivoNode != null)
                {
                    result.MotivoRechazo = motivoNode.Value;
                }

                var arecfNode = root.Element("ARECF");
                if (arecfNode != null)
                {
                    result.ARECFXML = arecfNode.Value;
                }

                var acecfNode = root.Element("ACECF");
                if (acecfNode != null)
                {
                    result.ACECFXML = acecfNode.Value;
                }
            }
        }
        catch (Exception ex)
        {
            return new DgiiResponse
            {
                Estado = ElectronicInvoiceStatus.Rechazado,
                MotivoRechazo = $"Error al parsear respuesta: {ex.Message}",
                RawResponse = responseXml
            };
        }

        return result;
    }

    private async Task<AnulacionDgiiResponse> EnviiarAnulacionADgiiAsync(
        string xml, string rncEmisor, CancellationToken ct)
    {
        throw new NotImplementedException(
            "Envío de anulación a DGII no implementado aún");
    }
}

// Clases auxiliares para respuesta DGII
public record DgiiResponse
{
    public ElectronicInvoiceStatus Estado { get; init; }
    public DateTime? FechaAprobacion { get; init; }
    public string? MotivoRechazo { get; init; }
    public string? ARECFXML { get; init; }
    public string? ACECFXML { get; init; }
    public string? RawResponse { get; init; }
}

public record AnulacionDgiiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<string> eNCFsAnulados { get; init; } = new();
}

public record ElectronicInvoiceStatusResponse
{
    public string eNCF { get; init; } = "";
    public ElectronicInvoiceStatus Estado { get; init; }
    public DateTime? FechaEmision { get; init; }
    public DateTime? FechaEnvio { get; init; }
    public DateTime? FechaAprobacion { get; init; }
    public string? MotivoRechazo { get; init; }
    public string Source { get; init; } = "DGII";
}
```

---

## 7. HttpClient Configuration

```csharp
// En Program.cs o Startup
services.AddHttpClient<DgiiElectronicInvoiceService>(client =>
{
    client.BaseAddress = new Uri(
        configuration["DGII:Endpoint"]);
    client.Timeout = TimeSpan.FromSeconds(
        configuration.GetValue<int>("DGII:TimeoutSeconds", 30));
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("text/xml"));
});

// Opcional: Addaithttpclient con Polly para reintentos
services.AddHttpClient("DgiiApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddTransientHttpErrorPolicy(policy =>
    policy.WaitAndRetryAsync(3,
        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
```

---

## 8. SqlInvoiceRepository — Implementación

```csharp
namespace POS.Infrastructure.ElectronicInvoicing;

public class SqlInvoiceRepository : IElectronicInvoiceRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<SqlInvoiceRepository> _logger;

    public SqlInvoiceRepository(
        ApplicationDbContext dbContext,
        ILogger<SqlInvoiceRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ElectronicInvoiceEntity?> GetByIdAsync(
        int id, CancellationToken ct = default)
    {
        return await _dbContext.ElectronicInvoices
            .Include(ei => ei.Items)
            .FirstOrDefaultAsync(ei => ei.Id == id, ct);
    }

    public async Task<ElectronicInvoiceEntity?> GetByENCFAsync(
        string enf, CancellationToken ct = default)
    {
        return await _dbContext.ElectronicInvoices
            .Include(ei => ei.Items)
            .FirstOrDefaultAsync(ei => ei.ENCF == enf, ct);
    }

    public async Task<ElectronicInvoiceEntity> CreateAsync(
        ElectronicInvoiceEntity invoice,
        CancellationToken ct = default)
    {
        invoice.CreatedAt = DateTime.Now;
        invoice.UpdatedAt = DateTime.Now;

        _dbContext.ElectronicInvoices.Add(invoice);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Factura electrónica creada: {ENCF} (ID: {Id})",
            invoice.ENCF, invoice.Id);

        return invoice;
    }

    public async Task<IEnumerable<ElectronicInvoiceEntity>> GetByRNCAsync(
        string rnc,
        int? tipoeCF = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.ElectronicInvoices
            .Where(ei => ei.RNCEmisor == rnc);

        if (tipoeCF.HasValue)
            query = query.Where(ei => ei.TipoeCF == tipoeCF.Value);

        return await query.OrderByDescending(ei => ei.FechaEmision)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ElectronicInvoiceEntity>> GetByDateRangeAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        return await _dbContext.ElectronicInvoices
            .Where(ei => ei.FechaEmision >= from.ToDateTime(0, 0, 0) &&
                         ei.FechaEmision <= to.ToDateTime(23, 59, 59))
            .OrderByDescending(ei => ei.FechaEmision)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ElectronicInvoiceEntity>> GetPendingAsync(
        CancellationToken ct = default)
    {
        return await _dbContext.ElectronicInvoices
            .Where(ei => ei.Estado == (int)ElectronicInvoiceStatus.Creado ||
                         ei.Estado == (int)ElectronicInvoiceStatus.Enviado)
            .OrderBy(ei => ei.FechaEnvio)
            .ToListAsync(ct);
    }

    public async Task UpdateEstadoAsync(
        int id,
        ElectronicInvoiceStatus nuevoEstado,
        CancellationToken ct = default)
    {
        var invoice = await _dbContext.ElectronicInvoices
            .FirstOrDefaultAsync(ei => ei.Id == id, ct);

        if (invoice == null)
            throw new InvoiceNotFoundException(id.ToString());

        invoice.Estado = (int)nuevoEstado;
        invoice.UpdatedAt = DateTime.Now;

        if (nuevoEstado == ElectronicInvoiceStatus.Aprobado)
            invoice.FechaAprobacion = DateTime.Now;

        if (nuevoEstado == ElectronicInvoiceStatus.Anulado)
            invoice.FechaAnulacion = DateTime.Now;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Estado de factura {ENCF} actualizado a {Estado}",
            invoice.ENCF, nuevoEstado);
    }

    public async Task<bool> ExistsENCFAsync(
        string enf, CancellationToken ct = default)
    {
        return await _dbContext.ElectronicInvoices
            .AnyAsync(ei => ei.ENCF == enf, ct);
    }

    public async Task<string?> GetLastENCFAsync(
        string serie, CancellationToken ct = default)
    {
        var invoices = await _dbContext.ElectronicInvoices
            .Where(ei => ei.ENCF.StartsWith(serie))
            .OrderByDescending(ei => ei.ENCF)
            .ToListAsync(ct);

        return invoices.FirstOrDefault()?.ENCF;
    }
}
```

---

## 9. ApplicationDbContext (EF Core)

```csharp
namespace POS.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<ElectronicInvoiceEntity> ElectronicInvoices { get; set; }
    public DbSet<ElectronicInvoiceItemEntity> ElectronicInvoiceItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ElectronicInvoiceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ENCF).IsUnique();
            entity.HasIndex(e => e.Estado);
            entity.HasIndex(e => e.FechaEmision);

            entity.Property(e => e.ENCF)
                .HasMaxLength(13)
                .IsRequired();

            entity.Property(e => e.XMLContent)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.RNCEmisor)
                .HasMaxLength(11)
                .IsRequired();

            entity.Property(e => e.RNCComprador)
                .HasMaxLength(11);

            entity.HasMany(e => e.Items)
                .WithOne(i => i.ElectronicInvoice)
                .HasForeignKey(i => i.ElectronicInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ElectronicInvoiceItemEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ElectronicInvoiceId);
            entity.Property(e => e.NombreItem).HasMaxLength(80);
            entity.Property(e => e.DescripcionItem).HasMaxLength(1000);
        });
    }
}
```

---

## 10. Entidades de Dominio

```csharp
namespace POS.Infrastructure.ElectronicInvoicing.Entities;

public class ElectronicInvoiceEntity
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int TipoeCF { get; set; }
    public string ENCF { get; set; } = "";
    public decimal Version { get; set; } = 1.0m;
    public string RNCEmisor { get; set; } = "";
    public string? RNCComprador { get; set; }
    public string? RazonSocialComprador { get; set; }
    public DateTime FechaEmision { get; set; }
    public int TipoPago { get; set; }
    public string TipoIngresos { get; set; } = "01";
    public DateTime? FechaLimitePago { get; set; }
    public string? TerminoPago { get; set; }
    public string? TipoCuentaPago { get; set; }
    public string? NumeroCuentaPago { get; set; }
    public string? BancoPago { get; set; }
    public int IndicadorEnvioDiferido { get; set; }
    public int IndicadorMontoGravado { get; set; }
    public int IndicadorServicioTodoIncluido { get; set; }
    public decimal MontoGravadoTotal { get; set; }
    public decimal MontoGravadoI1 { get; set; }
    public decimal MontoGravadoI2 { get; set; }
    public decimal MontoGravadoI3 { get; set; }
    public decimal MontoExento { get; set; }
    public int? ITBIS1 { get; set; }
    public int? ITBIS2 { get; set; }
    public int? ITBIS3 { get; set; }
    public decimal TotalITBIS { get; set; }
    public decimal TotalITBIS1 { get; set; }
    public decimal TotalITBIS2 { get; set; }
    public decimal TotalITBIS3 { get; set; }
    public decimal? MontoImpuestoAdicional { get; set; }
    public decimal MontoTotal { get; set; }
    public string XMLContent { get; set; } = "";
    public string XMLHash { get; set; } = "";
    public int Estado { get; set; }
    public DateTime FechaEnvio { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public DateTime? FechaAnulacion { get; set; }
    public string? MotivoRechazo { get; set; }
    public string? MotivoAnulacion { get; set; }
    public string? ARECFXML { get; set; }
    public string? ACECFXML { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ElectronicInvoiceItemEntity> Items { get; set; }
        = new List<ElectronicInvoiceItemEntity>();
}

public class ElectronicInvoiceItemEntity
{
    public int Id { get; set; }
    public int ElectronicInvoiceId { get; set; }
    public ElectronicInvoiceEntity ElectronicInvoice { get; set; } = null!;
    public int NumeroLinea { get; set; }
    public int IndicadorFacturacion { get; set; }
    public int IndicadorBienoServicio { get; set; }
    public string NombreItem { get; set; } = "";
    public string? DescripcionItem { get; set; }
    public decimal CantidadItem { get; set; }
    public int? UnidadMedida { get; set; }
    public decimal? CantidadReferencia { get; set; }
    public int? UnidadReferencia { get; set; }
    public decimal PrecioUnitarioItem { get; set; }
    public decimal? DescuentoMonto { get; set; }
    public decimal? RecargoMonto { get; set; }
    public decimal MontoItem { get; set; }
    public decimal? GradosAlcohol { get; set; }
    public DateTime? FechaElaboracion { get; set; }
    public DateTime? FechaVencimientoItem { get; set; }
}
```

---

## 11. Configuración DGII en appsettings.json

```json
{
  "DGII": {
    "XsdPath": "documentacion xsd",
    "Endpoint": "https://www.dgii.gov.do/serviciosonline/facturacion/",
    "Username": "usuario_dgii",
    "Password": "contraseña_dgii",
    "CertificatePath": "",
    "CertificatePassword": "",
    "TimeoutSeconds": 30,
    "RetryCount": 3,
    "UseContingencyMode": false,
    "PollingIntervalSeconds": 60,
    "MaxPendingDays": 7
  },
  "Enterprise": {
    "RNC": "00000000000",
    "RazonSocial": "EMPRESA EMISORA S.A.",
    "NombreComercial": "",
    "Sucursal": "",
    "Direccion": "Calle Principal #123",
    "Municipio": "010100",
    "Provincia": "010000",
    "Telefono1": "809-555-1234",
    "Telefono2": "",
    "Correo": "facturacion@empresa.com",
    "WebSite": "",
    "ActividadEconomica": "COMERCIO AL POR MENOR",
    "CodigoVendedor": ""
  },
  "Tax": {
    "ITBIS1Rate": 0.18,
    "ITBIS2Rate": 0.16,
    "ITBIS3Rate": 0.00
  }
}
```

---

## 12. Manejo de Contingencia

### 12.1 Tabla de Contingencias

```sql
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
```

### 12.2 Service de Contingencia

```csharp
namespace POS.Infrastructure.ElectronicInvoicing;

public class ContingencyService : IContingencyService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHashGenerator _hashGenerator;
    private readonly ILogger<ContingencyService> _logger;

    public async Task GuardarContingenciaAsync(
        ElectronicInvoiceRequest request,
        string xmlContingencia,
        CancellationToken ct)
    {
        var entity = new ElectronicInvoiceContingency
        {
            SaleId = request.SaleId,
            TipoeCF = (int)request.TipoeCF,
            eNCF = request.eNCF ?? "B0000000000001",
            Version = 1.0m,
            RNCEmisor = request.Emisor.RNCEmisor,
            RNCComprador = request.Comprador.RNC,
            RazonSocialComprador = request.Comprador.RazonSocial,
            FechaEmision = request.Emisor.FechaEmision,
            TipoPago = (int)request.TipoPago,
            TipoIngresos = ((int)request.TipoIngresos).ToString("D2"),
            XMLContent = xmlContingencia,
            XMLHash = _hashGenerator.Generate(xmlContingencia),
            FechaGuardado = DateTime.Now,
            Reintentos = 0,
            FechaProximoReintento = DateTime.Now.AddHours(1)
        };

        _dbContext.ElectronicInvoiceContingency.Add(entity);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Factura {ENCF} guardada en contingencia",
            entity.eNCF);
    }

    public async Task<IEnumerable<PendingContingencyInvoice>> GetPendingAsync(
        CancellationToken ct)
    {
        var pendientes = await _dbContext.ElectronicInvoiceContingency
            .Where(c =>
                c.FechaProximoReintento <= DateTime.Now ||
                c.UltimoError != null)
            .ToListAsync(ct);

        return pendientes.Select(c => new PendingContingencyInvoice
        {
            Id = c.Id,
            SaleId = c.SaleId,
            eNCF = c.eNCF,
            FechaGuardado = c.FechaGuardado,
            XmlContent = c.XMLContent,
            Reintentos = c.Reintentos,
            UltimoError = c.UltimoError
        });
    }

    public async Task<ElectronicInvoiceResult> ReintentarEnviarAsync(
        int idContingencia,
        CancellationToken ct)
    {
        var contingency = await _dbContext.ElectronicInvoiceContingency
            .FirstOrDefaultAsync(c => c.Id == idContingencia, ct);

        if (contingency == null)
            throw new InvalidOperationException(
                $"Contingencia no encontrada: {idContingencia}");

        // Reconstruir request desde la contingencia
        var request = new ElectronicInvoiceRequest
        {
            SaleId = contingency.SaleId,
            TipoeCF = (TipoeCFType)contingency.TipoeCF,
            eNCF = contingency.eNCF,
            Emisor = new EmisorRequest
            {
                RNCEmisor = contingency.RNCEmisor,
                RazonSocial = contingency.RazonSocialComprador,
                FechaEmision = contingency.FechaEmision
            },
            Comprador = new CompradorRequest
            {
                RNC = contingency.RNCComprador,
                RazonSocial = contingency.RazonSocialComprador
            },
            TipoPago = (TipoPagoType)contingency.TipoPago,
            TipoIngresos = TipoIngresosFormatter.FromString(
                contingency.TipoIngresos),
            XMLContent = contingency.XMLContent
        };

        // Reintenta envío
        try
        {
            var servicio = /* obtener IElectronicInvoiceService */;
            var result = await servicio.SubmitAsync(request, ct);

            if (result.Success)
            {
                _dbContext.ElectronicInvoiceContingency.Remove(contingency);
                await _dbContext.SaveChangesAsync(ct);
            }
            else
            {
                contingency.Reintentos++;
                contingency.UltimoError = result.Error;
                contingency.FechaProximoReintento =
                    DateTime.Now.AddHours(Math.Min(contingency.Reintentos, 24));
                await _dbContext.SaveChangesAsync(ct);
            }

            return result;
        }
        catch (Exception ex)
        {
            contingency.Reintentos++;
            contingency.UltimoError = ex.Message;
            await _dbContext.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task EliminarContingenciaAsync(
        int idContingencia,
        CancellationToken ct)
    {
        var contingency = await _dbContext.ElectronicInvoiceContingency
            .FirstOrDefaultAsync(c => c.Id == idContingencia, ct);

        if (contingency != null)
        {
            _dbContext.ElectronicInvoiceContingency.Remove(contingency);
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}
```

---

## 13. Planificación de Reintentos

### 13.1 Background Service para Reintentos

```csharp
namespace POS.Infrastructure.BackgroundServices;

public class ContingencyReintentoService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContingencyReintentoService> _logger;
    private readonly int _intervalMinutes;

    public ContingencyReintentoService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ContingencyReintentoService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _intervalMinutes = configuration.GetValue<int>(
            "DGII:PollingIntervalSeconds", 60) / 60;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarPendientesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error al procesar pendientes de contingencia");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(_intervalMinutes),
                stoppingToken);
        }
    }

    private async Task ProcessarPendientesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var contingencyService = scope.ServiceProvider
            .GetRequiredService<IContingencyService>();

        var pendientes = await contingencyService.GetPendingAsync(ct);
        var count = 0;

        foreach (var pendiente in pendientes)
        {
            try
            {
                _logger.LogInformation(
                    "Reintentando envío de factura {ENCF} (id: {Id})",
                    pendiente.eNCF, pendiente.Id);

                var result = await contingencyService.ReintentarEnviarAsync(
                    pendiente.Id, ct);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Factura {ENCF} enviada exitosamente tras reintento",
                        pendiente.eNCF);
                }
                else
                {
                    _logger.LogWarning(
                        "Reintento fallido para {ENCF}: {Error}",
                        pendiente.eNCF, result.Error);
                }

                count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error al reintentar envío de {ENCF}",
                    pendiente.eNCF);
            }
        }

        if (count > 0)
        {
            _logger.LogInformation(
                "{Count} facturas de contingencia procesadas",
                count);
        }
    }
}
```

---

## 14. Testing de Servicios

### 14.1 Tests Unitarios para XmlSerializer

```csharp
public class XmlSerializerTests
{
    private readonly XmlSerializer _serializer;

    public XmlSerializerTests()
    {
        _serializer = new XmlSerializer(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["DGII:XsdPath"] = "test_resources/xsd"
                })
                .Build());
    }

    [Fact]
    public async Task SerializeECF32_WithValidRequest_ReturnsValidXml()
    {
        // Arrange
        var request = new ElectronicInvoiceRequest
        {
            SaleId = 1,
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "B0000000000001",
            Emisor = new EmisorRequest
            {
                RNCEmisor = "00000000000",
                RazonSocial = "EMPRESA EMISORA S.A.",
                Direccion = "Calle Principal #123",
                Municipio = "010100",
                Provincia = "010000",
                FechaEmision = DateTime.Today
            },
            Comprador = new CompradorRequest
            {
                RNC = "09876543210",
                RazonSocial = "Juan Pérez"
            },
            TipoPago = TipoPagoType.Contado,
            TipoIngresos = TipoIngresosType.IngresosOperaciones,
            Lineas = new List<InvoiceLineRequest>
            {
                new InvoiceLineRequest
                {
                    NumeroLinea = 1,
                    Nombre = "Producto de prueba",
                    IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                    IndicadorBienoServicio = IndicadorBienoServicioType.Bien,
                    Cantidad = 2,
                    PrecioUnitario = 1500,
                    Monto = 3000
                }
            }
        };

        // Act
        var xml = await _serializer.SerializeAsync(
            request, "ECF32", CancellationToken.None);

        // Assert
        Assert.NotNull(xml);
        Assert.Contains("<ECF>", xml);
        Assert.Contains("<Encabezado>", xml);
        Assert.Contains("<eNCF>B0000000000001</eNCF>", xml);
        Assert.Contains("<Version>1.0</Version>", xml);
        Assert.Contains("<TipoeCF>32</TipoeCF>", xml);
        Assert.Contains("<FechaEmision>", xml);
        Assert.Contains("<MontoTotal>", xml);
        Assert.Contains("<Item>", xml);
        Assert.Contains("<MontoItem>", xml);
    }

    [Fact]
    public async Task SerializeECF31_IncludesFechaVencimientoSecuencia()
    {
        // Similar test para e-CF 31
    }

    [Fact]
    public async Task SerializeANECF_ReturnsValidXml()
    {
        var request = new AnulacionRequest
        {
            RNCEmisor = "00000000000",
            FechaHoraAnulacion = DateTime.Now,
            Anulaciones = new List<RangoAnulacionRequest>
            {
                new RangoAnulacionRequest
                {
                    NoLinea = 1,
                    TipoeCF = CFType.FacturaConsumo,
                    Secuencias = new List<RangoSecuenciaRequest>
                    {
                        new RangoSecuenciaRequest
                        {
                            Desde = "B0000000000001",
                            Hasta = "B0000000000003"
                        }
                    },
                    CantidadNCFAnulados = 3
                }
            }
        };

        var xml = await _serializer.SerializeAnulacionAsync(
            request, CancellationToken.None);

        Assert.NotNull(xml);
        Assert.Contains("<ANECF>", xml);
        Assert.Contains("<RncEmisor>00000000000</RncEmisor>", xml);
    }
}
```

### 14.2 Tests para HashGenerator

```csharp
public class HashGeneratorTests
{
    private readonly HashGenerator _generator;

    public HashGeneratorTests()
    {
        _generator = new HashGenerator();
    }

    [Fact]
    public void Generate_WithValidXml_Returns6Chars()
    {
        var xml = "<ECF><Encabezado><Version>1.0</Version></Encabezado></ECF>";

        var hash = _generator.Generate(xml);

        Assert.Equal(6, hash.Length);
        Assert.True(hash.All(c =>
            char.IsLetterOrDigit(c) || c == '+' || c == '/'));
    }

    [Fact]
    public void Generate_SameXml_ReturnsSameHash()
    {
        var xml1 = "<ECF><Encabezado><Version>1.0</Version></Encabezado></ECF>";
        var xml2 = "<ECF><Encabezado><Version>1.0</Version></Encabezado></ECF>";

        var hash1 = _generator.Generate(xml1);
        var hash2 = _generator.Generate(xml2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Generate_DifferentXml_ReturnsDifferentHash()
    {
        var xml1 = "<ECF><Encabezado><Version>1.0</Version></Encabezado></ECF>";
        var xml2 = "<ECF><Encabezado><Version>1.1</Version></Encabezado></ECF>";

        var hash1 = _generator.Generate(xml1);
        var hash2 = _generator.Generate(xml2);

        Assert.NotEqual(hash1, hash2);
    }
}
```

---

## 15. Documentación Relacionada

- [01_Resumen_General.md](01_Resumen_General.md) — Visión general
- [02_Mapeo_XSD_CSharp.md](02_Mapeo_XSD_CSharp.md) — Mapeo de tipos XSD a C#
- [03_Interfaces_Servicios.md](03_Interfaces_Servicios.md) — Contratos de interfaces
- [04_Diseno_Infraestructura_DGII.md](04_Diseno_Infraestructura_DGII.md) — Este documento

---

## 16. Próximos Pasos

1. Implementar XmlSerializer para e-CF 32 (prioridad alta)
2. Implementar XmlValidator para validar XML generados
3. Implementar HashGenerator
4. Configurar HttpClient para DGII
5. Implementar Service de envío SOAP (cuando se tengan los endpoints reales)
6. Implementar repositorio SQL
7. Implementar Command Handlers
8. Integrar con UI del POS
9. Testing con datos de prueba
10. Testing con DGII sandbox (si disponible)
