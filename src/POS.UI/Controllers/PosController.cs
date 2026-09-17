using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Domain.Types;
using POS.Infrastructure.DGII;

namespace POS.UI.Controllers;

public class PosController : Controller
{
    private readonly IProductoRepository _productoRepo;
    private readonly IInventarioAlmacenRepository _inventarioRepo;
    private readonly ISucursalRepository _sucursalRepo;
    private readonly IClienteRepository _clienteRepo;
    private readonly IEnterpriseRepository _enterpriseRepo;
    private readonly IVentaRepository _ventaRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly ICajaTurnoRepository _cajaRepo;
    private readonly IElectronicInvoiceService _invoiceService;
    private readonly ITaxCalculator _taxCalculator;
    private readonly IMovimientoInventarioRepository _movimientoRepo;
    private readonly IEmisionDGIIQueueRepository _queueRepo;

    public PosController(
        IProductoRepository productoRepo,
        IInventarioAlmacenRepository inventarioRepo,
        ISucursalRepository sucursalRepo,
        IClienteRepository clienteRepo,
        IEnterpriseRepository enterpriseRepo,
        IVentaRepository ventaRepo,
        IInvoiceRepository invoiceRepo,
        ICajaTurnoRepository cajaRepo,
        IElectronicInvoiceService invoiceService,
        ITaxCalculator taxCalculator,
        IMovimientoInventarioRepository movimientoRepo,
        IEmisionDGIIQueueRepository queueRepo)
    {
        _productoRepo = productoRepo;
        _inventarioRepo = inventarioRepo;
        _sucursalRepo = sucursalRepo;
        _clienteRepo = clienteRepo;
        _enterpriseRepo = enterpriseRepo;
        _ventaRepo = ventaRepo;
        _invoiceRepo = invoiceRepo;
        _cajaRepo = cajaRepo;
        _invoiceService = invoiceService;
        _taxCalculator = taxCalculator;
        _movimientoRepo = movimientoRepo;
        _queueRepo = queueRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var sucursales = (await _sucursalRepo.GetAllActiveAsync()).ToList();
        var turnoActivo = await _cajaRepo.GetTurnoActivoAsync();

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

    [HttpPost]
    public async Task<IActionResult> ProcesarVenta([FromBody] VentaPosRequest request)
    {
        if (request == null || request.Items == null || !request.Items.Any())
            return BadRequest(new { mensaje = "El carrito de compras no contiene productos." });

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        if (enterprise == null)
            return BadRequest(new { mensaje = "No hay una empresa configurada en el sistema." });

        var turnoActivo = await _cajaRepo.GetTurnoActivoAsync();
        int targetSucursalId = request.SucursalId 
            ?? turnoActivo?.SucursalId 
            ?? (await _sucursalRepo.GetPrincipalAsync())?.Id 
            ?? 1;

        // Validar política de inventario por almacén de sucursal
        if (!enterprise.PermitirVentaSinStock)
        {
            foreach (var itm in request.Items.Where(i => i.ProductoId > 0))
            {
                var stockDisponible = await _inventarioRepo.GetStockAsync(itm.ProductoId, targetSucursalId);
                if (stockDisponible < itm.Cantidad)
                {
                    var prod = await _productoRepo.GetByIdAsync(itm.ProductoId);
                    var desc = prod?.Descripcion ?? $"Producto #{itm.ProductoId}";
                    return BadRequest(new { mensaje = $"Stock insuficiente en sucursal para '{desc}'. Existencia disponible: {stockDisponible:0.##}, solicitada: {itm.Cantidad:0.##}." });
                }
            }
        }

        // 1. Crear Venta de Dominio
        var venta = new Venta
        {
            NumeroFacturaInterna = $"FAC-{DateTime.UtcNow:yyyyMMddHHmmss}",
            EnterpriseId = enterprise.Id,
            SucursalId = targetSucursalId,
            CajaTurnoId = turnoActivo?.Id,
            ClienteId = request.ClienteId,
            Fecha = DateTime.UtcNow,
            TipoPago = request.TipoPago,
            MetodoPago = request.MetodoPago,
            TipoeCF = request.TipoeCF
        };

        foreach (var item in request.Items)
        {
            var ventaItem = new VentaItem
            {
                ProductoId = item.ProductoId > 0 ? item.ProductoId : null,
                Descripcion = item.Descripcion,
                Cantidad = item.Cantidad,
                PrecioUnitario = item.PrecioUnitario,
                Descuento = item.Descuento,
                IndicadorFacturacion = item.IndicadorFacturacion
            };
            venta.AddItem(ventaItem);
        }

        // 2. Pagos Mixtos si fueron suministrados
        if (request.Pagos != null && request.Pagos.Any())
        {
            foreach (var p in request.Pagos)
            {
                venta.Pagos.Add(new PagoFactura
                {
                    MetodoPago = p.MetodoPago.ToString(),
                    Monto = p.Monto,
                    Referencia = p.Referencia
                });
            }
        }

        await _ventaRepo.AddAsync(venta);

        // 3. Generar el siguiente eNCF según el tipo
        var serie = request.TipoeCF == TipoeCFType.FacturaCreditoFiscal ? "E31" : "E32";
        var ultimoEncf = await _invoiceRepo.GetLastENCFAsync(serie);

        long secuencia = 1;
        if (!string.IsNullOrEmpty(ultimoEncf) && ultimoEncf.Length >= 13)
        {
            var secPart = ultimoEncf[3..];
            if (long.TryParse(secPart, out var num))
                secuencia = num + 1;
        }

        var nuevoENCF = $"{serie}{secuencia:D10}";

        // 4. Preparar request e-CF y calcular totales
        var invoiceItems = request.Items.Select((itm, idx) => new InvoiceItemRequest
        {
            Indice = idx + 1,
            Codigo = itm.ProductoId.ToString(),
            Descripcion = itm.Descripcion,
            Cantidad = itm.Cantidad,
            PrecioUnitario = itm.PrecioUnitario,
            Descuento = itm.Descuento,
            IndicadorFacturacion = itm.IndicadorFacturacion,
            UnidadMedida = itm.UnidadMedida
        }).ToList();

        var totales = _taxCalculator.CalcularTotales(invoiceItems);

        // 5. Impacto en Arqueo de Turno de Caja (soporte para pagos mixtos)
        if (turnoActivo != null)
        {
            if (request.Pagos != null && request.Pagos.Any())
            {
                turnoActivo.VentasEfectivo += request.Pagos.Where(p => p.MetodoPago == MetodoPago.Efectivo).Sum(p => p.Monto);
                turnoActivo.VentasTarjeta += request.Pagos.Where(p => p.MetodoPago == MetodoPago.TarjetaDebitoCredito).Sum(p => p.Monto);
                turnoActivo.VentasTransferencia += request.Pagos.Where(p => p.MetodoPago == MetodoPago.ChequeTransferenciaDeposito).Sum(p => p.Monto);
            }
            else
            {
                if (request.MetodoPago == MetodoPago.Efectivo)
                    turnoActivo.VentasEfectivo += totales.Total;
                else if (request.MetodoPago == MetodoPago.TarjetaDebitoCredito)
                    turnoActivo.VentasTarjeta += totales.Total;
                else
                    turnoActivo.VentasTransferencia += totales.Total;
            }

            turnoActivo.TotalVentas += totales.Total;
            turnoActivo.CantidadTransacciones += 1;
            await _cajaRepo.UpdateAsync(turnoActivo);
        }

        var ecfRequest = new ElectronicInvoiceRequest
        {
            TipoeCF = request.TipoeCF,
            eNCF = nuevoENCF,
            FechaFactura = FechaDominicana.Today,
            TipoIngresos = TipoIngresosType.IngresosOperaciones,
            TipoPago = request.TipoPago,
            MetodoPago = request.MetodoPago,
            Emisor = new EmisorRequest
            {
                RNC = enterprise.RNC,
                RazonSocial = enterprise.RazonSocial,
                NombreComercial = enterprise.NombreComercial,
                Direccion = enterprise.Direccion,
                Telefono = enterprise.Telefono,
                Email = enterprise.Email,
                CodigoProvincia = enterprise.CodigoProvincia,
                CodigoMunicipio = enterprise.CodigoMunicipio
            },
            Comprador = new CompradorRequest
            {
                RNC = request.RNCComprador,
                RazonSocial = string.IsNullOrWhiteSpace(request.RazonSocialComprador) ? "Consumidor Final" : request.RazonSocialComprador
            },
            Items = invoiceItems,
            Totales = totales
        };

        // 6. Emitir e-CF con tolerancia a fallas de red (Resiliencia DGII Offline)
        var emitirCommand = new EmitirFacturaCommand
        {
            Request = ecfRequest,
            VentaId = venta.Id
        };

        ElectronicInvoiceResponse emitirResult;
        bool esOffline = false;

        try
        {
            emitirResult = await _invoiceService.EmitirAsync(emitirCommand);
            if (!emitirResult.Exitoso)
            {
                esOffline = true;
            }
        }
        catch
        {
            esOffline = true;
            emitirResult = new ElectronicInvoiceResponse
            {
                Exitoso = true,
                Estado = EstadoFacturaElectronica.PendienteReenvio,
                Mensaje = "Venta registrada localmente. Factura encolada para transmisión diferida a DGII (Modo Resiliente)."
            };
        }

        var savedInvoice = await _invoiceRepo.GetByENCFAsync(nuevoENCF);

        // Si se emitió en modo offline o hubo fallo de comunicación con DGII, encolar en cola de fondo
        if (esOffline && savedInvoice != null)
        {
            await _queueRepo.AddAsync(new EmisionDGIIQueue
            {
                FacturaId = savedInvoice.Id,
                eNCF = nuevoENCF,
                XmlFirmado = savedInvoice.XMLContent ?? string.Empty,
                Intentos = 0,
                EnviadoExitosamente = false,
                FechaRegistro = DateTime.UtcNow
            });
        }

        var montoRecibido = request.MontoRecibido ?? totales.Total;
        var cambio = Math.Max(0, montoRecibido - totales.Total);

        // 7. Descontar existencias en el Almacén de la Sucursal Activa y registrar Kardex
        foreach (var itm in request.Items.Where(i => i.ProductoId > 0))
        {
            var stockAnterior = await _inventarioRepo.GetStockAsync(itm.ProductoId, targetSucursalId);
            var resultante = stockAnterior - itm.Cantidad;

            await _inventarioRepo.SetStockAsync(itm.ProductoId, targetSucursalId, resultante);

            var prod = await _productoRepo.GetByIdAsync(itm.ProductoId);
            await _movimientoRepo.AddAsync(new MovimientoInventario
            {
                ProductoId = itm.ProductoId,
                SucursalId = targetSucursalId,
                Tipo = TipoMovimientoInventario.VentaPOS,
                Cantidad = itm.Cantidad,
                StockAnterior = stockAnterior,
                StockNuevo = resultante,
                CostoUnitario = prod?.CostoUnitario ?? 0m,
                Concepto = $"Salida POS e-CF {nuevoENCF}",
                ReferenciaDocumento = nuevoENCF,
                Fecha = DateTime.UtcNow
            });
        }

        return Ok(new VentaPosResponse
        {
            Exitoso = true,
            VentaId = venta.Id,
            ElectronicInvoiceId = savedInvoice?.Id,
            eNCF = nuevoENCF,
            TrackId = emitirResult.TrackId,
            Total = totales.Total,
            Cambio = cambio,
            Estado = emitirResult.Estado,
            EsOfflineDGII = esOffline,
            Mensaje = emitirResult.Mensaje
        });
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
            invoice.XMLHash
        );

        ViewBag.Enterprise = enterprise;
        ViewBag.UrlQr = urlQr;

        return View(invoice);
    }
}
