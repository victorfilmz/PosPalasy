using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using POS.Application.CasosDeUso.Ventas;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;
using POS.Infrastructure.DGII;
using POS.Infrastructure.Persistence;
using POS.Infrastructure.Persistence.Repositories;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;

namespace POS.Ventas.Tests;

/// <summary>
/// Arnés de pruebas de la venta: base de datos SQLite real (archivo temporal), repositorios reales,
/// servicio de comprobantes real y un cliente DGII falso para no depender de la red.
/// </summary>
/// <remarks>
/// Se usa la base de datos real y no dobles de repositorio porque lo que se verifica aquí es
/// precisamente la integridad transaccional, la atomicidad del descuento de existencias y la
/// unicidad de la numeración fiscal: nada de eso se puede comprobar con dobles en memoria.
/// </remarks>
internal sealed class VentasTestHarness : IAsyncDisposable
{
    private readonly string _rutaArchivo;
    private readonly List<POSDbContext> _contextosCreados = new();

    public ClienteDgiiFalso ClienteDgii { get; } = new();

    public int EnterpriseId { get; private set; }
    public int SucursalId { get; private set; }
    public int ProductoGravado18Id { get; private set; }
    public int ProductoExentoId { get; private set; }
    public int TurnoId { get; private set; }

    public const int UsuarioId = 42;
    public const string UsuarioNombre = "cajero.pruebas";

    private VentasTestHarness(string rutaArchivo)
    {
        _rutaArchivo = rutaArchivo;
    }

