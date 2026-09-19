using System;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using POS.Domain.Common;
using POS.Infrastructure.XmlSerialization;

namespace POS.Infrastructure.Services;

/// <summary>
/// Contrato del firmador de comprobantes e-CF (XML-DSig enveloped con RSA-SHA256/SHA-256).
/// </summary>
public interface IFirmadorComprobanteECF
{
    /// <summary>
    /// Firma el XML del comprobante y devuelve el documento firmado. La firma se verifica
    /// criptográficamente antes de devolverla; un documento cuya firma no verifique nunca se
    /// entrega para transmisión.
    /// </summary>
    string Firmar(string xmlSinFirmar, X509Certificate2 certificado);
}

/// <summary>
/// El serializer (sub-fase 5.0) emite el documento con un hueco estructural
/// <c>&lt;ds:Signature/&gt;</c> vacío tras <c>FechaHoraFirma</c>: el XSD exige exactamente ese
/// elemento y el documento nace estructuralmente completo. Este firmador REEMPLAZA el hueco por la
/// firma real: un apéndice al final dejaría dos elementos ds:Signature y el documento inválido.
/// </summary>
/// <remarks>
/// Sobre documentos sin hueco (p. ej. XML de anulación ANECF) firma por apéndice, como el firmador
/// base de <see cref="XmlDigitalSigner"/>. La verificación usa el certificado firmante (no la cadena
/// de confianza), igual que hará la DGII al validar la firma del documento recibido.
/// </remarks>
public sealed class FirmadorComprobanteECF : IFirmadorComprobanteECF
{
    private const string NamespaceXmlDsig = "http://www.w3.org/2000/09/xmldsig#";

    private readonly IXmlDigitalSigner _signer;

    public FirmadorComprobanteECF(IXmlDigitalSigner signer)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
    }

    public string Firmar(string xmlSinFirmar, X509Certificate2 certificado)
    {
        if (string.IsNullOrWhiteSpace(xmlSinFirmar))
            throw new ArgumentException("El XML a firmar no puede estar vacío.", nameof(xmlSinFirmar));
        if (certificado is null)
            throw new ArgumentNullException(nameof(certificado));
        if (!certificado.HasPrivateKey)
            throw new ReglaDeNegocioException(
                "El certificado digital no contiene clave privada: no puede firmar comprobantes.",
                "CERTIFICADO_SIN_CLAVE_PRIVADA");

        var sinHueco = QuitarHuecoDeFirma(xmlSinFirmar);
        var firmado = _signer.Sign(sinHueco, certificado);
        VerificarFirma(firmado, certificado);
        return firmado;
    }

    /// <summary>
    /// Retira el hueco vacío de firma si el documento lo trae, para que la firma real ocupe su
    /// lugar y el documento firmado contenga UN solo elemento ds:Signature.
    /// </summary>
    private static string QuitarHuecoDeFirma(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);

        if (doc.DocumentElement is not { } raiz)
            return xml;

        foreach (var nodo in raiz.ChildNodes)
        {
            if (nodo is XmlElement elemento
                && elemento.LocalName == "Signature"
                && elemento.NamespaceURI == NamespaceXmlDsig
                && !elemento.HasChildNodes)
            {
                raiz.RemoveChild(elemento);
                return doc.OuterXml;
            }
        }

        return xml;
    }

    /// <summary>
    /// Verificación criptográfica de barrera: el documento no sale del firmador si su firma no
    /// verifica con el certificado firmante.
    /// </summary>
    private static void VerificarFirma(string xmlFirmado, X509Certificate2 certificado)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xmlFirmado);

        var nodoFirma = doc.GetElementsByTagName("Signature", NamespaceXmlDsig);
        if (nodoFirma.Count == 0 || nodoFirma[0] is not XmlElement firma)
            throw new ReglaDeNegocioException(
                "La firma XML-DSig no quedó incrustada en el documento; el comprobante no se transmite.",
                "FIRMA_AUSENTE");

        var signedXml = new SignedXml(doc);
        try
        {
            signedXml.LoadXml(firma);
        }
        catch (Exception ex)
        {
            throw new ReglaDeNegocioException(
                $"La firma incrustada no es un XML-DSig legible: {ex.Message}", "FIRMA_INVALIDA");
        }

        if (!signedXml.CheckSignature(certificado, true))
            throw new ReglaDeNegocioException(
                "La firma del comprobante no verifica criptográficamente; el documento no se transmite.",
                "FIRMA_INVALIDA");
    }
}
