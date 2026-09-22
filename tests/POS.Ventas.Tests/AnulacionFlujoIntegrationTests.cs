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
using Xunit;

namespace POS.Ventas.Tests;

/// <summary>
/// Pruebas de integración del flujo ANECF (anulación de rangos de secuencias NO utilizadas):
/// comando → serialización validada contra el XSD oficial → transmisión → consolidación honesta
/// del resultado → estado final del comprobante y del registro de anulación.
/// </summary>
/// <remarks>
/// El ANECF no es la nota de crédito de una devolución (eso es un e-CF 34 con
/// <c>InformacionReferencia</c>, ver 5.4): anula rangos de secuencias que la DGII declaró no
/// utilizadas. SQLite real y repositorios reales: lo que se verifica es la persistencia del
/// registro, el estado final del comprobante y la coherencia entre respuesta y datos.
/// </remarks>
public class AnulacionFlujoIntegrationTests
{
    private static POSDbContext CrearContexto(SqliteConnection conexion) =>
        new(new DbContextOptionsBuilder<POSDbContext>().UseSqlite(conexion).Options);

    private static DgiiElectronicInvoiceService CrearServicio(POSDbContext ctx, ClienteDgiiFalso cliente) =>
        new(
            new XmlSerializer(),
            new XmlValidator(),
            new HashGenerator(),
            cliente,
            new InvoiceRepository(ctx),
            new CommonRepositories(ctx),
            new EmisionDGIIQueueRepository(ctx),
            NullLogger<DgiiElectronicInvoiceService>.Instance,
            dgiiConfig: new DgiiConfig { ModoSimulador = false });

    private static AnulacionRequest SolicitudCanonica() => new()
    {
        RNCEmisor = "13100000001",
        TipoeCF = TipoeCFType.FacturaConsumo,
        eNCFDesde = "E320000000099",
        eNCFHasta = "E320000000101",
        CantidadSecuencias = 3,
        CodigoMotivoAnulacion = 5,
        Motivo = "Secuencias emitidas por error y nunca utilizadas"
    };

    [Fact]
    public async Task FlujoCompleto_AnulacionAceptada_RegistraConsolidaYAnulaElComprobante()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();