    /// <summary>Crea el arnés con empresa, sucursal, productos, existencias y turno abierto.</summary>
    public static async Task<VentasTestHarness> CrearAsync(
        decimal stockGravado = 10m,
        decimal stockExento = 10m,
        bool permitirVentaSinStock = false,
        bool conTurnoAbierto = true)
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"pospalasy-ventas-{Guid.NewGuid():N}.db");
        var harness = new VentasTestHarness(ruta);

        await using var ctx = harness.CrearContexto();
        await ctx.Database.EnsureCreatedAsync();

        var empresa = new Enterprise
        {
            RNC = "13100000001",
            RazonSocial = "PosPalasy Pruebas SRL",
            NombreComercial = "PosPalasy Pruebas",
            Direccion = "Av. Pruebas #1",
            Telefono = "809-000-0000",
            Email = "pruebas@pospalasy.test",
            CodigoProvincia = "01",
            CodigoMunicipio = "010100",
            PoliticaStock = permitirVentaSinStock ? PoliticaStock.Permitir : PoliticaStock.Bloquear,
            EstaActiva = true
        };
        empresa.Sucursales.Add(new Sucursal
        {
            CodigoSucursal = "001",
            Nombre = "Sucursal de Pruebas",
            Direccion = "Av. Pruebas #1",
            CodigoProvincia = "01",
            CodigoMunicipio = "010100"
        });

        await ctx.Enterprises.AddAsync(empresa);
        await ctx.SaveChangesAsync();

        harness.EnterpriseId = empresa.Id;
        harness.SucursalId = empresa.Sucursales.First().Id;

        var gravado = new Producto
        {
            Codigo = "P-18",
            Descripcion = "Producto Gravado 18%",
            PrecioUnitario = 100m,
            CostoUnitario = 60m,
            IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
            UnidadMedida = UnidadMedidaType.Unidad,
            EstaActivo = true
        };
        var exento = new Producto
        {
            Codigo = "P-EX",
            Descripcion = "Producto Exento",
            PrecioUnitario = 250m,
            CostoUnitario = 150m,
            IndicadorFacturacion = IndicadorFacturacionType.Exento,
            UnidadMedida = UnidadMedidaType.Unidad,
            EstaActivo = true
        };

        await ctx.Productos.AddRangeAsync(gravado, exento);
        await ctx.SaveChangesAsync();

        harness.ProductoGravado18Id = gravado.Id;
        harness.ProductoExentoId = exento.Id;

        await ctx.InventariosAlmacen.AddRangeAsync(
            new InventarioAlmacen
            {
                ProductoId = gravado.Id,
                SucursalId = harness.SucursalId,
                StockActual = stockGravado,
                StockMinimo = 1m
            },
            new InventarioAlmacen
            {
                ProductoId = exento.Id,
                SucursalId = harness.SucursalId,
                StockActual = stockExento,
                StockMinimo = 1m
            });
        await ctx.SaveChangesAsync();

        if (conTurnoAbierto)
        {
            var turno = new CajaTurno
            {
                SucursalId = harness.SucursalId,
                Cajero = "Cajero de Pruebas",
                UsuarioId = UsuarioId,
                UsuarioNombre = UsuarioNombre,
                MontoInicial = 1000m,
                Estado = TurnoCajaEstado.Abierto,
                FechaApertura = DateTime.UtcNow
            };

            await ctx.CajaTurnos.AddAsync(turno);
            await ctx.SaveChangesAsync();
            harness.TurnoId = turno.Id;
        }

        return harness;
    }

    /// <summary>Nuevo contexto sobre la misma base de datos (cada venta concurrente usa el suyo).</summary>
    public POSDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<POSDbContext>()
            .UseSqlite($"Data Source={_rutaArchivo}")
            .Options;

        var contexto = new POSDbContext(opciones);
        _contextosCreados.Add(contexto);
        return contexto;
    }

    /// <summary>Manejador de venta con repositorios y servicio de comprobantes reales.</summary>
    public ProcesarVentaHandler CrearHandler(POSDbContext contexto) =>
        CrearHandler(contexto, CrearServicioFacturacion(contexto));

    /// <summary>Manejador de venta cuyo registro de comprobante falla siempre (para probar el revertimiento).</summary>
    public ProcesarVentaHandler CrearHandlerQueFallaAlRegistrarComprobante(POSDbContext contexto) =>
        CrearHandler(contexto, new FacturacionConFalloControlado(CrearServicioFacturacion(contexto)));

    private static ProcesarVentaHandler CrearHandler(POSDbContext contexto, IElectronicInvoiceService facturacion)
    {
        var comun = new CommonRepositories(contexto);

        return new ProcesarVentaHandler(
            new VentaRepository(contexto),
            new InvoiceRepository(contexto),
            comun,
            comun,
            comun,
            new SecuenciaECFRepository(contexto),
            new InventarioAlmacenRepository(contexto),
            comun,
            new CajaTurnoRepository(contexto),
            new EmisionDGIIQueueRepository(contexto),
            facturacion,
            new TaxCalculator(),
            new UnidadDeTrabajo(contexto));
    }

    private DgiiElectronicInvoiceService CrearServicioFacturacion(POSDbContext contexto) =>
        new(
            new XmlSerializer(),
            new XmlValidator(),
            new HashGenerator(),
            ClienteDgii,
            new InvoiceRepository(contexto),
            new CommonRepositories(contexto),
            new EmisionDGIIQueueRepository(contexto),
            NullLogger<DgiiElectronicInvoiceService>.Instance,
            dgiiConfig: new DgiiConfig { ModoSimulador = true });

    /// <summary>Comando de venta con la clave de idempotencia indicada.</summary>
    public ProcesarVentaCommand CrearComando(
        Guid clave,
        int productoId,
        decimal cantidad = 1m,
        decimal descuento = 0m,
        TipoeCFType tipoeCF = TipoeCFType.FacturaConsumo,
        string? rncComprador = null,
        MetodoPago metodoPago = MetodoPago.Efectivo,
        decimal? montoRecibido = null)
    {
        return new ProcesarVentaCommand
        {
            ClaveIdempotencia = clave,
            TipoeCF = tipoeCF,
            TipoPago = TipoPago.Contado,
            MetodoPago = metodoPago,
            MontoRecibido = montoRecibido,
            RNCComprador = rncComprador,
            UsuarioId = UsuarioId,
            UsuarioNombre = UsuarioNombre,
            Items = new List<ItemVentaCommand>
            {
                new() { ProductoId = productoId, Cantidad = cantidad, Descuento = descuento }
            }
        };
    }

    public async Task<decimal> StockAsync(int productoId)
    {
        await using var ctx = CrearContexto();
        var inventario = await ctx.InventariosAlmacen
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductoId == productoId && i.SucursalId == SucursalId);

        return inventario?.StockActual ?? 0m;
    }

    public async Task<int> ContarVentasAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.Ventas.AsNoTracking().CountAsync();
    }

    public async Task<int> ContarMovimientosAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.MovimientosInventario.AsNoTracking().CountAsync();
    }

    public async Task<int> ContarColaAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.EmisionesDGIIQueue.AsNoTracking().CountAsync();
    }

    public async Task<CajaTurno> TurnoAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.CajaTurnos.AsNoTracking().FirstAsync(c => c.Id == TurnoId);
    }

    public async Task<ElectronicInvoice?> ComprobanteAsync(string eNCF)
    {
        await using var ctx = CrearContexto();
        return await ctx.ElectronicInvoices.AsNoTracking().FirstOrDefaultAsync(e => e.eNCF == eNCF);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var contexto in _contextosCreados)
        {
            await contexto.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_rutaArchivo))
                File.Delete(_rutaArchivo);
        }
        catch (IOException)
        {
            // Archivo temporal bloqueado por el sistema: no afecta el resultado de la prueba.
        }
    }
}

/// <summary>Cliente DGII falso: respuestas configurables sin salir a la red.</summary>
internal sealed class ClienteDgiiFalso : IDgiiApiClient
{
    public int EnviosRealizados { get; private set; }

