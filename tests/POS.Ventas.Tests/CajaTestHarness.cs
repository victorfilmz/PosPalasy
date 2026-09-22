using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using POS.Application.CasosDeUso.Caja;
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
using Xunit;

namespace POS.Ventas.Tests;

/// <summary>
/// Arnés de pruebas del módulo de caja (Fase 4): base de datos SQLite real (archivo temporal),
/// repositorios reales, caso de uso de venta real y cliente DGII falso. Se usa la base real porque
/// lo que se verifica es la atomicidad de la devolución, el tope de reembolso entre devoluciones
/// concurrentes y el arqueo del turno: nada de eso se puede comprobar con dobles en memoria.
/// </summary>
internal sealed class CajaTestHarness : IAsyncDisposable
{
    private readonly string _rutaArchivo;
    private readonly List<POSDbContext> _contextosCreados = new();

    public ClienteDgiiFalso ClienteDgii { get; } = new();

    public int EmpresaId { get; private set; }
    public int SucursalId { get; private set; }
    public int ProductoId { get; private set; }
    public int TurnoId { get; private set; }

    public const int UsuarioId = 77;
    public const string UsuarioNombre = "supervisor.pruebas";

    private CajaTestHarness(string rutaArchivo)
    {
        _rutaArchivo = rutaArchivo;
    }

