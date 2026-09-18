using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Domain.Types;
using POS.UI.Security;

namespace POS.UI.Controllers;

/// <summary>
/// Comprobantes fiscales electrónicos: listado, detalle, emisión manual, reenvío y anulación.
/// Las operaciones que alteran el estado fiscal exigen supervisión, no solo operación de POS.
/// </summary>
[Authorize(Policy = Politicas.OperacionPos)]
public class FacturacionController : Controller
{
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly IEnterpriseRepository _enterpriseRepo;
    private readonly IAnulacionRepository _anulacionRepo;
    private readonly IElectronicInvoiceService _invoiceService;
    private readonly ITaxCalculator _taxCalculator;

    public FacturacionController(
        IInvoiceRepository invoiceRepo,
        IEnterpriseRepository enterpriseRepo,
        IAnulacionRepository anulacionRepo,
        IElectronicInvoiceService invoiceService,
        ITaxCalculator taxCalculator)
    {
        _invoiceRepo = invoiceRepo;
        _enterpriseRepo = enterpriseRepo;
        _anulacionRepo = anulacionRepo;
        _invoiceService = invoiceService;
        _taxCalculator = taxCalculator;
    }

    public async Task<IActionResult> Lista(EstadoFacturaElectronica? estado = null)
    {
        IEnumerable<Domain.Entities.ElectronicInvoice> invoices;
        if (estado.HasValue)
        {
            invoices = await _invoiceRepo.GetByEstadoAsync(estado.Value);
        }
        else
        {
            var enterprise = await _enterpriseRepo.GetDefaultAsync();
            invoices = await _invoiceRepo.GetByRNCAsync(enterprise?.RNC ?? "13100000001");
        }

        ViewBag.FiltroEstado = estado;
        return View(invoices);
    }

    [HttpGet]
    [Authorize(Policy = Politicas.Supervision)]
    public async Task<IActionResult> Emitir()
    {
        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        var model = new ElectronicInvoiceRequest
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "E320000000001",
            Emisor = new EmisorRequest
            {
                RNC = enterprise?.RNC ?? "13100000001",
                RazonSocial = enterprise?.RazonSocial ?? "PosPalasy SRL",
                NombreComercial = enterprise?.NombreComercial,
                Direccion = enterprise?.Direccion ?? "Av. 27 de Febrero",
                Telefono = enterprise?.Telefono ?? "809-555-0199",
                Email = enterprise?.Email ?? "facturacion@pospalasy.com.do"
            },
            Comprador = new CompradorRequest
            {
                RazonSocial = "Consumidor Final"
            },
            Items = new List<InvoiceItemRequest>
            {
                new()
                {
                    Indice = 1,
                    Descripcion = "Producto Ejemplo POS",
                    Cantidad = 1,
                    PrecioUnitario = 100m,
                    IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                    UnidadMedida = UnidadMedidaType.Unidad
                }
            }
        };

        model.Totales = _taxCalculator.CalcularTotales(model.Items);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = Politicas.Supervision)]
    public async Task<IActionResult> Emitir(ElectronicInvoiceRequest request)
    {
        request.Totales = _taxCalculator.CalcularTotales(request.Items);

        var command = new EmitirFacturaCommand
        {
            Request = request
        };

        var response = await _invoiceService.EmitirAsync(command);

        if (response.Exitoso)
        {
            TempData["Mensaje"] = $"Factura emitida con éxito. eNCF: {response.eNCF}. TrackId: {response.TrackId}";
            return RedirectToAction(nameof(Lista));
        }

        ModelState.AddModelError("", response.Mensaje ?? "Ocurrió un error al emitir la factura electrónica.");
        return View(request);
    }

    public async Task<IActionResult> Detalle(int id)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(id);
        if (invoice == null)
            return NotFound();

        return View(invoice);
    }

    [HttpPost]
    [Authorize(Policy = Politicas.Supervision)]
    public async Task<IActionResult> ConsultarEstado(int id)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(id);
        if (invoice == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(invoice.TrackId))
        {
            TempData["Error"] = "La factura no posee TrackId para consultar en DGII.";
            return RedirectToAction(nameof(Detalle), new { id });
        }

        var result = await _invoiceService.ConsultarEstadoPorTrackIdAsync(invoice.TrackId);
        TempData["Mensaje"] = $"Estado consultado: {result.Estado}. {result.MensajeDGII}";

        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Politicas.Supervision)]
    public async Task<IActionResult> Reenviar(string encf)
    {
        var response = await _invoiceService.ReenviarAsync(encf);
        if (response.Exitoso)
            TempData["Mensaje"] = $"Factura reenviada a DGII. TrackId: {response.TrackId}";
        else
            TempData["Error"] = $"Fallo al reenviar: {response.Mensaje}";

        return RedirectToAction(nameof(Lista));
    }

    [HttpGet]
    [Authorize(Policy = Politicas.Supervision)]
    public async Task<IActionResult> Anulaciones()
    {
        var anulaciones = await _anulacionRepo.GetAllAsync();
        return View(anulaciones);
    }

    [HttpPost]
    [Authorize(Policy = Politicas.Supervision)]
    public async Task<IActionResult> Anular(int id, int codigoMotivo, string motivo)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(id);
        if (invoice == null) return NotFound();

        if (string.IsNullOrWhiteSpace(motivo))
        {
            TempData["Error"] = "Debe especificar un motivo para la anulación de la factura.";
            return RedirectToAction(nameof(Detalle), new { id });
        }

        var request = new AnulacionRequest
        {
            RNCEmisor = invoice.RNCEmisor,
            TipoeCF = invoice.TipoeCF,
            eNCFDesde = invoice.eNCF,
            eNCFHasta = invoice.eNCF,
            CantidadSecuencias = 1,
            CodigoMotivoAnulacion = codigoMotivo > 0 ? codigoMotivo : 1,
            Motivo = motivo.Trim()
        };

        var response = await _invoiceService.AnularAsync(request);

        if (response.Exitoso)
        {
            TempData["Mensaje"] = $"Comprobante {invoice.eNCF} anulado exitosamente. {response.Mensaje}";
        }
        else
        {
            TempData["Error"] = $"Error al anular comprobante: {response.Mensaje}";
        }

        return RedirectToAction(nameof(Detalle), new { id });
    }
}
