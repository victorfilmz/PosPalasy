using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace POS.UI.Security;

/// <summary>
/// Obliga a cambiar la contraseña antes de usar el sistema cuando la sesión tiene marcada esa
/// condición (cuentas creadas con contraseña temporal generada). Sin esta barrera, una contraseña
/// inicial conocida podría seguir en uso indefinidamente.
/// </summary>
public class CambioPasswordObligatorioMiddleware
{
    private static readonly string[] RutasPermitidas =
    {
        "/Cuenta/CambiarPassword",
        "/Cuenta/Login",
        "/Cuenta/Logout",
        "/Cuenta/AccesoDenegado"
    };

    private readonly RequestDelegate _next;

    public CambioPasswordObligatorioMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (DebeRedirigir(context))
        {
            // 302 a la pantalla de cambio; no se permite ninguna operación antes de cambiarla.
            context.Response.Redirect("/Cuenta/CambiarPassword");
            return;
        }

        await _next(context);
    }

    private static bool DebeRedirigir(HttpContext context)
    {
        var usuario = context.User;
        if (usuario?.Identity?.IsAuthenticated != true) return false;

        var obligatorio = usuario.FindFirst(SesionUsuario.ClaimCambioPasswordObligatorio)?.Value;
        if (!string.Equals(obligatorio, SesionUsuario.ValorVerdadero, StringComparison.Ordinal)) return false;

        var ruta = context.Request.Path.Value ?? string.Empty;

        if (RutasPermitidas.Any(r => ruta.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Recursos estáticos (nombres con extensión) nunca se redirigen.
        if (ruta.Contains('.', StringComparison.Ordinal))
            return false;

        return true;
    }
}
