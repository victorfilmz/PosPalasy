using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using POS.Application.CasosDeUso.Ventas;
using POS.Application.DTOs;
using POS.Application.Services;
using POS.Domain.Entities;
using POS.Domain.Repositories;
using POS.Domain.Types;
using POS.UI.Security;

namespace POS.UI.Controllers;

/// <summary>
/// Terminal de punto de venta: catálogo, procesamiento de la venta e impresión del ticket.
/// Requiere un usuario autenticado con permiso de operación de POS.
/// </summary>
/// <remarks>
/// Este controlador es un adaptador HTTP: no calcula importes, no asigna numeración fiscal, no mueve
/// inventario ni decide estados. Esas responsabilidades viven en el caso de uso
/// <see cref="ProcesarVentaHandler"/>.
/// </remarks>
[Authorize(Policy = Politicas.OperacionPos)]
public class PosController : Controller
{
    private readonly IProductoRepository _productoRepo;
    private readonly IInventarioAlmacenRepository _inventarioRepo;
    private readonly ISucursalRepository _sucursalRepo;
    private readonly IClienteRepository _clienteRepo;
    private readonly IEnterpriseRepository _enterpriseRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly ICajaTurnoRepository _cajaRepo;
    private readonly ProcesarVentaHandler _procesarVenta;
    private readonly ILogger<PosController> _logger;

    public PosController(
        IProductoRepository productoRepo,
        IInventarioAlmacenRepository inventarioRepo,
        ISucursalRepository sucursalRepo,
        IClienteRepository clienteRepo,
        IEnterpriseRepository enterpriseRepo,
        IInvoiceRepository invoiceRepo,
        ICajaTurnoRepository cajaRepo,
        ProcesarVentaHandler procesarVenta,
        ILogger<PosController> logger)
    {
        _productoRepo = productoRepo;
        _inventarioRepo = inventarioRepo;
        _sucursalRepo = sucursalRepo;
        _clienteRepo = clienteRepo;
        _enterpriseRepo = enterpriseRepo;
        _invoiceRepo = invoiceRepo;
        _cajaRepo = cajaRepo;
        _procesarVenta = procesarVenta;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var sucursales = (await _sucursalRepo.GetAllActiveAsync()).ToList();

        // El turno relevante es el del usuario que está operando el terminal.
        var usuarioId = SesionUsuario.ObtenerUsuarioId(User) ?? 0;
        var turnoActivo = await _cajaRepo.GetTurnoAbiertoDeUsuarioAsync(usuarioId);

        // Determinar sucursal activa: la del turno o la sucursal principal
        int activeSucursalId = turnoActivo?.SucursalId
            ?? (await _sucursalRepo.GetPrincipalAsync())?.Id
            ?? (sucursales.FirstOrDefault()?.Id ?? 1);

        var sucursalActiva = sucursales.FirstOrDefault(s => s.Id == activeSucursalId)
            ?? await _sucursalRepo.GetPrincipalAsync();

        var productos = (await _productoRepo.GetAllActiveAsync()).ToList();
        var inventarios = (await _inventarioRepo.GetBySucursalAsync(activeSucursalId)).ToDictionary(i => i.ProductoId);

        foreach (var p in productos)
        {
            if (inventarios.TryGetValue(p.Id, out var inv))
            {
                p.StockActual = inv.StockActual;
                p.StockMinimo = inv.StockMinimo;
            }
            else
            {
                p.StockActual = 0m;
                p.StockMinimo = 5m;
            }
        }

        var clientes = await _clienteRepo.GetAllAsync();
        var enterprise = await _enterpriseRepo.GetDefaultAsync();

        ViewBag.Enterprise = enterprise;
        ViewBag.Clientes = clientes;
        ViewBag.Sucursales = sucursales;
        ViewBag.SucursalActiva = sucursalActiva;
        ViewBag.TurnoActivo = turnoActivo;

        return View(productos);
    }

    [HttpGet]
    public async Task<IActionResult> BuscarProductos(string query, int? sucursalId)
    {
        var targetSucursal = sucursalId ?? (await _sucursalRepo.GetPrincipalAsync())?.Id ?? 1;
        var productos = (await _productoRepo.GetAllActiveAsync()).ToList();
        var inventarios = (await _inventarioRepo.GetBySucursalAsync(targetSucursal)).ToDictionary(i => i.ProductoId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            productos = productos.Where(p =>
                p.Codigo.ToLowerInvariant().Contains(q) ||
                p.Descripcion.ToLowerInvariant().Contains(q)).ToList();
        }

        return Json(productos.Select(p => new
        {
            p.Id,
            p.Codigo,
            p.Descripcion,
            p.Categoria,
            p.PrecioUnitario,
            StockActual = inventarios.TryGetValue(p.Id, out var inv) ? inv.StockActual : 0m,
            StockMinimo = inventarios.TryGetValue(p.Id, out var inv2) ? inv2.StockMinimo : 5m,
            p.IndicadorFacturacion,
            TasaITBIS = IndicadorFacturacionHelper.ObtenerTasaITBIS(p.IndicadorFacturacion),
            p.UnidadMedida,
            UnidadNombre = UnidadMedidaFormatter.GetAbreviatura(p.UnidadMedida)
        }));
    }

