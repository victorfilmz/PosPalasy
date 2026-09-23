using System;
using System.Linq;
using System.Threading.Tasks;
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace POS.Ventas.Tests;

/// <summary>
/// Gate G2 (FASE 6.2) — recuperación del régimen de contingencia: N comprobantes emitidos con la
/// DGII caída (contingencia declarada, en cola) se transmiten de vuelta en orden cronológico, sin
/// duplicados ni reenvío de los ya confirmados, y cada uno consolida su resultado fiscal real.
/// </summary>
public class RecuperacionContingenciaIntegrationTests
{
    private static POSDbContext CrearContexto(SqliteConnection conexion) =>
        new(new DbContextOptionsBuilder<POSDbContext>().UseSqlite(conexion).Options);

    private static string RutaPfxTemporal()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=PosPalasy Contingencia", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pospalasy-contingencia-{Guid.NewGuid():N}.pfx");
        System.IO.File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pkcs12, "clave-contingencia"));
        return ruta;
    }

    private static DgiiElectronicInvoiceService CrearServicio(POSDbContext ctx, ClienteDgiiFalso cliente)
    {
        var rutaPfx = RutaPfxTemporal();
        return new DgiiElectronicInvoiceService(
            new XmlSerializer(),
            new XmlValidator(),
            new HashGenerator(),
            cliente,
            new InvoiceRepository(ctx),
            new CommonRepositories(ctx),
            new EmisionDGIIQueueRepository(ctx),
            NullLogger<DgiiElectronicInvoiceService>.Instance,
            dgiiConfig: new DgiiConfig { ModoSimulador = false },
            codigoSeguridad: new GeneradorCodigoSeguridad(),
            proveedorCertificado: new ProveedorCertificadoDigital(rutaPfx, "clave-contingencia"),
            firmador: new FirmadorComprobanteECF(new XmlDigitalSigner()));
    }

    /// <summary>Siembra N comprobantes en contingencia (RFCE, consumo &lt; 250k) con su elemento de cola. El XML es real y válido contra el XSD.</summary>
    private static (int[] FacturaIds, string[] Encfs) SembrarContingencia(POSDbContext ctx, int cantidad)
    {
        var ahora = DateTime.UtcNow;
        var serializer = new XmlSerializer();
        var validator = new XmlValidator();
        var rutaXsd = ResolverXsd();
        var facturaIds = new int[cantidad];
        var encfs = new string[cantidad];

        for (int i = 0; i < cantidad; i++)
        {
            var request = SolicitudCanonica(i);
            var xml = serializer.Serialize(request);
            var validacion = validator.Validate(xml, rutaXsd);
            Assert.True(validacion.EsValido,
                "El comprobante de prueba debe validar contra el XSD oficial: " + string.Join(" | ", validacion.Errores));

            var factura = new ElectronicInvoice
            {
                TipoeCF = TipoeCFType.FacturaConsumo,
                eNCF = request.eNCF,
                Version = "1.0",
                RNCEmisor = request.Emisor.RNC,
                RazonSocialEmisor = request.Emisor.RazonSocial,
                FechaEmision = request.FechaFactura.ToXmlString(),
                TipoIngresos = request.TipoIngresos,
                TipoPago = request.TipoPago,
                MontoGravadoTotal = request.Totales.MontoGravadoTotal,
                MontoGravadoI1 = request.Totales.MontoGravadoI1,
                TotalITBIS = request.Totales.TotalITBIS,
                TotalITBIS1 = request.Totales.TotalITBIS1,
                MontoTotal = request.Totales.Total,
                XMLContent = xml,
                XMLHash = $"X{i:D5}",
                Estado = EstadoFacturaElectronica.PendienteReenvio,
                EstadoEmision = EstadoEmisionECF.Encolada,
                TipoContingencia = TipoContingenciaDgii.FallaPlataformaDgii,
                ContingenciaDesdeUtc = ahora.AddDays(-1),
                ContingenciaHastaUtc = RegimenContingencia.FinDeVentana(ahora.AddDays(-1))
            };
            ctx.ElectronicInvoices.Add(factura);
            ctx.SaveChanges();

            ctx.EmisionesDGIIQueue.Add(new EmisionDGIIQueue
            {
                FacturaId = factura.Id,
                eNCF = factura.eNCF,
                XmlFirmado = factura.XMLContent,
                Intentos = 0
            });
            ctx.SaveChanges();

            facturaIds[i] = factura.Id;
            encfs[i] = factura.eNCF;
        }

        return (facturaIds, encfs);
    }

    private static string? _rutaXsdCache;

    private static string ResolverXsd() =>
        _rutaXsdCache ??= Path.Combine(AppContext.BaseDirectory, "documentacion xsd", "e-CF 32 v.1.0.xsd");

    private static ElectronicInvoiceRequest SolicitudCanonica(int indice)
    {
        var request = new ElectronicInvoiceRequest
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = $"E3200000000{indice + 10:D2}",
            FechaFactura = new FechaDominicana(new DateOnly(2026, 9, 23)),
            TipoIngresos = TipoIngresosType.IngresosOperaciones,
            TipoPago = TipoPago.Contado,
            Emisor = new EmisorRequest
            {
                RNC = "13100000001",
                RazonSocial = "POS Palasy SRL",
                Direccion = "Av. 27 de Febrero #450",
                CodigoProvincia = "01",
                CodigoMunicipio = "010100"
            },
            Items =
            [
                new InvoiceItemRequest
                {
                    Indice = 1,
                    Descripcion = $"Producto de contingencia {indice}",
                    Cantidad = 1,
                    PrecioUnitario = 100.00m + indice,
                    IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                    Subtotal = 100.00m + indice,
                    ITBIS = 18.00m,
                    Total = 118.00m + indice
                }
            ],
            Totales = new TotalesRequest
            {
                MontoGravadoTotal = 100.00m + indice,
                MontoGravadoI1 = 100.00m + indice,
                TotalITBIS = 18.00m,
                TotalITBIS1 = 18.00m,
                Total = 118.00m + indice
            }
        };
        request.FechaHoraFirma = request.FechaFactura.ToXmlString();
        return request;
    }

    [Fact]
    public async Task Recuperacion_TransmiteEnOrdenSinDuplicadosYConsolidaResultado()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        const int cantidad = 4;
        var (facturaIds, encfs) = SembrarContingencia(ctx, cantidad);

        var cliente = new ClienteDgiiFalso
        {
            ProximaRespuesta = new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                Estado = "Aceptado",
                Mensaje = "Comprobante aceptado",
                SecuenciaUtilizada = true
            }
        };
        var servicio = CrearServicio(ctx, cliente);
        var repo = new InvoiceRepository(ctx);

        // Recuperación: retransmitir TODOS los comprobantes en contingencia, en orden de cola.
        var respuestas = new System.Collections.Generic.List<(string EnCF, ElectronicInvoiceResponse R)>();
        foreach (var facturaId in facturaIds)
        {
            var factura = await repo.GetByIdAsync(facturaId);
            Assert.NotNull(factura);
            respuestas.Add((factura!.eNCF, await servicio.EnviarAsync(facturaId)));
        }

        // Todos aceptados; el orden de transmisión respeta el orden de los comprobantes.
        Assert.All(respuestas, r => Assert.True(r.R.Exitoso, r.R.Mensaje));
        Assert.Equal(encfs, respuestas.Select(r => r.EnCF).ToArray());
        Assert.Equal(cantidad, cliente.EncfsEnviados.Count);

        // Sin duplicados: cada eNCF transmitido exactamente UNA vez, en el orden sembrado.
        Assert.Equal(encfs, cliente.EncfsEnviados);

        // Resultado fiscal consolidado y persistido para cada comprobante.
        await using var verificacion = CrearContexto(conexion);
        var comprobantes = await verificacion.ElectronicInvoices.AsNoTracking()
            .Where(f => facturaIds.Contains(f.Id)).ToListAsync();
        Assert.All(comprobantes, f =>
        {
            Assert.Equal(EstadoFacturaElectronica.Aceptado, f.Estado);
            Assert.Equal("Aceptado", f.EstadoDgii);
            Assert.NotNull(f.FechaEnvio);
        });

        // La cola queda sin pendientes de transmisión confirmada.
        Assert.Equal(0, await new EmisionDGIIQueueRepository(verificacion).ContarPendientesAsync());
    }

    [Fact]
    public async Task Recuperacion_NoReenviaLosYaConfirmados()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaIds, _) = SembrarContingencia(ctx, 3);

        // El segundo comprobante ya fue aceptado antes de la recuperación: no debe reenviarse.
        var yaConfirmada = await new InvoiceRepository(ctx).GetByIdAsync(facturaIds[1]);
        yaConfirmada!.Estado = EstadoFacturaElectronica.Aceptado;
        yaConfirmada.EstadoEmision = EstadoEmisionECF.ConfirmadaEnvio;
        yaConfirmada.TrackId = "TRACK-PREVIO";
        await ctx.SaveChangesAsync();

        var cliente = new ClienteDgiiFalso
        {
            ProximaRespuesta = new DgiiApiResponse { EsExitoso = true, CodigoHttp = 200, Estado = "Aceptado", Mensaje = "ok" }
        };
        var servicio = CrearServicio(ctx, cliente);

        foreach (var facturaId in facturaIds)
            await servicio.EnviarAsync(facturaId);

        // Solo 2 transmisiones: el aceptado conservó su TrackId previo y no salió a la red.
        Assert.Equal(2, cliente.EncfsEnviados.Count);
        await using var verificacion = CrearContexto(conexion);
        var conservado = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == facturaIds[1]);
        Assert.Equal("TRACK-PREVIO", conservado.TrackId);
    }
}
