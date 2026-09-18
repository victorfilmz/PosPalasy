using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using POS.Application.Security;
using POS.Domain.Repositories;
using POS.UI.Models;
using POS.UI.Security;

namespace POS.UI.Controllers;

/// <summary>
/// Gestión de la sesión del sistema POS: inicio y cierre de sesión, cambio de contraseña propia
/// y pantalla de acceso denegado. Es el único controlador con acciones anónimas.
/// </summary>
public class CuentaController : Controller
{
    private readonly IAutenticacionService _autenticacion;
    private readonly IUsuarioRepository _usuarios;
    private readonly ILogger<CuentaController> _logger;

    public CuentaController(
        IAutenticacionService autenticacion,
        IUsuarioRepository usuarios,
        ILogger<CuentaController> logger)
    {
        _autenticacion = autenticacion ?? throw new ArgumentNullException(nameof(autenticacion));
        _usuarios = usuarios ?? throw new ArgumentNullException(nameof(usuarios));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var resultado = await _autenticacion.AutenticarAsync(model.Usuario, model.Password, DateTime.UtcNow);

        if (!resultado.Exito)
        {
            _logger.LogWarning(
                "Intento de inicio de sesión fallido para '{Usuario}' desde {Ip}. Motivo: {Motivo}",
                model.Usuario,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                resultado.Motivo);

            // El mensaje proviene del dominio y nunca revela si el usuario existe.
            ModelState.AddModelError(string.Empty, resultado.Mensaje);
            model.Password = string.Empty;
            return View(model);
        }

        var usuario = resultado.Usuario!;
        await SesionUsuario.IniciarSesionAsync(HttpContext, usuario);

        _logger.LogInformation(
            "Inicio de sesión exitoso: '{Usuario}' (rol {Rol}, id {UsuarioId}).",
            usuario.NombreUsuario, usuario.Rol, usuario.Id);

        if (Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl!);

        if (usuario.DebeCambiarPassword)
            return RedirectToAction(nameof(CambiarPassword));

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var nombre = SesionUsuario.ObtenerNombre(User);
        await SesionUsuario.CerrarSesionAsync(HttpContext);
        _logger.LogInformation("Sesión finalizada para '{Usuario}'.", nombre);

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public async Task<IActionResult> CambiarPassword()
    {
        var usuario = await ObtenerUsuarioActualAsync();
        if (usuario is null)
            return RedirectToAction(nameof(Login));

        return View(new CambiarPasswordViewModel { EsObligatorio = usuario.DebeCambiarPassword });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarPassword(CambiarPasswordViewModel model)
    {
        var usuarioId = SesionUsuario.ObtenerUsuarioId(User);
        if (usuarioId is null)
            return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
            return View(model);

        var resultado = await _autenticacion.CambiarPasswordAsync(
            usuarioId.Value, model.PasswordActual, model.PasswordNueva, DateTime.UtcNow);

        if (!resultado.Exito)
        {
            _logger.LogWarning(
                "Cambio de contraseña rechazado para el usuario id {UsuarioId}.", usuarioId.Value);

            foreach (var error in resultado.Errores)
                ModelState.AddModelError(string.Empty, error);

            model.PasswordActual = string.Empty;
            model.PasswordNueva = string.Empty;
            model.ConfirmarPassword = string.Empty;
            model.EsObligatorio = (await ObtenerUsuarioActualAsync())?.DebeCambiarPassword ?? false;
            return View(model);
        }

        // Se renueva la cookie: la marca de cambio obligatorio ya no aplica.
        await SesionUsuario.IniciarSesionAsync(HttpContext, resultado.Usuario!);

        _logger.LogInformation(
            "Contraseña actualizada para '{Usuario}' (id {UsuarioId}).",
            resultado.Usuario!.NombreUsuario, resultado.Usuario.Id);

        TempData["Mensaje"] = "Contraseña actualizada correctamente.";
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult AccesoDenegado() => View();

    private async Task<Domain.Entities.Usuario?> ObtenerUsuarioActualAsync()
    {
        var usuarioId = SesionUsuario.ObtenerUsuarioId(User);
        if (usuarioId is null) return null;

        var usuario = await _usuarios.GetByIdAsync(usuarioId.Value);

        // Una cuenta desactivada pierde la sesión de inmediato.
        if (usuario is null || !usuario.EstaActivo)
        {
            await SesionUsuario.CerrarSesionAsync(HttpContext);
            return null;
        }

        return usuario;
    }
}
