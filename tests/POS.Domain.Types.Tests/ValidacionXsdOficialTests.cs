using System;
using System.IO;
using System.Linq;
using POS.Application.DTOs;
using POS.Domain.Common;
using POS.Domain.Types;
using POS.Infrastructure.XmlSerialization;
using Xunit;

namespace POS.Domain.Types.Tests;

/// <summary>
/// Validación REAL contra los XSD oficiales de la DGII (FASE 5, sub-fase 5.0).
/// </summary>
/// <remarks>
/// Este es el "test detector": por primera vez el XML construido por el sistema se compara contra
/// el contrato del XSD oficial. Los XSD se copian al directorio de salida por la referencia del
/// csproj de Infraestructura; la prueba los resuelve desde ahí.
/// </remarks>
public class ValidacionXsdOficialTests
{
    private readonly XmlSerializer _serializer = new();
    private readonly XmlValidator _validator = new();

    /// <summary>Request canónico con datos que cumplen los patrones del XSD (RNC 11, teléfono, correo).</summary>
    private static ElectronicInvoiceRequest RequestCanonico() => new()
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

    /// <summary>Ruta del XSD del tipo 32 desde el directorio de salida.</summary>
    private static string RutaXsd32()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "documentacion xsd", "e-CF 32 v.1.0.xsd");
        Assert.True(File.Exists(ruta), $"No se encontró el XSD oficial en {ruta}. Revisar la copia al output del csproj.");
        return ruta;
    }

    /// <summary>Ruta del XSD del tipo 34 (Nota de Crédito) desde el directorio de salida.</summary>
    private static string RutaXsd34()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "documentacion xsd", "e-CF 34 v.1.0.xsd");
        Assert.True(File.Exists(ruta), $"No se encontró el XSD oficial en {ruta}. Revisar la copia al output del csproj.");
        return ruta;
    }

    /// <summary>Request canónico del tipo 34 con la referencia al comprobante modificado.</summary>
    private static ElectronicInvoiceRequest RequestNotaCredito()
    {
        var request = RequestCanonico();
        request.TipoeCF = TipoeCFType.NotaCredito;
        request.eNCF = "E340000000001";
        request.IndicadorNotaCredito = 0;
        request.NCFModificado = "E320000000001";
        request.FechaNCFModificado = "19-09-2026";
        request.CodigoModificacion = 3;
        request.MotivoModificacion = "Devolución de venta";
        return request;
    }

    [Fact]
    public void EcFCanonico_ValidaContraElXsdOficial()
    {
        // El serializer emite el hueco estructural de la firma (ds:Signature) que el XSD exige tras
        // FechaHoraFirma: el documento nace estructuralmente completo y válido tal como se produce.
        var xml = _serializer.Serialize(RequestCanonico());
        var resultado = _validator.Validate(xml, RutaXsd32());

        Assert.True(
            resultado.EsValido,
            "El e-CF canónico debe validar contra el XSD oficial. Errores: " + Environment.NewLine +
            string.Join(Environment.NewLine, resultado.Errores.Select(e => $"- {e}")));
    }

    [Fact]
    public void Serializer_EmiteElHuecoDeLaFirmaXmlDsigQueExigeElEstandar()
    {
        // El XSD exige exactamente un elemento tras FechaHoraFirma: la ranura de ds:Signature.
        // Sin ella el documento sería rechazado por la DGII; con ella, la firma (5.1) solo llena
        // el hueco sin alterar la estructura validada.
        var xml = _serializer.Serialize(RequestCanonico());

        Assert.Contains("http://www.w3.org/2000/09/xmldsig#", xml);
        Assert.Contains("Signature", xml);
    }

    [Fact]
    public void XmlCorrupto_FallaConMensajesLegibles()
    {
        var resultado = _validator.Validate("<ECF><Encabezado>", RutaXsd32());

        Assert.False(resultado.EsValido);
        Assert.NotEmpty(resultado.Errores);
    }

    [Fact]
    public void EcFConEncfInvalido_FallaYElErrorMencionaElCampo()
    {
        var request = RequestCanonico();
        request.eNCF = "FA-123";    // viola el patrón del XSD: 13 caracteres alfanuméricos

        var xml = _serializer.Serialize(request);
        var resultado = _validator.Validate(xml, RutaXsd32());

        Assert.False(resultado.EsValido);
        Assert.Contains(resultado.Errores, e => e.Contains("eNCF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MapaXsd_CubreTodosLosTiposDelEnum()
    {
        foreach (var tipo in Enum.GetValues<TipoeCFType>())
        {
            // No exige que el archivo exista en disco; exige que el mapa lo conozca.
            var nombre = MapaXsdComprobante.ArchivoDe(tipo);
            Assert.False(string.IsNullOrWhiteSpace(nombre));
        }
    }

    [Fact]
    public void ResolverRuta_DevuelveElXsdDelTipo32EnElOutput()
    {
        var ruta = MapaXsdComprobante.ResolverRuta(TipoeCFType.FacturaConsumo);

        Assert.NotNull(ruta);
        Assert.Contains("e-CF 32", Path.GetFileName(ruta));
    }

    // ------------------------------------------------------------------
    // FASE 5.4 — Nota de Crédito e-CF 34: validación real contra su XSD oficial.
    // ------------------------------------------------------------------

    [Fact]
    public void NotaCreditoCanonica_ValidaContraElXsd34Oficial()
    {
        var xml = _serializer.Serialize(RequestNotaCredito());
        var resultado = _validator.Validate(xml, RutaXsd34());

        Assert.True(
            resultado.EsValido,
            "La nota de crédito canónica debe validar contra el XSD 34 oficial. Errores: " +
            Environment.NewLine + string.Join(Environment.NewLine, resultado.Errores.Select(e => $"- {e}")));
    }

    [Fact]
    public void NotaCredito_LlevaLaInformacionReferenciaCompletaEnElOrdenDelXsd()
    {
        var xml = _serializer.Serialize(RequestNotaCredito());

        Assert.Contains("<InformacionReferencia>", xml);
        Assert.Contains("<NCFModificado>E320000000001</NCFModificado>", xml);
        Assert.Contains("<FechaNCFModificado>19-09-2026</FechaNCFModificado>", xml);
        Assert.Contains("<CodigoModificacion>3</CodigoModificacion>", xml);
        Assert.Contains("<RazonModificacion>Devolución de venta</RazonModificacion>", xml);

        // Orden de la secuencia del XSD: NCFModificado → FechaNCFModificado → CodigoModificacion.
        Assert.True(
            xml.IndexOf("<NCFModificado>", StringComparison.Ordinal)
                < xml.IndexOf("<FechaNCFModificado>", StringComparison.Ordinal),
            "NCFModificado debe preceder a FechaNCFModificado.");
        Assert.True(
            xml.IndexOf("<FechaNCFModificado>", StringComparison.Ordinal)
                < xml.IndexOf("<CodigoModificacion>", StringComparison.Ordinal),
            "FechaNCFModificado debe preceder a CodigoModificacion.");
    }

    [Fact]
    public void NotaCredito_IdDocLlevaElIndicadorNotaCreditoObligatorio()
    {
        var xml = _serializer.Serialize(RequestNotaCredito());

        Assert.Contains("<TipoeCF>34</TipoeCF>", xml);
        Assert.Contains("<IndicadorNotaCredito>0</IndicadorNotaCredito>", xml);
    }

    [Fact]
    public void NotaCreditoSinReferencia_FallaElXsdPorInformacionReferenciaAusente()
    {
        // Test detector: sin InformacionReferencia el documento NO puede pasar el esquema oficial.
        var request = RequestNotaCredito();
        request.NCFModificado = null;

        var xml = _serializer.Serialize(request);
        var resultado = _validator.Validate(xml, RutaXsd34());

        Assert.False(resultado.EsValido);
        Assert.Contains(resultado.Errores, e =>
            e.Contains("InformacionReferencia", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NotaCreditoConCodigoModificacionFueraDeCatalogo_FallaElXsd()
    {
        // El XSD restringe CodigoModificacion a 1..4 (1=Anula, 2=Texto, 3=Montos, 4=Contingencia).
        var request = RequestNotaCredito();
        request.CodigoModificacion = 9;

        var xml = _serializer.Serialize(request);
        var resultado = _validator.Validate(xml, RutaXsd34());

        Assert.False(resultado.EsValido);
        Assert.Contains(resultado.Errores, e =>
            e.Contains("CodigoModificacion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FacturaDeConsumo_NoLlevaInformacionReferenciaNiIndicadorNotaCredito()
    {
        // La rama del tipo 32 no debe arrastrar elementos del tipo 34.
        var xml = _serializer.Serialize(RequestCanonico());

        Assert.DoesNotContain("InformacionReferencia", xml);
        Assert.DoesNotContain("IndicadorNotaCredito", xml);
    }

    [Fact]
    public void TipoSinMapa_LanzaErrorDeNegocio()
    {
        // El mapa cubre los 10 tipos; se comprueba el comportamiento defensivo con un valor fuera.
        Assert.Throws<ReglaDeNegocioException>(() => MapaXsdComprobante.ArchivoDe((TipoeCFType)99));
    }
}
