using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using POS.Application.DTOs;
using POS.Domain.Common;
using POS.Domain.Types;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;
using Xunit;

namespace POS.Domain.Types.Tests;

/// <summary>
/// Pruebas de la firma XML-DSig del comprobante (FASE 5, sub-fase 5.1).
/// </summary>
/// <remarks>
/// El serializer (5.0) emite el documento con el hueco estructural ds:Signature que el XSD exige;
/// el firmador lo REEMPLAZA por la firma real. Las pruebas usan un certificado RSA efímero creado
/// en memoria, igual que la prueba preexistente del firmador base.
/// </remarks>
public class FirmaXmlDsigTests
{
    private const string NsXmlDsig = "http://www.w3.org/2000/09/xmldsig#";

    private readonly XmlSerializer _serializer = new();
    private readonly XmlValidator _validator = new();

    private static string RutaXsd32()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "documentacion xsd", "e-CF 32 v.1.0.xsd");
        Assert.True(File.Exists(ruta), $"No se encontró el XSD oficial en {ruta}.");
        return ruta;
    }

    internal static X509Certificate2 CrearCertificadoDePrueba(string sujeto = "CN=PosPalasy Emisor de Pruebas")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(sujeto, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    /// <summary>Request canónico con datos que cumplen los patrones del XSD.</summary>
    internal static ElectronicInvoiceRequest RequestCanonico() => new()
    {
        TipoeCF = TipoeCFType.FacturaConsumo,
        eNCF = "E320000000001",
        Version = "1.0",
        FechaFactura = new FechaDominicana(new DateOnly(2026, 9, 19)),
        Emisor = new EmisorRequest
        {
            RNC = "13100000001",
            RazonSocial = "POS Palasy SRL",
            NombreComercial = "POS Palasy",
            Direccion = "Av. Winston Churchill #100",
            Telefono = "809-555-1234",
            Email = "facturacion@pospalasy.com",
            CodigoProvincia = "01",
            CodigoMunicipio = "010100"
        },
        Comprador = new CompradorRequest
        {
            RNC = "10100000101",
            RazonSocial = "Cliente Final SA",
            Direccion = "Av. 27 de Febrero"
        },
        Items = new List<InvoiceItemRequest>
        {
            new()
            {
                Indice = 1,
                Descripcion = "Refresco Cola 500ml",
                Cantidad = 2,
                PrecioUnitario = 50.00m,
                Subtotal = 100.00m,
                ITBIS = 18.00m,
                Total = 118.00m,
                IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                UnidadMedida = UnidadMedidaType.Botella
            }
        },
        Totales = new TotalesRequest
        {
            SubTotal = 100.00m,
            MontoGravadoTotal = 100.00m,
            MontoGravadoI1 = 100.00m,
            TotalITBIS = 18.00m,
            TotalITBIS1 = 18.00m,
            Total = 118.00m
        }
    };

    [Fact]
    public void DocumentoFirmado_VerificaCriptograficamenteYContieneUnaSoloFirma()
    {
        using var cert = CrearCertificadoDePrueba();
        var firmador = new FirmadorComprobanteECF(new XmlDigitalSigner());

        var firmado = firmador.Firmar(_serializer.Serialize(RequestCanonico()), cert);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(firmado);

        // Exactamente UNA firma: el hueco fue reemplazado, no acumulado.
        var firmas = doc.GetElementsByTagName("Signature", NsXmlDsig);
        Assert.Equal(1, firmas.Count);
        Assert.True(((XmlElement)firmas[0]!).HasChildNodes, "La firma debe tener contenido (no el hueco vacío).");

        var signedXml = new SignedXml(doc);
        signedXml.LoadXml((XmlElement)firmas[0]!);
        Assert.True(signedXml.CheckSignature(cert, true), "La firma debe verificar con el certificado firmante.");
    }

    [Fact]
    public void DocumentoFirmado_SigueValidandoContraElXsdOficial()
    {
        using var cert = CrearCertificadoDePrueba();
        var firmador = new FirmadorComprobanteECF(new XmlDigitalSigner());

        var firmado = firmador.Firmar(_serializer.Serialize(RequestCanonico()), cert);
        var resultado = _validator.Validate(firmado, RutaXsd32());

        Assert.True(
            resultado.EsValido,
            "El documento FIRMADO debe seguir validando contra el XSD oficial. Errores: " + Environment.NewLine +
            string.Join(Environment.NewLine, resultado.Errores.Select(e => $"- {e}")));
    }

    [Fact]
    public void Firmador_ReemplazaElHuecoEstructuralPorLaFirmaReal()
    {
        using var cert = CrearCertificadoDePrueba();
        var firmador = new FirmadorComprobanteECF(new XmlDigitalSigner());
        var xmlSinFirmar = _serializer.Serialize(RequestCanonico());

        // El sin-firmar trae el hueco vacío; el firmado contiene los bloques de la firma.
        Assert.Contains(NsXmlDsig, xmlSinFirmar);

        var firmado = firmador.Firmar(xmlSinFirmar, cert);

        Assert.Contains("<SignatureValue>", firmado);
        Assert.Contains("<X509Certificate>", firmado);
        Assert.Contains("<SignedInfo>", firmado);
    }

    [Fact]
    public void DocumentoAlterado_NoVerificaFirma()
    {
        // La propiedad que la DGII ejercerá contra nosotros: cualquier alteración del contenido
        // invalida la firma. Se verifica aquí con el mismo algoritmo de detección.
        using var cert = CrearCertificadoDePrueba();
        var firmador = new FirmadorComprobanteECF(new XmlDigitalSigner());

        var firmado = firmador.Firmar(_serializer.Serialize(RequestCanonico()), cert);
        var alterado = firmado.Replace("Refresco Cola 500ml", "Refresco ADULTERADO");

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(alterado);
        var signedXml = new SignedXml(doc);
        signedXml.LoadXml((XmlElement)doc.GetElementsByTagName("Signature", NsXmlDsig)[0]!);

        Assert.False(signedXml.CheckSignature(cert, true), "Un documento alterado no debe verificar.");
    }

    [Fact]
    public void CertificadoSinClavePrivada_NoFirma()
    {
        using var cert = CrearCertificadoDePrueba();
        var publico = X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));    // solo parte pública
        var firmador = new FirmadorComprobanteECF(new XmlDigitalSigner());

        var ex = Assert.Throws<ReglaDeNegocioException>(() =>
            firmador.Firmar(_serializer.Serialize(RequestCanonico()), publico));

        Assert.Equal("CERTIFICADO_SIN_CLAVE_PRIVADA", ex.Codigo);
    }
}