    /// <summary>Crea el arnés: empresa (política estricta), producto con stock, turno abierto del supervisor.</summary>
    public static async Task<CajaTestHarness> CrearAsync()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"pospalasy-caja-{Guid.NewGuid():N}.db");
        var harness = new CajaTestHarness(ruta);

        await using var ctx = harness.CrearContexto();
        await ctx.Database.EnsureCreatedAsync();

        var empresa = new Enterprise
        {
            RNC = "13100000002",
            RazonSocial = "PosPalasy Caja Pruebas SRL",
            NombreComercial = "PosPalasy Caja Pruebas",
            Direccion = "Av. Caja #2",
            Telefono = "809-000-0000",
            Email = "caja@pospalasy.test",
            CodigoProvincia = "01",
            CodigoMunicipio = "010100",
            PoliticaStock = PoliticaStock.Bloquear,
            EstaActiva = true
        };
        empresa.Sucursales.Add(new Sucursal
        {
            CodigoSucursal = "001",
            Nombre = "Sucursal Caja",
            Direccion = "Av. Caja #2",
            CodigoProvincia = "01",
            CodigoMunicipio = "010100"
        });

        await ctx.Enterprises.AddAsync(empresa);
        await ctx.SaveChangesAsync();

        harness.EmpresaId = empresa.Id;
        harness.SucursalId = empresa.Sucursales.First().Id;

        var producto = new Producto
        {
            Codigo = "C-1",
            Descripcion = "Producto de Caja",
            PrecioUnitario = 50m,
            CostoUnitario = 30m,
            IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
            UnidadMedida = UnidadMedidaType.Unidad,
            EstaActivo = true
        };
        await ctx.Productos.AddAsync(producto);
        await ctx.SaveChangesAsync();
        harness.ProductoId = producto.Id;

        await ctx.InventariosAlmacen.AddAsync(new InventarioAlmacen
        {
            ProductoId = producto.Id,
            SucursalId = harness.SucursalId,
            StockActual = 100m,
            StockMinimo = 1m
        });
        await ctx.SaveChangesAsync();

        var turno = new CajaTurno
        {
            SucursalId = harness.SucursalId,
            Cajero = "Supervisor de Pruebas",
            UsuarioId = UsuarioId,
            UsuarioNombre = UsuarioNombre,
            MontoInicial = 5000m,
            Estado = TurnoCajaEstado.Abierto,
            FechaApertura = DateTime.UtcNow
        };
        await ctx.CajaTurnos.AddAsync(turno);
        await ctx.SaveChangesAsync();
        harness.TurnoId = turno.Id;

        return harness;
    }

    public POSDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<POSDbContext>()
            .UseSqlite($"Data Source={_rutaArchivo}")
            .Options;

        var contexto = new POSDbContext(opciones);
        _contextosCreados.Add(contexto);
        return contexto;
    }

    /// <summary>Registra una venta real por el caso de uso (todo en verde) y devuelve su id.</summary>
    public async Task<Venta> SembrarVentaAsync(
        decimal cantidad = 2m,
        MetodoPago metodoPago = MetodoPago.Efectivo,
        decimal? montoRecibido = null,
        List<PagoVentaCommand>? pagos = null)
    {
        await using var ctx = CrearContexto();
        var handler = CrearProcesarVentaHandler(ctx);

        var resultado = await handler.EjecutarAsync(new ProcesarVentaCommand
        {
            ClaveIdempotencia = Guid.NewGuid(),
            TipoeCF = TipoeCFType.FacturaConsumo,
            TipoPago = TipoPago.Contado,
            MetodoPago = metodoPago,
            MontoRecibido = montoRecibido,
            Pagos = pagos ?? new List<PagoVentaCommand>(),
            UsuarioId = UsuarioId,
            UsuarioNombre = UsuarioNombre,
            Items = new List<ItemVentaCommand>
            {
                new() { ProductoId = ProductoId, Cantidad = cantidad }
            }
        });

        Assert.True(resultado.Exitoso, resultado.Mensaje);

        await using var ctx2 = CrearContexto();
        var venta = await ctx2.Ventas.AsNoTracking()
            .Include(v => v.ElectronicInvoice)
            .Include(v => v.Items)
            .Include(v => v.Pagos)
            .FirstAsync(v => v.Id == resultado.VentaId);

        return venta;
    }

    public RegistrarDevolucionHandler CrearDevolucionHandler(POSDbContext contexto)
    {
        var comun = new CommonRepositories(contexto);

        return new RegistrarDevolucionHandler(
            new VentaRepository(contexto),
            new DevolucionRepository(contexto),
            new InventarioAlmacenRepository(contexto),
            comun,
            new CajaTurnoRepository(contexto),
            new UnidadDeTrabajo(contexto));
    }

    private static ProcesarVentaHandler CrearProcesarVentaHandler(POSDbContext contexto)
    {
        var comun = new CommonRepositories(contexto);

        return new ProcesarVentaHandler(
            new VentaRepository(contexto),
            new InvoiceRepository(contexto),
            comun,
            comun,
            comun,
            new SecuenciaECFRepository(contexto, new SecuenciaLibreRepository(contexto)),
            new InventarioAlmacenRepository(contexto),
            comun,
            new CajaTurnoRepository(contexto),
            new EmisionDGIIQueueRepository(contexto),
            new DgiiElectronicInvoiceService(
                new XmlSerializer(),
                new XmlValidator(),
                new HashGenerator(),
                new ClienteDgiiFalso(),
                new InvoiceRepository(contexto),
                new CommonRepositories(contexto),
                new EmisionDGIIQueueRepository(contexto),
                NullLogger<DgiiElectronicInvoiceService>.Instance,
                dgiiConfig: new DgiiConfig { ModoSimulador = true }),
            new TaxCalculator(),
            new UnidadDeTrabajo(contexto));
    }

    // ------------------------------------------------------------------ lecturas de verificación

    public async Task<decimal> StockAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.InventariosAlmacen.AsNoTracking()
            .Where(i => i.ProductoId == ProductoId && i.SucursalId == SucursalId)
            .Select(i => i.StockActual)
            .SingleAsync();
    }

    public async Task<int> ContarDevolucionesAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.Devoluciones.AsNoTracking().CountAsync();
    }

    public async Task<int> ContarMovimientosCajaAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.MovimientosCaja.AsNoTracking().CountAsync();
    }

    public async Task<int> ContarMovimientosInventarioAsync(TipoMovimientoInventario tipo)
    {
        await using var ctx = CrearContexto();
        return await ctx.MovimientosInventario.AsNoTracking()
            .CountAsync(m => m.Tipo == tipo);
    }

    public async Task<CajaTurno> TurnoAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.CajaTurnos.AsNoTracking().FirstAsync(t => t.Id == TurnoId);
    }

    public async Task<MovimientoCaja> MovimientoDevolucionAsync()
    {
        await using var ctx = CrearContexto();
        return await ctx.MovimientosCaja.AsNoTracking()
            .Include(m => m.Venta)
            .SingleAsync(m => m.VentaId != null);
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
            // Archivo temporal bloqueado: no afecta el resultado.
        }
    }
}
