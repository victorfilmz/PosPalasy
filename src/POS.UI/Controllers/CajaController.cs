using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.UI.Security;

namespace POS.UI.Controllers;

/// <summary>
/// Control de turnos de caja: apertura, movimientos, cortes X/Z y cierre con arqueo.
/// </summary>
[Authorize(Policy = Politicas.OperacionPos)]
public class CajaController : Controller
{
    private readonly ICajaTurnoRepository _cajaRepo;
    private readonly IEnterpriseRepository _enterpriseRepo;

    public CajaController(
        ICajaTurnoRepository cajaRepo,
        IEnterpriseRepository enterpriseRepo)
    {
        _cajaRepo = cajaRepo;
        _enterpriseRepo = enterpriseRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var turnoActivo = await ObtenerTurnoDelUsuarioAsync();
        var historial = await _cajaRepo.GetAllAsync();
        var enterprise = await _enterpriseRepo.GetDefaultAsync();

        ViewBag.Enterprise = enterprise;
        ViewBag.TurnoActivo = turnoActivo;
        return View(historial.Where(t => t.Id != turnoActivo?.Id).Take(15));
    }

    [HttpGet]
    public async Task<IActionResult> Apertura()
    {
        var turnoActivo = await ObtenerTurnoDelUsuarioAsync();
        if (turnoActivo != null)
        {
            TempData["Mensaje"] = "Ya tiene un turno de caja abierto.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.NombreSugerido = SesionUsuario.ObtenerNombreCompleto(User);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Apertura(string cajero, decimal montoInicial)
    {
        var turnoActivo = await ObtenerTurnoDelUsuarioAsync();
        if (turnoActivo != null)
        {
            TempData["Error"] = "Ya tiene un turno de caja abierto.";
            return RedirectToAction(nameof(Index));
        }

        if (montoInicial < 0)
        {
            ModelState.AddModelError("", "El monto inicial de caja no puede ser negativo.");
            return View();
        }

        var (sucursalId, _) = await ResolverSucursalAsync();
        var nombreCompleto = SesionUsuario.ObtenerNombreCompleto(User);

        var nuevoTurno = new CajaTurno
        {
            SucursalId = sucursalId,
            // El responsable fiable del turno es el usuario autenticado, no el texto del formulario.
            UsuarioId = SesionUsuario.ObtenerUsuarioId(User),
            UsuarioNombre = SesionUsuario.ObtenerNombre(User),
            Cajero = string.IsNullOrWhiteSpace(cajero)
                ? (string.IsNullOrWhiteSpace(nombreCompleto) ? "Cajero Principal" : nombreCompleto)
                : cajero.Trim(),
            FechaApertura = DateTime.UtcNow,
            MontoInicial = montoInicial,
            Estado = TurnoCajaEstado.Abierto
        };

        await _cajaRepo.AddAsync(nuevoTurno);
        TempData["Mensaje"] = $"Turno de caja #{nuevoTurno.Id} abierto exitosamente con fondo inicial de RD$ {montoInicial:N2}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Cierre()
    {
        var turnoActivo = await ObtenerTurnoDelUsuarioAsync();
        if (turnoActivo == null)
        {
            TempData["Error"] = "No hay ningún turno de caja abierto para cerrar.";
            return RedirectToAction(nameof(Index));
        }

        return View(turnoActivo);
    }

    [HttpPost]
    public async Task<IActionResult> Cierre(int id, decimal montoRealCierre, string? observaciones)
    {
        // El cierre es atómico: el efectivo esperado y la diferencia se calculan en la base de datos
        // y solo el primer cierre del turno se aplica (un segundo intento no altera el arqueo).
        var filas = await _cajaRepo.CerrarTurnoAsync(id, montoRealCierre, observaciones);

        if (filas == 0)
        {
            TempData["Error"] = "El turno especificado no está abierto o no existe.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Mensaje"] = $"Turno #{id} cerrado correctamente. Arqueo completado.";
        return RedirectToAction(nameof(ReporteZ), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> CorteX(int? id)
    {
        CajaTurno? turno;
        if (id.HasValue)
            turno = await _cajaRepo.GetByIdAsync(id.Value);
        else
            turno = await _cajaRepo.GetTurnoActivoAsync();

        if (turno == null)
        {
            TempData["Error"] = "No se encontró ningún turno activo para generar el Corte X.";
            return RedirectToAction(nameof(Index));
        }

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        ViewBag.Enterprise = enterprise;
        ViewBag.TipoCorte = "X";
        ViewBag.TituloReporte = "CORTE PARCIAL DE CAJA (CORTE X)";
        return View("TicketCorte", turno);
    }

    [HttpGet]
    public async Task<IActionResult> ReporteZ(int id)
    {
        var turno = await _cajaRepo.GetByIdAsync(id);
        if (turno == null)
            return NotFound();

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        ViewBag.Enterprise = enterprise;
        ViewBag.TipoCorte = "Z";
        ViewBag.TituloReporte = "CIERRE DEFINITIVO DE CAJA (REPORTE Z)";
        return View("TicketCorte", turno);
    }

    [HttpPost]
    public async Task<IActionResult> Movimiento(int turnoId, TipoMovimientoCaja tipo, decimal monto, string concepto)
    {
        var turno = await _cajaRepo.GetByIdAsync(turnoId);
        if (turno == null || turno.Estado != TurnoCajaEstado.Abierto)
        {
            TempData["Error"] = "El turno de caja no está abierto.";
            return RedirectToAction(nameof(Index));
        }

        if (monto <= 0)
        {
            TempData["Error"] = "El monto del movimiento debe ser mayor a cero.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(concepto))
        {
            TempData["Error"] = "Debe especificar el concepto del movimiento de caja.";
            return RedirectToAction(nameof(Index));
        }

        var movimiento = new MovimientoCaja
        {
            CajaTurnoId = turno.Id,
            Tipo = tipo,
            Monto = monto,
            Concepto = concepto.Trim(),
            Fecha = DateTime.UtcNow
        };

        try
        {
            await _cajaRepo.RegistrarMovimientoAsync(movimiento);
        }
        catch (POS.Domain.Common.ReglaDeNegocioException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }

        TempData["Mensaje"] = $"Movimiento de {tipo} por RD$ {monto:N2} registrado correctamente.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Turno abierto del usuario autenticado (el nombre escrito a mano no identifica a nadie).</summary>
    private async Task<CajaTurno?> ObtenerTurnoDelUsuarioAsync()
    {
        var usuarioId = SesionUsuario.ObtenerUsuarioId(User) ?? 0;
        return await _cajaRepo.GetTurnoAbiertoDeUsuarioAsync(usuarioId);
    }

    /// <summary>Sucursal donde opera este terminal: la principal de la empresa emisora.</summary>
    private async Task<(int SucursalId, string? Nombre)> ResolverSucursalAsync()
    {
        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        var sucursal = enterprise?.Sucursales.FirstOrDefault();
        return (sucursal?.Id ?? 1, sucursal?.Nombre);
    }
}
