using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using POS.Application.DTOs;
using POS.Domain.Types;
using POS.Infrastructure.XmlSerialization;
using Xunit;

namespace POS.Domain.Types.Tests;

public class InfrastructureTests
{
    private readonly XmlSerializer _serializer = new();
    private readonly XmlDigitalSigner _signer = new();

    [Fact]
    public void XmlSerializer_Serialize_ProducesWellFormedECFXml()
    {
        var request = new ElectronicInvoiceRequest
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "E320000000001",
            FechaFactura = new FechaDominicana(new DateOnly(2026, 9, 16)),
            Emisor = new EmisorRequest
            {
                RNC = "13100000001",
                RazonSocial = "POS Palasy SRL",
                Direccion = "Av. Winston Churchill #100",
                Telefono = "809-555-1234",
                Email = "facturacion@pospalasy.com",
                CodigoProvincia = "01",
                CodigoMunicipio = "010100"
            },
            Comprador = new CompradorRequest
            {
                RNC = "101000001",
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

        var xml = _serializer.Serialize(request);

        Assert.NotNull(xml);
        Assert.Contains("<ECF>", xml);
        Assert.Contains("<Version>1.0</Version>", xml);
        Assert.Contains("<TipoeCF>32</TipoeCF>", xml);
        Assert.Contains("<eNCF>E320000000001</eNCF>", xml);
        Assert.Contains("<RNCEmisor>13100000001</RNCEmisor>", xml);
        Assert.Contains("<RazonSocialEmisor>POS Palasy SRL</RazonSocialEmisor>", xml);
        Assert.Contains("<MontoTotal>118.00</MontoTotal>", xml);
        Assert.Contains("<NombreItem>Refresco Cola 500ml</NombreItem>", xml);

        // Verificar que carga en XmlDocument sin errores de sintaxis
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        Assert.Equal("ECF", doc.DocumentElement?.Name);
    }

    [Fact]
    public void XmlSerializer_SerializeAnulacion_ProducesValidANECF()
    {
        var request = new AnulacionRequest
        {
            RNCEmisor = "13100000001",
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCFDesde = "E320000000001",
            eNCFHasta = "E320000000005",
            CantidadSecuencias = 5,
            CodigoMotivoAnulacion = 1,
            Motivo = "Deterioro"
        };

        var xml = _serializer.SerializeAnulacion(request);

        Assert.Contains("<ANECF>", xml);
        Assert.Contains("<RNCEmisor>13100000001</RNCEmisor>", xml);
        Assert.Contains("<TipoeCF>32</TipoeCF>", xml);
        Assert.Contains("<CantidadSecuencias>5</CantidadSecuencias>", xml);
    }

    [Fact]
    public void XmlDigitalSigner_Sign_AddsValidXmlSignature()
    {
        var rawXml = "<ECF><Encabezado><Version>1.0</Version><RNCEmisor>13100000001</RNCEmisor></Encabezado></ECF>";

        // Generar certificado RSA efímero en memoria para la prueba
        using var rsa = RSA.Create(2048);
        var certReq = new CertificateRequest("CN=POS Palasy Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));

        var signedXml = _signer.Sign(rawXml, cert);

        Assert.NotNull(signedXml);
        Assert.Contains("<Signature", signedXml);
        Assert.Contains("<SignatureValue>", signedXml);
        Assert.Contains("<X509Certificate>", signedXml);

        // Verificar validez de la firma usando SignedXml
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(signedXml);

        var signedDoc = new System.Security.Cryptography.Xml.SignedXml(doc);
        var sigNode = doc.GetElementsByTagName("Signature")[0] as XmlElement;
        Assert.NotNull(sigNode);

        signedDoc.LoadXml(sigNode);
        var isValid = signedDoc.CheckSignature(cert, true);
        Assert.True(isValid);
    }
}
