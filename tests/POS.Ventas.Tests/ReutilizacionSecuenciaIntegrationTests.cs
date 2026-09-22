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
/// Pruebas de integración de la reutilización de secuencias tras rechazo (FASE 5, sub-fase 5.5):
/// un rechazo con <c>secuenciaUtilizada=false</c> devuelve el número al pool, la siguiente
/// asignación lo consume ANTES de avanzar el contador y no se generan duplicados.
/// </summary>
public class ReutilizacionSecuenciaIntegrationTests
{
    private static POSDbContext CrearContexto(SqliteConnection conexion) =>
        new(new DbContextOptionsBuilder<POSDbContext>().UseSqlite(conexion).Options);

    private static string RutaPfxTemporal(string password)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=PosPalasy Emisor Secuencias", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var ruta = Path.Combine(Path.GetTempPath(), $"pospalasy-secuencias-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pkcs12, password));
        return ruta;
    }

    /// <summary>Comprobante canónico de consumo (RFCE, &lt; 250k) en el estado que deja la venta.</summary>
    private static ElectronicInvoice CrearFactura(string eNCF = "E320000000042") => new()
    {
        TipoeCF = TipoeCFType.FacturaConsumo,
        eNCF = eNCF,
        Version = "1.0",
        RNCEmisor = "13100000001",
        RazonSocialEmisor = "POS Palasy SRL",
        FechaEmision = new FechaDominicana(new DateOnly(2026, 9, 19)).ToXmlString(),
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

    private static (int FacturaId, int ColaId) Sembrar(POSDbContext ctx, ElectronicInvoice factura)
    {
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
        var password = "clave-secuencias";
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
            firmador: new FirmadorComprobanteECF(new XmlDigitalSigner()),
            secuenciasLibresRepository: new SecuenciaLibreRepository(ctx),
            auditoriaRepository: new AuditoriaRepository(ctx));
    }

    [Fact]
    public async Task RechazoConSecuenciaNoUtilizada_LaSiguienteAsignacionReutilizaElNumero()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = Sembrar(ctx, CrearFactura());

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
        await servicio.EnviarAsync(facturaId);

        // La numeración siguiente DEBE ser la liberada (el pool se consume antes que el contador):
        // sin el pool, el índice de último emitido daría 43 y el 42 quedaría quemado.
        var secuencias = new SecuenciaECFRepository(ctx, new SecuenciaLibreRepository(ctx));
        var reutilizada = await secuencias.AsignarSiguienteENCFAsync(TipoeCFType.FacturaConsumo);

        Assert.Equal("E320000000042", reutilizada);

        // La fila del pool queda marcada como consumida, no borrada: historial auditable.
        var libres = await ctx.SecuenciasLibres.AsNoTracking().ToListAsync();
        var fila = Assert.Single(libres);
        Assert.True(fila.Consumida);
        Assert.NotNull(fila.FechaConsumoUtc);
        Assert.Equal("E320000000042", fila.ENCF);
        Assert.Contains("Error en la firma", fila.MotivoRechazo);

        // La liberación dejó su traza de auditoría.
        var auditorias = await ctx.AuditoriaCambios.AsNoTracking().ToListAsync();
        Assert.Contains(auditorias, a => a.Entidad == "ElectronicInvoice" && a.ValorAnterior == "E320000000042");
    }

    [Fact]
    public async Task RechazoConSecuenciaUtilizada_True_NuncaLiberaLaSecuencia()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = Sembrar(ctx, CrearFactura());

        var cliente = new ClienteDgiiFalso
        {
            ProximaRespuesta = new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                Estado = "Rechazado",
                SecuenciaUtilizada = true,      // la DGII consumió el número
                Mensaje = "Duplicado en la DGII"
            }
        };
        var servicio = CrearServicio(ctx, cliente);
        await servicio.EnviarAsync(facturaId);

        await using var verificacion = CrearContexto(conexion);
        Assert.Empty(await verificacion.SecuenciasLibres.AsNoTracking().ToListAsync());
        Assert.Empty(await verificacion.AuditoriaCambios.AsNoTracking()
            .Where(a => a.Campo == "SecuenciaUtilizada").ToListAsync());
    }

    [Fact]
    public async Task RechazoEnRecepcionSinMarca_SeInterpretaComoSecuenciaNoUtilizada()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = Sembrar(ctx, CrearFactura());

        // 422: rechazo de validación en la recepción (respuesta sin cuerpo fiscal ⇒ sin marca).
        var cliente = new ClienteDgiiFalso
        {
            ProximaRespuesta = new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 422,
                Mensaje = "XML no conforme al esquema"
            }
        };
        var servicio = CrearServicio(ctx, cliente);
        var respuesta = await servicio.EnviarAsync(facturaId);

        Assert.Equal(EstadoFacturaElectronica.Rechazado, respuesta.Estado);

        await using var verificacion = CrearContexto(conexion);
        var fila = Assert.Single(await verificacion.SecuenciasLibres.AsNoTracking().ToListAsync());
        Assert.Equal("E320000000042", fila.ENCF);
        Assert.False(fila.Consumida);
    }

    [Fact]
    public async Task ComprobanteAceptado_NuncaLiberaSecuenciaAunqueLlegueFalse()
    {
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = Sembrar(ctx, CrearFactura());

        var cliente = new ClienteDgiiFalso
        {
            ProximaRespuesta = new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                Estado = "Aceptado",
                SecuenciaUtilizada = true
            }
        };
        var servicio = CrearServicio(ctx, cliente);
        await servicio.EnviarAsync(facturaId);

        await using var verificacion = CrearContexto(conexion);
        Assert.Empty(await verificacion.SecuenciasLibres.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReutilizacionEnCadena_NingunComprobanteVigenteComparteNumero()
    {
        // Barrera física: el índice único es FILTRADO (excluye rechazados), de modo que la
        // reutilización declarada por la DGII no choca con la trazabilidad del rechazado, pero dos
        // comprobantes VIGENTES con el mismo número siguen siendo imposibles.
        await using var conexion = new SqliteConnection("DataSource=:memory:");
        conexion.Open();
        await using var ctx = CrearContexto(conexion);
        await ctx.Database.EnsureCreatedAsync();
        var (facturaId, _) = Sembrar(ctx, CrearFactura());

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
        await CrearServicio(ctx, cliente).EnviarAsync(facturaId);

        // La reutilización del número en un comprobante NUEVO es posible (no revienta el índice).
        var reutilizada = new ElectronicInvoice
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "E320000000042",
            RNCEmisor = "13100000001",
            RazonSocialEmisor = "POS Palasy SRL",
            FechaEmision = new FechaDominicana(new DateOnly(2026, 9, 21)).ToXmlString(),
            MontoTotal = 118.00m,
            XMLContent = "<eCF/>",
            XMLHash = "C5D8E0",
            Estado = EstadoFacturaElectronica.NoEnviado,
            EstadoEmision = EstadoEmisionECF.Creada
        };
        await ctx.ElectronicInvoices.AddAsync(reutilizada);
        await ctx.SaveChangesAsync();                       // índice filtrado: el rechazado no bloquea

        // Dos VIGENTES con el mismo número siguen siendo imposibles.
        var duplicado = new ElectronicInvoice
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "E320000000042",
            RNCEmisor = "13100000001",
            RazonSocialEmisor = "POS Palasy SRL",
            FechaEmision = new FechaDominicana(new DateOnly(2026, 9, 21)).ToXmlString(),
            MontoTotal = 200.00m,
            XMLContent = "<eCF/>",
            XMLHash = "D6E9F1",
            Estado = EstadoFacturaElectronica.NoEnviado,
            EstadoEmision = EstadoEmisionECF.Creada
        };
        await ctx.ElectronicInvoices.AddAsync(duplicado);

        await Assert.ThrowsAnyAsync<DbUpdateException>(async () => await ctx.SaveChangesAsync());
    }
}