        // El rango anulado incluye la secuencia de un comprobante existente en el sistema.
        var factura = new ElectronicInvoice
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "E320000000100",
            Version = "1.0",
            RNCEmisor = "13100000001",
            RazonSocialEmisor = "POS Palasy SRL",
            FechaEmision = new FechaDominicana(new DateOnly(2026, 9, 22)).ToXmlString(),
            MontoTotal = 118.00m,
            XMLContent = "<ECF/>",
            XMLHash = "A1B2C3",
            Estado = EstadoFacturaElectronica.Aceptado,
            EstadoEmision = EstadoEmisionECF.ConfirmadaEnvio
        };
        ctx.ElectronicInvoices.Add(factura);
        await ctx.SaveChangesAsync();

        var cliente = new ClienteDgiiFalso();
        var servicio = CrearServicio(ctx, cliente);
        var request = SolicitudCanonica();

        var respuesta = await servicio.AnularAsync(request);

        // La DGII (doble) acepta: la respuesta es exitosa y con TrackId.
        Assert.True(respuesta.Exitoso, respuesta.Mensaje);
        Assert.NotNull(respuesta.TrackId);
        Assert.Contains("aceptada", respuesta.Mensaje);

        // La transmisión ocurrió UNA vez, con el XML y el nombre de archivo oficiales.
        Assert.Equal(1, cliente.AnulacionesEnviadas);
        Assert.Equal("13100000001E320000000099.xml", cliente.UltimoNombreArchivoAnulacion);
        Assert.Contains("<ANECF>", cliente.UltimoXmlAnulacion);
        Assert.Contains("<SecuenciaeNCFDesde>E320000000099</SecuenciaeNCFDesde>", cliente.UltimoXmlAnulacion);

        await using var verificacion = CrearContexto(conexion);

        // El registro de anulación queda auditable con el resultado de la DGII.
        var anulacion = await verificacion.Anulaciones.AsNoTracking().SingleAsync();
        Assert.Equal(request.RNCEmisor, anulacion.RNCEmisor);
        Assert.Equal(request.eNCFDesde, anulacion.eNCFDesde);
        Assert.Equal(request.eNCFHasta, anulacion.eNCFHasta);
        Assert.Equal(request.CantidadSecuencias, anulacion.CantidadSecuencias);
        Assert.True(anulacion.Aprobada);
        Assert.Equal(respuesta.TrackId, anulacion.TrackId);
        Assert.NotNull(anulacion.FechaRespuesta);
        Assert.Contains("ANECF", anulacion.XMLContent);

        // El comprobante cuyo e-NCF cae dentro del rango anulado queda Anulado, una sola vez.
        var comprobante = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == factura.Id);
        Assert.Equal(EstadoFacturaElectronica.Anulado, comprobante.Estado);
        Assert.Equal(request.Motivo, comprobante.MotivoAnulacion);
        Assert.NotNull(comprobante.FechaAnulacion);
    }

    [Fact]
    public async Task FlujoCompleto_DgiiRechaza_ElComprobanteConservaSuEstadoVigente()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();

        var factura = new ElectronicInvoice
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "E320000000200",
            Version = "1.0",
            RNCEmisor = "13100000001",
            RazonSocialEmisor = "POS Palasy SRL",
            FechaEmision = new FechaDominicana(new DateOnly(2026, 9, 22)).ToXmlString(),
            MontoTotal = 118.00m,
            XMLContent = "<ECF/>",
            XMLHash = "A1B2C3",
            Estado = EstadoFacturaElectronica.Aceptado,
            EstadoEmision = EstadoEmisionECF.ConfirmadaEnvio
        };
        ctx.ElectronicInvoices.Add(factura);
        await ctx.SaveChangesAsync();

        var cliente = new ClienteDgiiFalso
        {
            RespuestaAnulacion = new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 409,
                Mensaje = "Las secuencias ya fueron utilizadas"
            }
        };
        var servicio = CrearServicio(ctx, cliente);

        var respuesta = await servicio.AnularAsync(SolicitudCanonica());

        // El veredicto de la DGII es la palabra final: la respuesta NO es exitosa.
        Assert.False(respuesta.Exitoso);
        Assert.Contains("rechazada", respuesta.Mensaje);

        await using var verificacion = CrearContexto(conexion);

        // El intento queda auditable (rechazado), con su motivo oficial.
        var anulacion = await verificacion.Anulaciones.AsNoTracking().SingleAsync();
        Assert.False(anulacion.Aprobada);
        Assert.Contains("utilizadas", anulacion.MensajeRespuesta);
        Assert.Null(anulacion.FechaRespuesta);

        // El comprobante NO se anula: conserva su estado fiscal vigente.
        var comprobante = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == factura.Id);
        Assert.Equal(EstadoFacturaElectronica.Aceptado, comprobante.Estado);
        Assert.Null(comprobante.MotivoAnulacion);
        Assert.Null(comprobante.FechaAnulacion);
    }

    [Fact]
    public async Task FlujoCompleto_ComprobanteYaAnulado_NoSeReAnula()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();

        var fechaOriginal = DateTime.UtcNow.AddDays(-3);
        var factura = new ElectronicInvoice
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "E320000000300",
            Version = "1.0",
            RNCEmisor = "13100000001",
            RazonSocialEmisor = "POS Palasy SRL",
            FechaEmision = new FechaDominicana(new DateOnly(2026, 9, 22)).ToXmlString(),
            MontoTotal = 118.00m,
            XMLContent = "<ECF/>",
            XMLHash = "A1B2C3",
            Estado = EstadoFacturaElectronica.Anulado,
            MotivoAnulacion = "Anulación previa",
            FechaAnulacion = fechaOriginal,
            EstadoEmision = EstadoEmisionECF.ConfirmadaEnvio
        };
        ctx.ElectronicInvoices.Add(factura);
        await ctx.SaveChangesAsync();

        var cliente = new ClienteDgiiFalso();
        var servicio = CrearServicio(ctx, cliente);

        var respuesta = await servicio.AnularAsync(new AnulacionRequest
        {
            RNCEmisor = "13100000001",
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCFDesde = "E320000000300",
            eNCFHasta = "E320000000300",
            CantidadSecuencias = 1,
            CodigoMotivoAnulacion = 1,
            Motivo = "Segunda solicitud para la misma secuencia"
        });

        Assert.True(respuesta.Exitoso, respuesta.Mensaje);

        await using var verificacion = CrearContexto(conexion);
        var comprobante = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == factura.Id);

        // La re-anulación no sobrescribe la trazabilidad de la primera anulación.
        Assert.Equal(EstadoFacturaElectronica.Anulado, comprobante.Estado);
        Assert.Equal("Anulación previa", comprobante.MotivoAnulacion);
        Assert.Equal(fechaOriginal, comprobante.FechaAnulacion);
    }

    [Fact]
    public async Task SolicitudInvalida_NoTransmiteYNoRegistra()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();

        var cliente = new ClienteDgiiFalso();
        var servicio = CrearServicio(ctx, cliente);

        var respuesta = await servicio.AnularAsync(new AnulacionRequest
        {
            RNCEmisor = "",                          // obligatorio
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCFDesde = "E32-CORTO",                 // viola el patrón de 13 caracteres
            eNCFHasta = "E320000000101",
            CantidadSecuencias = 0,                  // debe ser > 0
            CodigoMotivoAnulacion = 1,
            Motivo = ""
        });

        Assert.False(respuesta.Exitoso);
        Assert.Equal(0, cliente.AnulacionesEnviadas);    // nada llegó a la DGII

        await using var verificacion = CrearContexto(conexion);
        Assert.Empty(await verificacion.Anulaciones.AsNoTracking().ToListAsync());
    }
}
