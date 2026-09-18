using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using POS.Domain.Entities;
using POS.Domain.Enums;

namespace POS.UI.Security;

/// <summary>
/// Construcción de la identidad de sesión (claims) y utilidades de inicio/cierre de sesión.
/// Centraliza qué información del usuario viaja en la cookie para que no se repita en los controladores.
/// </summary>
public static class SesionUsuario
{
    /// <summary>Claim que marca la obligación de cambiar la contraseña antes de operar.</summary>
    public const string ClaimCambioPasswordObligatorio = "cambio_password_obligatorio";

    public const string ValorVerdadero = "1";

    /// <summary>Crea el principal de sesión a partir del usuario autenticado.</summary>
    public static ClaimsPrincipal CrearPrincipal(Usuario usuario)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.NombreUsuario),
            new(ClaimTypes.GivenName, usuario.NombreCompleto),
            new(ClaimTypes.Role, Roles.NombreDe(usuario.Rol)),
            new("rol_id", ((int)usuario.Rol).ToString())
        };

        if (usuario.DebeCambiarPassword)
            claims.Add(new Claim(ClaimCambioPasswordObligatorio, ValorVerdadero));

        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identidad);
    }

    /// <summary>Inicia sesión emitiendo la cookie de autenticación.</summary>
    public static async Task IniciarSesionAsync(HttpContext http, Usuario usuario)
    {
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CrearPrincipal(usuario),
            new AuthenticationProperties
            {
                IsPersistent = false,
                IssuedUtc = DateTimeOffset.UtcNow
            });
    }

    /// <summary>Cierra la sesión eliminando la cookie de autenticación.</summary>
    public static async Task CerrarSesionAsync(HttpContext http) =>
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>Identificador del usuario autenticado, o null si no hay sesión válida.</summary>
    public static int? ObtenerUsuarioId(ClaimsPrincipal principal)
    {
        var valor = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(valor, out var id) ? id : null;
    }

    /// <summary>Nombre del usuario autenticado.</summary>
    public static string ObtenerNombre(ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    /// <summary>Nombre completo del usuario autenticado.</summary>
    public static string ObtenerNombreCompleto(ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.GivenName)?.Value
        ?? principal.FindFirst(ClaimTypes.Name)?.Value
        ?? string.Empty;

    /// <summary>Rol del usuario autenticado.</summary>
    public static string ObtenerRol(ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
}
