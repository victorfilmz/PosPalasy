using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.UI.Services;

namespace POS.UI.Controllers;

/// <summary>
/// Panel de métricas de comprobantes electrónicos. Accesible a cualquier usuario autenticado
/// (la información consolidada de reportes queda restringida en su propio controlador).
/// </summary>
[Authorize]
public class DashboardController : Controller
{
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly IVentaRepository _ventaRepo;
    private readonly DgiiConectividadHealthCheck _conectividadDgii;

    public DashboardController(
        IInvoiceRepository invoiceRepo,
        IVentaRepository ventaRepo,
        DgiiConectividadHealthCheck conectividadDgii)
    {
        _invoiceRepo = invoiceRepo;
        _ventaRepo = ventaRepo;
        _conectividadDgii = conectividadDgii;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Dashboard - PosPalasy DGII";

        // Métricas rápidas
        var pending = await _invoiceRepo.GetByEstadoAsync(EstadoFacturaElectronica.EnProceso);
        var accepted = await _invoiceRepo.GetByEstadoAsync(EstadoFacturaElectronica.Aceptado);
        var rejected = await _invoiceRepo.GetByEstadoAsync(EstadoFacturaElectronica.Rechazado);
        var contingencia = await _invoiceRepo.GetByEstadoAsync(EstadoFacturaElectronica.PendienteReenvio);

        ViewBag.EnProcesoCount = ((System.Collections.Generic.List<Domain.Entities.ElectronicInvoice>)pending).Count;
        ViewBag.AceptadosCount = ((System.Collections.Generic.List<Domain.Entities.ElectronicInvoice>)accepted).Count;
        ViewBag.RechazadosCount = ((System.Collections.Generic.List<Domain.Entities.ElectronicInvoice>)rejected).Count;
        ViewBag.ContingenciaCount = ((System.Collections.Generic.List<Domain.Entities.ElectronicInvoice>)contingencia).Count;

        // Estado DGII para el badge del dashboard: modo (simulador/real) + conectividad.
        var estadoDgii = await _conectividadDgii.CheckHealthAsync(
            new HealthCheckContext(),
            HttpContext.RequestAborted);
        ViewBag.DgiiEsSimulador = _conectividadDgii.EsSimulador;
        ViewBag.DgiiAmbiente = _conectividadDgii.Ambiente;
        ViewBag.DgiiEstado = estadoDgii.Status.ToString(); // Healthy | Degraded
        ViewBag.DgiiDetalle = _conectividadDgii.UltimoDetalle;

        return View();
    }
}
