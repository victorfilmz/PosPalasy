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
    private readonly IMovimientoInventarioRepository _movimientoRepo;
    private readonly IReporteFiscalService _reporteService;

    public ReportesController(
        IInvoiceRepository invoiceRepo,
        IEnterpriseRepository enterpriseRepo,
        IMovimientoInventarioRepository movimientoRepo,
        IReporteFiscalService reporteService)
    {
        _invoiceRepo = invoiceRepo;
        _enterpriseRepo = enterpriseRepo;
        _movimientoRepo = movimientoRepo;
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

    /// <summary>
    /// Libro de compras 606 (sub-fase 6.0): entradas por compra del kardex en el período.
    /// La fuente es el kardex (EntradaCompra con proveedor); el módulo de compras formal es
    /// un pendiente del propietario (decisión D1 del doc 15).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Compras606(int? anio, int? mes)
    {
        var (targetAnio, targetMes, desde, hasta) = Periodo(anio, mes);

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        var entradas = await _movimientoRepo.GetByTipoYFechaAsync(
            TipoMovimientoInventario.EntradaCompra, desde, hasta);

        var registros = _reporteService.GenerarRegistros606(entradas);

        ViewBag.Anio = targetAnio;
        ViewBag.Mes = targetMes;
        ViewBag.Enterprise = enterprise;

        return View(registros);
    }

    [HttpGet]
    public async Task<IActionResult> Exportar606Txt(int anio, int mes)
    {
        var (targetAnio, targetMes, desde, hasta) = Periodo(anio, mes);

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        var rnc = enterprise?.RNC ?? "13100000001";
        var entradas = await _movimientoRepo.GetByTipoYFechaAsync(
            TipoMovimientoInventario.EntradaCompra, desde, hasta);

        var registros = _reporteService.GenerarRegistros606(entradas);
        var txt = _reporteService.GenerarArchivo606Txt(rnc, targetAnio, targetMes, registros);

        var fileName = $"DGII_F_606_{rnc}_{targetAnio:D4}{targetMes:D2}.txt";
        var bytes = Encoding.UTF8.GetBytes(txt);

        return File(bytes, "text/plain; charset=utf-8", fileName);
    }

    /// <summary>Rango UTC [primer día del mes 00:00, primer día del mes siguiente) para un período.</summary>
    private static (int Anio, int Mes, DateTime DesdeUtc, DateTime HastaUtc) Periodo(int? anio, int? mes)
    {
        var targetAnio = anio ?? DateTime.UtcNow.Year;
        var targetMes = Math.Clamp(mes ?? DateTime.UtcNow.Month, 1, 12);
        var desde = new DateTime(targetAnio, targetMes, 1, 0, 0, 0, DateTimeKind.Utc);
        return (targetAnio, targetMes, desde, desde.AddMonths(1));
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