    /// <summary>
    /// Registra una venta. Toda la lógica (precios, impuestos, cobro, numeración, inventario, caja,
    /// comprobante y cola) ocurre en una única transacción dentro del caso de uso.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ProcesarVenta([FromBody] VentaPosRequest request)
    {
        if (request == null)
            return BadRequest(new VentaPosResponse
            {
                Exitoso = false,
                CodigoError = "SOLICITUD_NULA",
                Mensaje = "No se recibió la solicitud de venta."
            });

        var command = ConstruirComando(request);

        var resultado = await _procesarVenta.EjecutarAsync(command, HttpContext.RequestAborted);

        if (!resultado.Exitoso)
        {
            _logger.LogWarning(
                "Venta rechazada ({Codigo}): {Mensaje}",
                resultado.CodigoError,
                resultado.Mensaje);

            return BadRequest(new VentaPosResponse
            {
                Exitoso = false,
                CodigoError = resultado.CodigoError,
                Mensaje = resultado.Mensaje
            });
        }

        return Ok(new VentaPosResponse
        {
            Exitoso = true,
            Duplicada = resultado.Duplicada,
            VentaId = resultado.VentaId,
            ElectronicInvoiceId = resultado.ElectronicInvoiceId,
            eNCF = resultado.eNCF,
            TrackId = resultado.TrackId,
            Total = resultado.Total,
            Cambio = resultado.Cambio,
            Estado = resultado.EstadoFiscal,
            EsOfflineDGII = resultado.EsOfflineDGII,
            Mensaje = resultado.Mensaje
        });
    }

    /// <summary>
    /// Traduce el cuerpo HTTP al comando del caso de uso. Los importes, impuestos y descripciones que
    /// el navegador pudiera enviar no se propagan: el servidor los resuelve desde el catálogo.
    /// </summary>
    private ProcesarVentaCommand ConstruirComando(VentaPosRequest request)
    {
        var clave = request.ClaveIdempotencia ?? Guid.Empty;
        if (clave == Guid.Empty)
        {
            // Compatibilidad con terminales que aún no envían la clave: sin ella no hay protección
            // contra duplicados, por lo que se registra el hecho y se genera una nueva.
            clave = Guid.NewGuid();
            _logger.LogWarning(
                "La solicitud de venta llegó sin clave de idempotencia; no se pudo proteger contra reenvíos duplicados.");
        }

        return new ProcesarVentaCommand
        {
            ClaveIdempotencia = clave,
            SucursalId = request.SucursalId,
            ClienteId = request.ClienteId,
            RNCComprador = request.RNCComprador,
            RazonSocialComprador = request.RazonSocialComprador,
            TipoeCF = request.TipoeCF,
            TipoPago = request.TipoPago,
            MetodoPago = request.MetodoPago,
            MontoRecibido = request.MontoRecibido,
            UsuarioId = SesionUsuario.ObtenerUsuarioId(User) ?? 0,
            UsuarioNombre = SesionUsuario.ObtenerNombre(User),
            Items = (request.Items ?? new List<VentaPosItemRequest>())
                .Select(i => new ItemVentaCommand
                {
                    ProductoId = i.ProductoId,
                    Cantidad = i.Cantidad,
                    Descuento = i.Descuento
                })
                .ToList(),
            Pagos = (request.Pagos ?? new List<PagoFacturaDto>())
                .Select(p => new PagoVentaCommand
                {
                    MetodoPago = p.MetodoPago,
                    Monto = p.Monto,
                    Referencia = p.Referencia
                })
                .ToList()
        };
    }

    [HttpGet]
    public async Task<IActionResult> Ticket(int id)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(id);
        if (invoice == null) return NotFound();

        var enterprise = await _enterpriseRepo.GetDefaultAsync();

        var urlQr = DgiiQrHelper.GenerarUrlConsultaDgii(
            invoice.RNCEmisor,
            invoice.RNCComprador,
            invoice.eNCF,
            invoice.FechaEmision,
            invoice.MontoTotal,
            invoice.TotalITBIS,
            invoice.XMLHash);

        ViewBag.Enterprise = enterprise;
        ViewBag.UrlQr = urlQr;

        return View(invoice);
    }
}
