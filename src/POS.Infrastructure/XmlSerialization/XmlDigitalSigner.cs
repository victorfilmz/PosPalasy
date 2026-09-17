using System;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace POS.Infrastructure.XmlSerialization;

public interface IXmlDigitalSigner
{
    string Sign(string xmlContent, X509Certificate2 certificate);
}

/// <summary>
/// Servicio de firma digital XML-DSig según la especificación de DGII República Dominicana.
/// Firma el documento completo con el certificado digital autorizado por INDOTEL.
/// </summary>
public class XmlDigitalSigner : IXmlDigitalSigner
{
    public string Sign(string xmlContent, X509Certificate2 certificate)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            throw new ArgumentException("El contenido XML no puede estar vacío.", nameof(xmlContent));

        if (certificate == null)
            throw new ArgumentNullException(nameof(certificate));

        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("El certificado digital proporcionado no contiene la clave privada necesaria para firmar.");

        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        xmlDoc.LoadXml(xmlContent);

        var signedXml = new SignedXml(xmlDoc)
        {
            SigningKey = certificate.GetRSAPrivateKey()
        };

        // Algoritmos requeridos por DGII
        if (signedXml.SignedInfo != null)
        {
            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigCanonicalizationUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;
        }

        // Referencia al documento completo (enveloped)
        var reference = new Reference { Uri = "" };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        reference.DigestMethod = SignedXml.XmlDsigSHA256Url;

        signedXml.AddReference(reference);

        // KeyInfo con certificado X.509
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificate));
        signedXml.KeyInfo = keyInfo;

        // Calcular firma
        signedXml.ComputeSignature();

        // Incrustar elemento de firma en el XML
        var xmlDigitalSignature = signedXml.GetXml();
        xmlDoc.DocumentElement?.AppendChild(xmlDoc.ImportNode(xmlDigitalSignature, true));

        return xmlDoc.OuterXml;
    }
}