/// <summary>
/// Pruebas del proveedor del certificado del emisor: resolución, caché por fecha de archivo,
/// contraseña errónea y estado legible.
/// </summary>
public class ProveedorCertificadoDigitalTests
{
    private static string CarpetaTemporal() =>
        Path.Combine(Path.GetTempPath(), $"pospalasy-cert-{Guid.NewGuid():N}");

    private static string GuardarPfx(string carpeta, string nombre, string sujeto, string password)
    {
        Directory.CreateDirectory(carpeta);
        using var cert = FirmaXmlDsigTests.CrearCertificadoDePrueba(sujeto);
        var ruta = Path.Combine(carpeta, nombre);
        File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pkcs12, password));
        return ruta;
    }

    [Fact]
    public void SinCertificadoInstalado_DevuelveNullYDescribeElEstado()
    {
        var proveedor = new ProveedorCertificadoDigital(
            Path.Combine(CarpetaTemporal(), "emisor.pfx"), null);

        Assert.Null(proveedor.ObtenerCertificado());
        Assert.Contains("No hay certificado digital", proveedor.DescribirEstado());
    }

    [Fact]
    public void PfxValido_CargaClavePrivadaYDescribeVigencia()
    {
        var ruta = GuardarPfx(CarpetaTemporal(), "emisor.pfx", "CN=Emisor Vigente", "clave-secreta");
        var proveedor = new ProveedorCertificadoDigital(ruta, "clave-secreta");

        var certificado = proveedor.ObtenerCertificado();

        Assert.NotNull(certificado);
        Assert.True(certificado!.HasPrivateKey);
        Assert.Contains("CN=Emisor Vigente", proveedor.DescribirEstado());
        Assert.Contains("vence el", proveedor.DescribirEstado());
    }

    [Fact]
    public void PasswordErronea_LanzaExcepcionCriptografica()
    {
        var ruta = GuardarPfx(CarpetaTemporal(), "emisor.pfx", "CN=Emisor Protegido", "clave-correcta");
        var proveedor = new ProveedorCertificadoDigital(ruta, "clave-erronea");

        // El servicio de facturación traduce esta excepción a un fallo de firma permanente.
        Assert.ThrowsAny<CryptographicException>(() => proveedor.ObtenerCertificado());
    }

    [Fact]
    public void ArchivoReemplazado_SeRecargaSinReiniciarElProceso()
    {
        var carpeta = CarpetaTemporal();
        var ruta = GuardarPfx(carpeta, "emisor.pfx", "CN=Emisor Primero", "clave");
        var proveedor = new ProveedorCertificadoDigital(ruta, "clave");

        var primero = proveedor.ObtenerCertificado();
        Assert.Contains("CN=Emisor Primero", primero!.SubjectName.Name);

        // Se instala un certificado nuevo (simula la pantalla de Configuración).
        var rutaNueva = GuardarPfx(carpeta, "nuevo.pfx", "CN=Emisor Segundo", "clave");
        File.Copy(rutaNueva, ruta, overwrite: true);
        File.SetLastWriteTimeUtc(ruta, DateTime.UtcNow.AddMinutes(1));   // asegura cambio de marca de tiempo

        var segundo = proveedor.ObtenerCertificado();
        Assert.Contains("CN=Emisor Segundo", segundo!.SubjectName.Name);
    }
}
