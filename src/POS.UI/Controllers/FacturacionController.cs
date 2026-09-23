using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.CasosDeUso.Facturacion;
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
    private readonly EmitirNotaCreditoDevolucionHandler _notaCreditoHandler;
    private readonly IAuditoriaRepository _auditoriaRepo;

    public FacturacionController(
        IInvoiceRepository invoiceRepo,
        IEnterpriseRepository enterpriseRepo,
        IAnulacionRepository anulacionRepo,
        IElectronicInvoiceService invoiceService,
        EmitirNotaCreditoDevolucionHandler notaCreditoHandler,
        IAuditoriaRepository auditoriaRepo)
    {
        _invoiceRepo = invoiceRepo;
        _enterpriseRepo = enterpriseRepo;
        _anulacionRepo = anulacionRepo;
        _invoiceService = invoiceService;
        _notaCreditoHandler = notaCreditoHandler;
        _auditoriaRepo = auditoriaRepo;
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

    // ------------------------------------------------------------------
    // FASE 3: la emisión libre de comprobantes (GET/POST Emitir) fue retirada.
    // Permitía escribir el eNCF a mano y emitir sin venta, sin caja, sin kardex y sin idempotencia:
    // una segunda ruta fiscal que eludía el control de integridad del POS.
    // El único camino de registro es ProcesarVentaHandler; la numeración la asigna SecuenciaECF.
    // Historial disponible en el commit bc1ecf7 y anteriores.
    // ------------------------------------------------------------------

    public async Task<IActionResult> Detalle(int id)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(id);
        if (invoice == null)
            return NotFound();

        // Las notas de crédito 34 muestran de forma destacada el comprobante que corrigen:
        // su estado fiscal vigente, montos y acceso directo a su detalle.
        ViewBag.ComprobanteOriginal = !string.IsNullOrWhiteSpace(invoice.eNCFModificado)
            ? await _invoiceRepo.GetByENCFAsync(invoice.eNCFModificado)
            : null;

        return View(invoice);
    }

    /// <summary>
    /// Emite la nota de crédito e-CF 34 de una devolución registrada (5.4). El caso de uso es
    /// idempotente: reintentar sobre una devolución ya saldada devuelve la nota original.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Politicas.Supervision)]
    public async Task<IActionResult> EmitirNotaCredito(int devolucionId)
    {
        var resultado = await _notaCreditoHandler.EjecutarAsync(new NotaCreditoDevolucionCommand
        {
            DevolucionId = devolucionId,
            UsuarioNombre = SesionUsuario.ObtenerNombre(User) ?? string.Empty
        });

        TempData[resultado.Exitoso ? "Mensaje" : "Error"] = resultado.Mensaje;

        return resultado.ElectronicInvoiceId > 0
            ? RedirectToAction(nameof(Detalle), new { id = resultado.ElectronicInvoiceId })
            : RedirectToAction(nameof(Lista));
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

    /// <summary>
    /// Portal de consultas (6.3): el listado ya muestra estado fiscal, TrackId y mensajes de la
    /// DGII. El reenvío manual queda en este controlador con política de SUPERVISIÓN y traza de
    /// auditoría: quién reenvió qué comprobante y cuándo.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Politicas.Supervision)]
    public async Task<IActionResult> Reenviar(string encf)
    {
        var response = await _invoiceService.ReenviarAsync(encf);
        if (response.Exitoso)
            TempData["Mensaje"] = $"Factura reenviada a DGII. TrackId: {response.TrackId}";
        else
            TempData["Error"] = $"Fallo al reenviar: {response.Mensaje}";

        // Trazabilidad del reenvío manual (6.3): siempre se registra, exitoso o no.
        await _auditoriaRepo.RegistrarAsync(new Domain.Entities.AuditoriaCambio
        {
            Usuario = SesionUsuario.ObtenerNombre(User) ?? "desconocido",
            Entidad = "ElectronicInvoice",
            Campo = "ReenvioManual",
            ValorAnterior = encf,
            ValorNuevo = response.Exitoso
                ? $"TrackId={response.TrackId ?? "sin-track"}"
                : $"Fallo: {response.Mensaje}",
            Motivo = "Reenvío manual desde el portal de consultas"
        });

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
