using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
/// Pruebas de integración del resultado fiscal (FASE 5, sub-fase 5.3): la recepción RFCE consolida
/// de inmediato el veredicto de la DGII (Aceptado / Aceptado Condicional / Rechazado + mensajes +
/// secuenciaUtilizada); la recepción e-CF queda En proceso con TrackId y se consolida al consultar
/// el resultado. Toda la traza oficial queda persistida en el comprobante.
/// </summary>
public class ResultadoFiscalIntegrationTests
{
    private static POSDbContext CrearContexto(SqliteConnection conexion) =>
        new(new DbContextOptionsBuilder<POSDbContext>().UseSqlite(conexion).Options);

    private static string RutaPfxTemporal(string password)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=PosPalasy Emisor Resultado", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var ruta = Path.Combine(Path.GetTempPath(), $"pospalasy-resultado-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pkcs12, password));
        return ruta;
    }

    /// <summary>Comprobante canónico de consumo (128.00 &lt; 250k) en el estado que deja la venta.</summary>
    private static ElectronicInvoice CrearFactura() => new()
    {
        TipoeCF = TipoeCFType.FacturaConsumo,
        eNCF = "E320000000042",
        Version = "1.0",
        RNCEmisor = "13100000001",
        RazonSocialEmisor = "POS Palasy SRL",
        FechaEmision = new FechaDominicana(new DateOnly(2026, 9, 19)).ToXmlString(),
        TipoIngresos = TipoIngresosType.IngresosOperaciones,
        TipoPago = TipoPago.Contado,
        MontoGravadoTotal = 100.00m,
        MontoGravadoI1 = 100.00m,
        TotalITBIS = 18.00m,
        TotalITBIS1 = 18.00m,
        MontoTotal = 118.00m,
        XMLContent = "<eCF/>",
        XMLHash = "B4C7D9",
        Estado = EstadoFacturaElectronica.NoEnviado,
        EstadoEmision = EstadoEmisionECF.XsdValidado
    };

    private static (int FacturaId, int ColaId) Sembrar(POSDbContext ctx)
    {
        var factura = CrearFactura();
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

    private static DgiiElectronicInvoiceService CrearServicio(POSDbContext ctx, ClienteDgiiFalso cliente)
    {
        var password = "clave-resultado";
        var rutaPfx = RutaPfxTemporal(password);
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
            proveedorCertificado: new ProveedorCertificadoDigital(rutaPfx, password),
            firmador: new FirmadorComprobanteECF(new XmlDigitalSigner()));
    }

    [Fact]
    public async Task ConsumoMenor250k_RecibeElResultadoFiscalCompletoEnLaMismaLlamada()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = Sembrar(ctx);

        var cliente = new ClienteDgiiFalso
        {
            ProximaRespuesta = new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                Estado = "Aceptado Condicional",
                eNCF = "E320000000042",
                SecuenciaUtilizada = true,
                Mensaje = "1: El RNC del comprador no existe"
            }
        };
        var servicio = CrearServicio(ctx, cliente);

        var respuesta = await servicio.EnviarAsync(facturaId);

        // RFCE: resultado fiscal definitivo SIN TrackId.
        Assert.True(respuesta.Exitoso, respuesta.Mensaje);
        Assert.Equal(EstadoFacturaElectronica.Aceptado, respuesta.Estado);
        Assert.Equal(EstadoEmisionECF.ConfirmadaEnvio, respuesta.EstadoEmision);
        Assert.Null(respuesta.TrackId);

        await using var verificacion = CrearContexto(conexion);
        var factura = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == facturaId);

        Assert.Equal(EstadoFacturaElectronica.Aceptado, factura.Estado);
        Assert.Equal("Aceptado Condicional", factura.EstadoDgii);
        Assert.Contains("El RNC del comprador no existe", factura.MensajesDgii);
        Assert.True(factura.SecuenciaUtilizada);
        Assert.NotNull(factura.FechaAprobacion);
        Assert.Null(factura.TrackId);
    }

    [Fact]
    public async Task ConsumoMenor250k_RechazadoConservaMotivoYMarcaSecuenciaReutilizable()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = Sembrar(ctx);

        var cliente = new ClienteDgiiFalso
        {
            ProximaRespuesta = new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                Estado = "Rechazado",
                SecuenciaUtilizada = false,
                Mensaje = "9: Error en la firma del comprobante"
            }
        };
        var servicio = CrearServicio(ctx, cliente);

        var respuesta = await servicio.EnviarAsync(facturaId);

        // La RECEPCIÓN fue exitosa; el veredicto fiscal es Rechazado (documento con validez local,
        // sin validez fiscal, con la secuencia marcada como reutilizable por la DGII).
        Assert.True(respuesta.Exitoso, respuesta.Mensaje);
        Assert.Equal(EstadoFacturaElectronica.Rechazado, respuesta.Estado);

        await using var verificacion = CrearContexto(conexion);
        var factura = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == facturaId);

        Assert.Equal(EstadoFacturaElectronica.Rechazado, factura.Estado);
        Assert.Contains("Error en la firma del comprobante", factura.MotivoRechazo);   // texto oficial con su código
        Assert.False(factura.SecuenciaUtilizada);   // la DGII permite reutilizar la secuencia
    }

    [Fact]
    public async Task ConsumoMayorOIgual250k_QuedaEnProcesoYSeConfirmaAlConsultarResultado()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();

        // Se siembra un comprobante que alcanza el umbral e-CF (≥ 250k).
        var factura = CrearFactura();
        factura.eNCF = "E320000000043";
        factura.MontoGravadoTotal = 300_000.00m;
        factura.MontoGravadoI1 = 300_000.00m;
        factura.TotalITBIS = 54_000.00m;
        factura.TotalITBIS1 = 54_000.00m;
        factura.MontoTotal = 354_000.00m;
        ctx.ElectronicInvoices.Add(factura);
        ctx.SaveChanges();
        var facturaId = factura.Id;

        var cliente = new ClienteDgiiFalso();
        var servicio = CrearServicio(ctx, cliente);

        // 1) Recepción e-CF: TrackId con "En proceso"; el veredicto llega por consulta.
        var envio = await servicio.EnviarAsync(facturaId);

        Assert.True(envio.Exitoso, envio.Mensaje);
        Assert.Equal("TRACK-TEST-0001", envio.TrackId);
        Assert.Equal(EstadoFacturaElectronica.EnProceso, envio.Estado);
        Assert.Equal(EstadoEmisionECF.ConfirmadaEnvio, envio.EstadoEmision);

        // 2) Consulta del resultado por TrackId: consolida el veredicto oficial.
        cliente.RespuestaConsultaEstado = new DgiiApiResponse
        {
            EsExitoso = true,
            CodigoHttp = 200,
            TrackId = "TRACK-TEST-0001",
            Estado = "Aceptado Condicional",
            eNCF = factura.eNCF,
            SecuenciaUtilizada = true,
            Mensaje = "Monto mayor al declarado"
        };
        var consulta = await servicio.ConsultarEstadoPorTrackIdAsync("TRACK-TEST-0001");

        Assert.Equal(EstadoFacturaElectronica.Aceptado, consulta.Estado);

        await using var verificacion = CrearContexto(conexion);
        var persistida = await verificacion.ElectronicInvoices.AsNoTracking()
            .SingleAsync(f => f.Id == facturaId);

        Assert.Equal(EstadoFacturaElectronica.Aceptado, persistida.Estado);
        Assert.Equal("Aceptado Condicional", persistida.EstadoDgii);
        Assert.Equal("Monto mayor al declarado", persistida.MensajesDgii);
        Assert.True(persistida.SecuenciaUtilizada);
        Assert.Equal("TRACK-TEST-0001", persistida.TrackId);
    }

    [Fact]
    public async Task Transmision_UsaElNombreOficialRncMasEncf()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = Sembrar(ctx);

        var cliente = new ClienteDgiiFalso();
        var servicio = CrearServicio(ctx, cliente);

        await servicio.EnviarAsync(facturaId);

        Assert.Equal("13100000001E320000000042.xml", cliente.UltimoNombreArchivo);
        Assert.True(cliente.UltimaEsFacturaConsumo);
        Assert.Equal(118.00m, cliente.UltimoMontoTotal);
    }
}
