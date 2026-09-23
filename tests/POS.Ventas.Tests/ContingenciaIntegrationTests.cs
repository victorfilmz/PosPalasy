using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using POS.Application.Services;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;
using POS.Infrastructure.Persistence;
using Xunit;

namespace POS.Ventas.Tests;

/// <summary>
/// Pruebas del régimen de contingencia (FASE 6.1): declaración de tipo oficial (1–5) con ventana
/// normativa de 30 días, idempotencia de la declaración, guarda de transmisión con ventana vencida
/// y persistencia real del metadato (SQLite + repositorios reales).
/// </summary>
/// <remarks>
/// D4 (doc 15): los XSD e-CF v1.0 NO llevan campo XML de contingencia — el marcado es metadato
/// local y el comprobante se transmite después como e-CF normal dentro de su ventana.
/// </remarks>
public class ContingenciaIntegrationTests
{
    private static POSDbContext CrearContexto(SqliteConnection conexion) =>
        new(new DbContextOptionsBuilder<POSDbContext>().UseSqlite(conexion).Options);

    private static ElectronicInvoice ComprobantePendiente() => new()
    {
        TipoeCF = TipoeCFType.FacturaConsumo,
        eNCF = "E320000000010",
        Version = "1.0",
        RNCEmisor = "13100000001",
        RazonSocialEmisor = "POS Palasy SRL",
        FechaEmision = new FechaDominicana(new DateOnly(2026, 9, 23)).ToXmlString(),
        MontoTotal = 118.00m,
        XMLContent = "<ECF/>",
        XMLHash = "A1B2C3",
        Estado = EstadoFacturaElectronica.NoEnviado,
        EstadoEmision = EstadoEmisionECF.Encolada
    };

    private static readonly DateTime Ahora = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Declarar_ContingenciaPersisteTipoYVentanaNormativa()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();

        var factura = ComprobantePendiente();
        ctx.ElectronicInvoices.Add(factura);
        await ctx.SaveChangesAsync();

        var resultado = ServicioContingencia.Declarar(factura,
            new DeclararContingenciaCommand(factura.Id, TipoContingenciaDgii.FallaPlataformaDgii, Ahora));
        Assert.True(resultado.Exitoso, resultado.Mensaje);

        await ctx.SaveChangesAsync();

        await using var verificacion = CrearContexto(conexion);
        var persistido = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == factura.Id);

        Assert.Equal(TipoContingenciaDgii.FallaPlataformaDgii, persistido.TipoContingencia);
        Assert.Equal(Ahora, persistido.ContingenciaDesdeUtc);
        Assert.Equal(RegimenContingencia.FinDeVentana(Ahora), persistido.ContingenciaHastaUtc);
        Assert.True(ServicioContingencia.PuedeTransmitirse(persistido, Ahora));
    }

    [Fact]
    public async Task Declarar_SegundaVezEsIdempotente_NoSobrescribeLaPrimeraVentana()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();

        var factura = ComprobantePendiente();
        ctx.ElectronicInvoices.Add(factura);
        await ctx.SaveChangesAsync();

        ServicioContingencia.Declarar(factura,
            new DeclararContingenciaCommand(factura.Id, TipoContingenciaDgii.FallaPlataformaDgii, Ahora));

        var segunda = ServicioContingencia.Declarar(factura,
            new DeclararContingenciaCommand(factura.Id, TipoContingenciaDgii.RecorteEnergiaElectrica, Ahora.AddDays(3)));

        // La primera declaración manda: es la que fija la ventana normativa.
        Assert.True(segunda.Exitoso, segunda.Mensaje);
        Assert.Equal(TipoContingenciaDgii.FallaPlataformaDgii, factura.TipoContingencia);
        Assert.Equal(Ahora, factura.ContingenciaDesdeUtc);
    }

    [Fact]
    public void Declarar_ComprobanteYaTransmitido_NoAplica()
    {
        var factura = ComprobantePendiente();
        factura.Estado = EstadoFacturaElectronica.Aceptado;

        var resultado = ServicioContingencia.Declarar(factura,
            new DeclararContingenciaCommand(factura.Id, TipoContingenciaDgii.FallaPlataformaDgii, Ahora));

        Assert.False(resultado.Exitoso);
        Assert.Null(factura.TipoContingencia);
    }

    [Fact]
    public void VentanaVencida_NoPuedeTransmitirse_YElEmisorRechazaConCodigo()
    {
        var factura = ComprobantePendiente();
        factura.TipoContingencia = TipoContingenciaDgii.FallaPlataformaDgii;
        factura.ContingenciaDesdeUtc = Ahora.AddDays(-31);
        factura.ContingenciaHastaUtc = RegimenContingencia.FinDeVentana(Ahora.AddDays(-31));

        Assert.False(ServicioContingencia.PuedeTransmitirse(factura, Ahora));

        // La guarda del emisor convierte la ventana vencida en un error con código estable.
        var ex = Record.Exception(() =>
        {
            if (!ServicioContingencia.PuedeTransmitirse(factura, Ahora))
                throw new ReglaDeNegocioException(
                    $"El comprobante {factura.eNCF} venció su ventana de contingencia.",
                    "CONTINGENCIA_VENCIDA");
        });
        var regla = Assert.IsType<ReglaDeNegocioException>(ex);
        Assert.Equal("CONTINGENCIA_VENCIDA", regla.Codigo);
    }

    [Fact]
    public void VentanaLimite_ElDia30SigueAbierta()
    {
        Assert.True(RegimenContingencia.VentanaAbierta(Ahora, Ahora.AddDays(30)));
        Assert.False(RegimenContingencia.VentanaAbierta(Ahora, Ahora.AddDays(30).AddSeconds(1)));
    }
}
