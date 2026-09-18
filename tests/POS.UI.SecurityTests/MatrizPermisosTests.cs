using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Enums;
using POS.UI.Controllers;
using POS.UI.Security;

namespace POS.UI.SecurityTests;

/// <summary>
/// Documenta y protege la matriz de permisos frente a regresiones: si alguien retira un
/// atributo de autorización de un controlador o cambia los roles de una política, estas pruebas fallan.
/// </summary>
public class MatrizPermisosTests
{
    /// <summary>Controladores con acciones públicas por diseño (login y páginas informativas).</summary>
    private static readonly Type[] ControladoresPublicos =
    {
        typeof(CuentaController),
        typeof(HomeController)
    };

    public static IEnumerable<object[]> PoliticasEsperadas => new[]
    {
        new object[] { Politicas.CambioAmbiente, new[] { Roles.SuperAdmin } },
        new object[] { Politicas.GestionUsuarios, new[] { Roles.SuperAdmin } },
        new object[] { Politicas.Configuracion, new[] { Roles.SuperAdmin, Roles.Administrador } },
        new object[] { Politicas.Supervision, new[] { Roles.SuperAdmin, Roles.Administrador, Roles.Supervisor } },
        new object[] { Politicas.OperacionPos, new[] { Roles.SuperAdmin, Roles.Administrador, Roles.Supervisor, Roles.Cajero } },
        new object[] { Politicas.ReportesFiscales, new[] { Roles.SuperAdmin, Roles.Administrador, Roles.Supervisor, Roles.Contador } }
    };

    [Theory]
    [MemberData(nameof(PoliticasEsperadas))]
    public void Politica_ContieneExactamenteLosRolesDefinidos(string politica, string[] rolesEsperados)
    {
        Assert.True(Politicas.Matriz.TryGetValue(politica, out var roles), $"La política {politica} no está registrada.");

        Assert.Equal(
            rolesEsperados.OrderBy(r => r),
            roles!.OrderBy(r => r));
    }

    [Fact]
    public void RolesDeOperacion_NoIncluyenAlContador()
    {
        Assert.DoesNotContain(Roles.Contador, Politicas.Matriz[Politicas.OperacionPos]);
    }

    [Fact]
    public void CambioDeAmbienteDgii_QuedaReservadoAlSuperAdmin()
    {
        // Operación de mayor impacto: cambia el entorno fiscal de todos los usuarios.
        Assert.Equal(new[] { Roles.SuperAdmin }, Politicas.Matriz[Politicas.CambioAmbiente]);
    }

    [Fact]
    public void TodosLosControladoresExigenAutorizacionSalvoLosPublicos()
    {
        var controladores = typeof(PosController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var sinProteccion = new List<string>();

        foreach (var controlador in controladores)
        {
            var esPublico = ControladoresPublicos.Contains(controlador) ||
                            controlador.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;

            if (esPublico) continue;

            var tieneAutorizacion =
                controlador.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null ||
                controlador.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Any(m => m.GetCustomAttribute<AuthorizeAttribute>() is not null);

            if (!tieneAutorizacion)
                sinProteccion.Add(controlador.Name);
        }

        Assert.True(
            sinProteccion.Count == 0,
            $"Controladores sin atributo de autorización: {string.Join(", ", sinProteccion)}");
    }

    [Fact]
    public void AccionesSensibles_DeclaranLaPoliticaEsperada()
    {
        Assert.Equal(Politicas.CambioAmbiente, PoliticaDe<ConfiguracionController>(nameof(ConfiguracionController.CambiarAmbiente), 1));
        Assert.Equal(Politicas.CambioAmbiente, PoliticaDe<ConfiguracionController>(nameof(ConfiguracionController.ProbarConectividad), 0));
        Assert.Equal(Politicas.Configuracion, PoliticaDe<ConfiguracionController>(nameof(ConfiguracionController.CargarCertificado), 2));
        Assert.Equal(Politicas.Configuracion, PoliticaDe<ConfiguracionController>(nameof(ConfiguracionController.GuardarEmpresa), 1));

        Assert.Equal(Politicas.Supervision, PoliticaDe<FacturacionController>(nameof(FacturacionController.Anular), 3));
        Assert.Equal(Politicas.Supervision, PoliticaDe<FacturacionController>(nameof(FacturacionController.Reenviar), 1));

        Assert.Equal(Politicas.Configuracion, PoliticaDe<InventarioController>(nameof(InventarioController.Crear), 1));
        Assert.Equal(Politicas.Supervision, PoliticaDe<InventarioController>(nameof(InventarioController.AjustarStock), 1));

        Assert.Equal(Politicas.OperacionPos, typeof(PosController).GetCustomAttribute<AuthorizeAttribute>(inherit: true)?.Policy);
        Assert.Equal(Politicas.ReportesFiscales, typeof(ReportesController).GetCustomAttribute<AuthorizeAttribute>(inherit: true)?.Policy);
    }

    [Fact]
    public void PantallaDeLogin_EsLaUnicaAccionAnonimaDelControladorDeCuenta()
    {
        var anonimas = typeof(CuentaController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(new[] { nameof(CuentaController.Login) }, anonimas);
    }

    [Fact]
    public void ControladoresSensibles_EstanEnLaListaDeAuditoriaDeSeguridad()
    {
        // Guarda contra la reintroducción de controladores administrativos sin política:
        // todo controlador nuevo debe entrar aquí de forma consciente.
        var esperados = new[]
        {
            nameof(CajaController), nameof(ConfiguracionController), nameof(DashboardController),
            nameof(FacturacionController), nameof(InventarioController), nameof(PosController),
            nameof(ReportesController)
        };

        var encontrados = typeof(PosController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.Name)
            .Where(n => !ControladoresPublicos.Any(p => p.Name == n))
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(esperados.OrderBy(n => n), encontrados);
    }

    private static string? PoliticaDe<TControlador>(string accion, int cantidadParametros)
    {
        var metodo = typeof(TControlador)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SingleOrDefault(m => m.Name == accion && m.GetParameters().Length == cantidadParametros);

        Assert.NotNull(metodo);

        // Política efectiva: la del atributo propio si existe y, si no, la del controlador
        // (comportamiento real de MVC: los atributos de clase y de acción se combinan).
        return metodo!.GetCustomAttribute<AuthorizeAttribute>()?.Policy
            ?? typeof(TControlador).GetCustomAttribute<AuthorizeAttribute>(inherit: true)?.Policy;
    }
}
