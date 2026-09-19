using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using POS.Application.DTOs;
using POS.Application.Services;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;
using POS.Infrastructure.DGII;
using POS.Infrastructure.Persistence;
using POS.Infrastructure.Persistence.Repositories;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;
using Xunit;

namespace POS.Ventas.Tests;

/// <summary>
/// Pruebas de integración de la firma XML-DSig del comprobante (FASE 5, sub-fase 5.1).
/// </summary>
/// <remarks>
/// Ejercitan <see cref="DgiiElectronicInvoiceService.EnviarAsync"/> con base SQLite real, cola y
/// repositorios reales y un certificado RSA efímero en memoria: el contrato verificado es que
/// NADA se transmite sin firma verificable en modo real, y que el documento firmado queda
/// persistido para trazabilidad fiscal.
/// </remarks>
public class FirmaComprobanteIntegrationTests
{
    private const string NsXmlDsig = "http://www.w3.org/2000/09/xmldsig#";

    private static POSDbContext CrearContexto(SqliteConnection conexion) =>
        new(new DbContextOptionsBuilder<POSDbContext>().UseSqlite(conexion).Options);

    private static string RutaPfxTemporal(string password)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=PosPalasy Emisor Integracion", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var ruta = Path.Combine(Path.GetTempPath(), $"pospalasy-emisor-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pkcs12, password));
        return ruta;
    }

    /// <summary>Request canónico equivalente al del detector XSD (5.0).</summary>
    private static ElectronicInvoiceRequest CrearRequestCanonico() => new()
    {
        TipoeCF = TipoeCFType.FacturaConsumo,
        eNCF = "E320000000009",
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

    /// <summary>
    /// Siembra un comprobante tal como lo dejó la venta (XsdValidado, con hueco de firma) y su
    /// elemento de cola pendiente.
    /// </summary>
    private static (int FacturaId, int ColaId) SembrarComprobante(POSDbContext ctx)
    {
        var request = CrearRequestCanonico();
        var factura = new ElectronicInvoice
        {
            TipoeCF = request.TipoeCF,
            eNCF = request.eNCF,
            Version = request.Version,
            RNCEmisor = request.Emisor.RNC,
            RazonSocialEmisor = request.Emisor.RazonSocial,
            FechaEmision = request.FechaFactura.ToXmlString(),
            TipoIngresos = request.TipoIngresos,
            TipoPago = request.TipoPago,
            MontoGravadoTotal = request.Totales.MontoGravadoTotal,
            MontoGravadoI1 = request.Totales.MontoGravadoI1,
            MontoExento = request.Totales.MontoExento,
            TotalITBIS = request.Totales.TotalITBIS,
            TotalITBIS1 = request.Totales.TotalITBIS1,
            MontoTotal = request.Totales.Total,
            XMLContent = new XmlSerializer().Serialize(request),
            XMLHash = "A7B3C9",
            Estado = EstadoFacturaElectronica.NoEnviado,
            EstadoEmision = EstadoEmisionECF.XsdValidado
        };

        ctx.ElectronicInvoices.Add(factura);
        ctx.SaveChanges();

        var cola = new EmisionDGIIQueue
        {
            FacturaId = factura.Id,
            eNCF = factura.eNCF,
            XmlFirmado = factura.XMLContent,
            Intentos = 0
        };
        ctx.EmisionesDGIIQueue.Add(cola);
        ctx.SaveChanges();

        return (factura.Id, cola.Id);
    }

    private static DgiiElectronicInvoiceService CrearServicio(
        POSDbContext ctx,
        IDgiiApiClient cliente,
        bool simulador,
        string? rutaPfx = null,
        string? password = null)
    {
        return new DgiiElectronicInvoiceService(
            new XmlSerializer(),
            new XmlValidator(),
            new HashGenerator(),
            cliente,
            new InvoiceRepository(ctx),
            new CommonRepositories(ctx),
            new EmisionDGIIQueueRepository(ctx),
            NullLogger<DgiiElectronicInvoiceService>.Instance,
            dgiiConfig: new DgiiConfig { ModoSimulador = simulador },
            codigoSeguridad: new GeneradorCodigoSeguridad(),
            proveedorCertificado: new ProveedorCertificadoDigital(
                rutaPfx ?? Path.Combine(Path.GetTempPath(), "pospalasy-sin-certificado", "emisor.pfx"),
                password),
            firmador: new FirmadorComprobanteECF(new XmlDigitalSigner()));
    }

    private static X509Certificate2 ExtraerCertificadoDelXml(XmlDocument doc)
    {
        var nodo = (XmlElement)doc.GetElementsByTagName("X509Certificate", NsXmlDsig)[0]!;
        return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(nodo.InnerText.Trim()));
    }

    [Fact]
    public async Task EnvioSinCertificado_NoSaltaALaRed_YElComprobanteVuelveALaCola()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, colaId) = SembrarComprobante(ctx);

        var cliente = new ClienteDgiiFalso();
        var servicio = CrearServicio(ctx, cliente, simulador: false);

        var respuesta = await servicio.EnviarAsync(facturaId);

        Assert.False(respuesta.Exitoso);
        Assert.Equal(0, cliente.EnviosRealizados);                      // NADA salió a la red
        Assert.Equal(EstadoEmisionECF.Encolada, respuesta.EstadoEmision);
        Assert.True(respuesta.EsRecuperable);
        Assert.Contains("certificado", respuesta.Mensaje, StringComparison.OrdinalIgnoreCase);

        await using var verificacion = CrearContexto(conexion);
        var factura = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == facturaId);
        Assert.Equal(EstadoEmisionECF.Encolada, factura.EstadoEmision);
        Assert.DoesNotContain("<SignatureValue>", factura.XMLContent);  // persistió intacto, sin firmar

        var cola = await verificacion.EmisionesDGIIQueue.AsNoTracking().SingleAsync(q => q.Id == colaId);
        Assert.Equal(EstadoColaDGII.Pendiente, cola.Estado);            // vuelve a la cola…
        Assert.NotNull(cola.ProximoIntentoUtc);                         // …con reintento programado
        Assert.NotNull(cola.UltimoError);
    }

    [Fact]
    public async Task EnvioConCertificado_FirmaVerifica_YElXmlPersistidoEsElFirmado()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, colaId) = SembrarComprobante(ctx);

        var password = "clave-integracion";
        var rutaPfx = RutaPfxTemporal(password);
        try
        {
            var cliente = new ClienteDgiiFalso();
            var servicio = CrearServicio(ctx, cliente, simulador: false, rutaPfx, password);

            var respuesta = await servicio.EnviarAsync(facturaId);

            Assert.True(respuesta.Exitoso, respuesta.Mensaje);
            Assert.Equal(EstadoEmisionECF.ConfirmadaEnvio, respuesta.EstadoEmision);
            Assert.Equal(1, cliente.EnviosRealizados);

            await using var verificacion = CrearContexto(conexion);
            var factura = await verificacion.ElectronicInvoices.AsNoTracking()
                .SingleAsync(f => f.Id == facturaId);
            Assert.Equal(EstadoEmisionECF.ConfirmadaEnvio, factura.EstadoEmision);

            // La firma persistida verifica criptográficamente con el certificado INCORPORADO en el
            // propio documento (la forma en que la DGII la comprobará al recibirlo).
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(factura.XMLContent);
            var signedXml = new SignedXml(doc);
            signedXml.LoadXml((XmlElement)doc.GetElementsByTagName("Signature", NsXmlDsig)[0]!);

            Assert.True(
                signedXml.CheckSignature(ExtraerCertificadoDelXml(doc), true),
                "La firma persistida debe verificar con el certificado del documento.");

            var cola = await verificacion.EmisionesDGIIQueue.AsNoTracking().SingleAsync(q => q.Id == colaId);
            Assert.True(cola.EnviadoExitosamente);
        }
        finally
        {
            File.Delete(rutaPfx);
        }
    }

    [Fact]
    public async Task ReintentoTrasInstalarCertificado_ElComprobanteSaleSolo()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = SembrarComprobante(ctx);

        // Intento 1: sin certificado. El comprobante no se transmite ni se marca definitivo.
        var cliente = new ClienteDgiiFalso();
        var servicioSinCert = CrearServicio(ctx, cliente, simulador: false);
        var primera = await servicioSinCert.EnviarAsync(facturaId);

        Assert.False(primera.Exitoso);
        Assert.Equal(0, cliente.EnviosRealizados);

        // Intento 2: el certificado quedó instalado; el reintento (programado por la cola) emite.
        var password = "clave-integracion";
        var rutaPfx = RutaPfxTemporal(password);
        try
        {
            var servicioConCert = CrearServicio(ctx, cliente, simulador: false, rutaPfx, password);
            var segunda = await servicioConCert.EnviarAsync(facturaId);

            Assert.True(segunda.Exitoso, segunda.Mensaje);
            Assert.Equal(1, cliente.EnviosRealizados);                  // UN envío para ambos intentos
            Assert.Equal(EstadoEmisionECF.ConfirmadaEnvio, segunda.EstadoEmision);
        }
        finally
        {
            File.Delete(rutaPfx);
        }
    }
}
