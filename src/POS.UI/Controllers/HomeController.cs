using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.UI.Models;

namespace POS.UI.Controllers;

/// <summary>
/// Páginas públicas del sitio (portada informativa y manejo de errores).
/// Es el único controlador, junto con el inicio de sesión, accesible sin autenticación.
/// </summary>
[AllowAnonymous]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        // Nunca se expone el detalle técnico de la excepción al usuario final.
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