    public string? UltimoNombreArchivo { get; private set; }
    public bool? UltimaEsFacturaConsumo { get; private set; }
    public decimal? UltimoMontoTotal { get; private set; }
    public string? UltimoXmlEnviado { get; private set; }

    /// <summary>Respuesta de la recepción (envío del comprobante).</summary>
    public DgiiApiResponse ProximaRespuesta { get; set; } = new()
    {
        EsExitoso = true,
        CodigoHttp = 200,
        TrackId = "TRACK-TEST-0001",
        Estado = "EnProceso",
        Mensaje = "Comprobante recibido (prueba)"
    };

    /// <summary>Respuesta de la consulta de resultado e-CF (por TrackId).</summary>
    public DgiiApiResponse RespuestaConsultaEstado { get; set; } = new()
    {
        EsExitoso = true,
        CodigoHttp = 200,
        TrackId = "TRACK-TEST-0001",
        Estado = "Aceptado"
    };

    /// <summary>Respuesta de la consulta de resumen RFCE (por RNC/eNCF/código).</summary>
    public DgiiApiResponse RespuestaConsultaRFCE { get; set; } = new()
    {
        EsExitoso = true,
        CodigoHttp = 200,
        Estado = "Aceptado"
    };

    public Task<DgiiApiResponse> EnviarFacturaAsync(
        string xml, string nombreArchivo, bool esFacturaConsumo, decimal montoTotal, CancellationToken ct = default)
    {
        EnviosRealizados++;
        UltimoXmlEnviado = xml;
        UltimoNombreArchivo = nombreArchivo;
        UltimaEsFacturaConsumo = esFacturaConsumo;
        UltimoMontoTotal = montoTotal;
        return Task.FromResult(ProximaRespuesta);
    }

    public Task<DgiiApiResponse> ConsultarEstadoAsync(string trackId, CancellationToken ct = default) =>
        Task.FromResult(RespuestaConsultaEstado);

    public Task<DgiiApiResponse> EnviarAprobacionComercialAsync(string xml, string nombreArchivo, CancellationToken ct = default) =>
        Task.FromResult(ProximaRespuesta);

    public Task<DgiiApiResponse> EnviarAnulacionAsync(string xml, string nombreArchivo, CancellationToken ct = default) =>
        Task.FromResult(ProximaRespuesta);

    public Task<DgiiApiResponse> ConsultarRFCEAsync(string rncEmisor, string encf, string codigoSeguridad, CancellationToken ct = default) =>
        Task.FromResult(RespuestaConsultaRFCE);
}

/// <summary>
/// Decorador que provoca un fallo controlado al registrar el comprobante: comprueba que un error a
/// mitad de la venta revierte todo lo escrito antes (venta, existencias, kardex, caja y cola).
/// </summary>
internal sealed class FacturacionConFalloControlado : IElectronicInvoiceService
{
    private readonly IElectronicInvoiceService _real;

    public FacturacionConFalloControlado(IElectronicInvoiceService real)
    {
        _real = real;
    }

    public Task<ComprobantePreparado> PrepararYRegistrarAsync(
        PrepararComprobanteCommand command,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Fallo simulado al registrar el comprobante.");

    public Task<ElectronicInvoiceResponse> EnviarAsync(int electronicInvoiceId, CancellationToken cancellationToken = default) =>
        _real.EnviarAsync(electronicInvoiceId, cancellationToken);

    public Task<ElectronicInvoiceResponse> EmitirAsync(EmitirFacturaCommand command, CancellationToken cancellationToken = default) =>
        _real.EmitirAsync(command, cancellationToken);

    public Task<AnulacionResponse> AnularAsync(AnulacionRequest request, CancellationToken cancellationToken = default) =>
        _real.AnularAsync(request, cancellationToken);

    public Task<EstadoFacturaResponse> ConsultarEstadoAsync(string eNCF, CancellationToken cancellationToken = default) =>
        _real.ConsultarEstadoAsync(eNCF, cancellationToken);

    public Task<EstadoFacturaResponse> ConsultarEstadoPorTrackIdAsync(string trackId, CancellationToken cancellationToken = default) =>
        _real.ConsultarEstadoPorTrackIdAsync(trackId, cancellationToken);

    public Task<string> GenerarXmlAsync(ElectronicInvoiceRequest request) => _real.GenerarXmlAsync(request);

    public Task<POS.Application.Validators.ValidationResult> ValidarXmlAsync(string xmlContent, TipoeCFType tipo = TipoeCFType.FacturaConsumo) =>
        _real.ValidarXmlAsync(xmlContent, tipo);

    public string GenerarHash(string xmlContent) => _real.GenerarHash(xmlContent);

    public Task<ElectronicInvoiceResponse> ReenviarAsync(string eNCF, CancellationToken cancellationToken = default) =>
        _real.ReenviarAsync(eNCF, cancellationToken);
}
