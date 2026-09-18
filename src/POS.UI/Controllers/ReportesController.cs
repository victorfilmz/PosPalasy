using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Interfaces;
using POS.Domain.Entities;
using POS.Domain.Repositories;
using POS.UI.Security;

namespace POS.UI.Controllers;

/// <summary>
/// Reportes fiscales para la DGII (Libro de Ventas 607 y resumen de ITBIS del IT-1).
/// Solo roles contables, de supervisión o administración.
/// </summary>
[Authorize(Policy = Politicas.ReportesFiscales)]
public class ReportesController : Controller
{
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly IEnterpriseRepository _enterpriseRepo;
    private readonly IReporteFiscalService _reporteService;

    public ReportesController(
        IInvoiceRepository invoiceRepo,
        IEnterpriseRepository enterpriseRepo,
        IReporteFiscalService reporteService)
    {
        _invoiceRepo = invoiceRepo;
        _enterpriseRepo = enterpriseRepo;
        _reporteService = reporteService;
    }

    [HttpGet]
    public async Task<IActionResult> Ventas607(int? anio, int? mes)
    {
        var targetAnio = anio ?? DateTime.UtcNow.Year;
        var targetMes = mes ?? DateTime.UtcNow.Month;

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        var facturas = await _invoiceRepo.GetByRNCAsync(enterprise?.RNC ?? "13100000001");

        // Filtrar por período si la fecha coincide, o mostrar las facturas del período
        var registros = _reporteService.GenerarRegistros607(facturas);

        ViewBag.Anio = targetAnio;
        ViewBag.Mes = targetMes;
        ViewBag.Enterprise = enterprise;

        return View(registros);
    }

    [HttpGet]
    public async Task<IActionResult> Exportar607Txt(int anio, int mes)
    {
        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        var rnc = enterprise?.RNC ?? "13100000001";
        var facturas = await _invoiceRepo.GetByRNCAsync(rnc);

        var registros = _reporteService.GenerarRegistros607(facturas);
        var txt = _reporteService.GenerarArchivo607Txt(rnc, anio, mes, registros);

        var fileName = $"DGII_F_607_{rnc}_{anio:D4}{mes:D2}.txt";
        var bytes = Encoding.UTF8.GetBytes(txt);

        return File(bytes, "text/plain; charset=utf-8", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> ResumenItbis(int? anio, int? mes)
    {
        var targetAnio = anio ?? DateTime.UtcNow.Year;
        var targetMes = mes ?? DateTime.UtcNow.Month;

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        var facturas = await _invoiceRepo.GetByRNCAsync(enterprise?.RNC ?? "13100000001");

        var resumen = _reporteService.GenerarResumenItbis(targetAnio, targetMes, facturas);
        ViewBag.Enterprise = enterprise;

        return View(resumen);
    }
}
