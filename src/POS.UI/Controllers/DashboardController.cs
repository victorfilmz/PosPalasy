using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Enums;
using POS.Domain.Repositories;

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

    public DashboardController(IInvoiceRepository invoiceRepo, IVentaRepository ventaRepo)
    {
        _invoiceRepo = invoiceRepo;
        _ventaRepo = ventaRepo;
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

        return View();
    }
}
