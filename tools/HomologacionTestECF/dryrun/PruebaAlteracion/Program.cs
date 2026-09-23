// Prueba de aislamiento del caso 5 (firma alterada).
// Firma un e-CF con el firmador REAL de PosPalasy, altera el SignatureValue con la MISMA
// rutina del verificador, y verifica con la MISMA lógica del servidor falso (SignedXml +
// certificado incrustado). Imprime cada paso para diagnosticar dónde se pierde la detección.
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using POS.Application.DTOs;
using POS.Domain.Types;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;

var rsa = RSA.Create(2048);
var req = new CertificateRequest("CN=Prueba Alteracion", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

var request = new ElectronicInvoiceRequest
{
    TipoeCF = TipoeCFType.FacturaConsumo,
    eNCF = "E320000000001",
    FechaFactura = new FechaDominicana(DateOnly.FromDateTime(DateTime.Today)),
    Emisor = new EmisorRequest
    {
        RNC = "13100000001", RazonSocial = "POS Palasy SRL", NombreComercial = "POS Palasy",
        Direccion = "Av. 27", Telefono = "809-555-1234", Email = "a@b.com",
        CodigoProvincia = "01", CodigoMunicipio = "010100"
    },
    Comprador = new CompradorRequest { RNC = "10100000101", RazonSocial = "Cliente Final SA", Direccion = "Av. 27" },
    Items = new List<InvoiceItemRequest>
    {
        new() { Indice = 1, Descripcion = "X", Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m,
                ITBIS = 1.80m, Total = 11.80m, IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                UnidadMedida = UnidadMedidaType.Botella }
    },
    Totales = new TotalesRequest { SubTotal = 10m, MontoGravadoTotal = 10m, MontoGravadoI1 = 10m,
                                   TotalITBIS = 1.80m, TotalITBIS1 = 1.80m, Total = 11.80m }
};

var serializer = new XmlSerializer();
var signer = new XmlDigitalSigner();
var firmador = new FirmadorComprobanteECF(signer);

var xml = serializer.Serialize(request);
request.CodigoSeguridadeCF = "AB12CD";
xml = serializer.Serialize(request);
var firmado = firmador.Firmar(xml, cert);
Console.WriteLine("1. Firmado OK, contiene SignatureValue: " + firmado.Contains("<SignatureValue>"));

// Alteración EXACTA del verificador (caso 5): XDocument.Parse + primer carácter cambiado.
var doc = XDocument.Parse(firmado);
var sv = doc.Descendants().First(e => e.Name.LocalName == "SignatureValue");
var valor = sv.Value;
sv.Value = valor[0] == 'A' ? "B" + valor[1..] : "A" + valor[1..];
var alterado = doc.ToString();
Console.WriteLine($"2. Alterado: {valor[..12]}... → {sv.Value[..12]}...");

// Verificación EXACTA del servidor falso.
Console.WriteLine("3. Verificación del ORIGINAL: " + Verificar(firmado));
Console.WriteLine("4. Verificación del ALTERADO: " + Verificar(alterado));

// ¿El XDocument.Parse+ToString alteró el whitespace del documento (cambia el digest)?
var mismoBlanco = firmado.Replace("\r\n", "\n") == alterado.Replace("\r\n", "\n");
Console.WriteLine("5. ¿alterado == firmado salvo SignatureValue? " + mismoBlanco);

static string Verificar(string xml)
{
    try
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        var firma = doc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
        if (firma.Count == 0 || firma[0] is not XmlElement fe) return "sin firma";
        var sx = new SignedXml(doc);
        sx.LoadXml(fe);
        X509Certificate2? c = null;
        foreach (KeyInfoClause clause in sx.KeyInfo)
            if (clause is KeyInfoX509Data d && d.Certificates.Count > 0)
            { c = (X509Certificate2)d.Certificates[0]!; break; }
        if (c is null) return "sin certificado";
        return sx.CheckSignature(c, true) ? "VERIFICA" : "NO VERIFICA";
    }
    catch (Exception ex) { return "excepción: " + ex.Message; }
}
