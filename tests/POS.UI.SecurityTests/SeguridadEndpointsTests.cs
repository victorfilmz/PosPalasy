using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.Infrastructure.Persistence;

namespace POS.UI.SecurityTests;

/// <summary>
/// Pruebas de seguridad sobre el pipeline HTTP real: ningún endpoint protegido debe responder a
/// peticiones anónimas, los cambios de estado deben exigir token antiforgery y cada rol debe quedar
/// limitado a su matriz de permisos.
/// </summary>
public class SeguridadEndpointsTests : IClassFixture<PosAppFactory>
{
    private const string UsuarioCajero = "cajero-pruebas";

    private readonly PosAppFactory _app;

    public SeguridadEndpointsTests(PosAppFactory app)
    {
        _app = app;
    }

    // ---------------------------------------------------------------- autenticación

    [Theory]
    [InlineData("/")]
    [InlineData("/Dashboard")]
    [InlineData("/Pos")]
    [InlineData("/Caja")]
    [InlineData("/Inventario")]
    [InlineData("/Facturacion/Lista")]
    [InlineData("/Reportes/Ventas607")]
    [InlineData("/Reportes/ResumenItbis")]
    [InlineData("/Configuracion/Certificado")]
    [InlineData("/Configuracion/Empresa")]
    [InlineData("/Configuracion/FacturaFisica")]
    public async Task EndpointProtegido_SinSesion_RedirigeAlLogin(string ruta)
    {
        var cliente = _app.CrearCliente();

        var respuesta = await cliente.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Cuenta/Login", respuesta.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Login_SinTokenAntiforgery_Devuelve400()
    {
        var cliente = _app.CrearCliente();

        // Se envía únicamente el formulario, sin el token: la petición debe rechazarse (CSRF).
        var respuesta = await cliente.PostAsync("/Cuenta/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Usuario"] = PosAppFactory.UsuarioAdmin,
            ["Password"] = PosAppFactory.PasswordAdmin
        }));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_OtorgaAccesoYFirmaCookieSegura()
    {
        var cliente = _app.CrearCliente();

        var respuesta = await LoginAsync(cliente, PosAppFactory.UsuarioAdmin, PosAppFactory.PasswordAdmin);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);

        var setCookie = Assert.Single(respuesta.Headers.GetValues("Set-Cookie"), c => c.Contains("PosPalasy.Sesion"));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

        var protegido = await cliente.GetAsync("/Pos");
        Assert.Equal(HttpStatusCode.OK, protegido.StatusCode);
    }

    [Fact]
    public async Task Login_ConPasswordIncorrecta_NoOtorgaAcceso()
    {
        var cliente = _app.CrearCliente();

        var respuesta = await LoginAsync(cliente, PosAppFactory.UsuarioAdmin, "Password-Incorrecta-2026!");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(respuesta.Headers.Contains("Set-Cookie") &&
                     respuesta.Headers.GetValues("Set-Cookie").Any(c => c.Contains("PosPalasy.Sesion")));

        var protegido = await cliente.GetAsync("/Pos");
        Assert.Equal(HttpStatusCode.Redirect, protegido.StatusCode);
    }

    [Fact]
    public async Task Login_TrasSuperarIntentosFallidos_BloqueaLaCuenta()
    {
        // Usuario propio para que el bloqueo no afecte a las demás pruebas.
        var usuario = _app.CrearUsuario("cajero-bloqueo-pruebas", RolUsuario.Cajero);
        var cliente = _app.CrearCliente();

        for (var intento = 0; intento < 6; intento++)
            await LoginAsync(cliente, usuario, "Password-Incorrecta-2026!");

        var respuesta = await LoginAsync(cliente, usuario, PosAppFactory.PasswordAuxiliar);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode); // sigue en el formulario: bloqueado
        var contenido = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("bloqueada", contenido, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- autorización por rol

    [Fact]
    public async Task Cajero_NoAccedeALaConfiguracion()
    {
        var cliente = await CrearSesionAsync(_app.CrearUsuario(UsuarioCajero, RolUsuario.Cajero), PosAppFactory.PasswordAuxiliar);

        var respuesta = await cliente.GetAsync("/Configuracion/Empresa");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Cuenta/AccesoDenegado", respuesta.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Cajero_NoPuedeAnularComprobantes()
    {
        var cliente = await CrearSesionAsync(_app.CrearUsuario(UsuarioCajero, RolUsuario.Cajero), PosAppFactory.PasswordAuxiliar);
        var token = await ObtenerTokenAsync(cliente, "/Pos");

        var respuesta = await cliente.PostAsync("/Facturacion/Anular", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = "1",
            ["codigoMotivo"] = "5",
            ["motivo"] = "Intento de anulación sin permisos",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Cuenta/AccesoDenegado", respuesta.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Cajero_NoAccedeAlCambioDeAmbienteDgii()
    {
        var cliente = await CrearSesionAsync(_app.CrearUsuario(UsuarioCajero, RolUsuario.Cajero), PosAppFactory.PasswordAuxiliar);
        var token = await ObtenerTokenAsync(cliente, "/Pos");

        var respuesta = await cliente.PostAsync("/Configuracion/CambiarAmbiente", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ambiente"] = "Produccion",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Cuenta/AccesoDenegado", respuesta.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task SuperAdmin_AccedeALaConfiguracion()
    {
        var cliente = await CrearSesionAsync(PosAppFactory.UsuarioAdmin, PosAppFactory.PasswordAdmin);

        var respuesta = await cliente.GetAsync("/Configuracion/Certificado");

        Assert.True(
            respuesta.StatusCode == HttpStatusCode.OK,
            $"/Configuracion/Certificado devolvió {Describir(respuesta)} para el SuperAdmin autenticado.");
    }

    private static string Describir(HttpResponseMessage respuesta) =>
        $"{(int)respuesta.StatusCode} {respuesta.StatusCode}" +
        (respuesta.Headers.Location is null ? string.Empty : $" → {respuesta.Headers.Location}");

    [Fact]
    public async Task Contador_NoAccedeAlTerminalPos()
    {
        var cliente = await CrearSesionAsync(_app.CrearUsuario("contador-pruebas", RolUsuario.Contador), PosAppFactory.PasswordAuxiliar);

        var respuesta = await cliente.GetAsync("/Pos");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Cuenta/AccesoDenegado", respuesta.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Contador_AccedeALosReportesFiscales()
    {
        var cliente = await CrearSesionAsync(_app.CrearUsuario("contador-pruebas", RolUsuario.Contador), PosAppFactory.PasswordAuxiliar);

        var respuesta = await cliente.GetAsync("/Reportes/Ventas607");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    // ---------------------------------------------------------------- antiforgery en operaciones

    [Fact]
    public async Task ProcesarVenta_ConSesionYSinToken_Devuelve400YNoRegistraVenta()
    {
        var cliente = await CrearSesionAsync(PosAppFactory.UsuarioAdmin, PosAppFactory.PasswordAdmin);

        var ventasAntes = await ContarVentasAsync();

        var peticion = new HttpRequestMessage(HttpMethod.Post, "/Pos/ProcesarVenta")
        {
            Content = new StringContent(
                """{"tipoeCF":32,"metodoPago":1,"items":[{"productoId":1,"descripcion":"Prueba CSRF","cantidad":1,"precioUnitario":10}]}""",
                Encoding.UTF8, "application/json")
        };

        var respuesta = await cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(ventasAntes, await ContarVentasAsync());
    }

    [Fact]
    public async Task ProcesarVenta_SinSesion_NoEjecutaLaVenta()
    {
        var cliente = _app.CrearCliente();
        var ventasAntes = await ContarVentasAsync();

        var peticion = new HttpRequestMessage(HttpMethod.Post, "/Pos/ProcesarVenta")
        {
            Content = new StringContent(
                """{"tipoeCF":32,"metodoPago":1,"items":[{"productoId":1,"descripcion":"Prueba anónima","cantidad":1,"precioUnitario":10}]}""",
                Encoding.UTF8, "application/json")
        };

        var respuesta = await cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Cuenta/Login", respuesta.Headers.Location?.ToString() ?? string.Empty);
        Assert.Equal(ventasAntes, await ContarVentasAsync());
    }

    [Theory]
    [InlineData("/Caja/Apertura")]
    [InlineData("/Caja/Cierre")]
    [InlineData("/Caja/Movimiento")]
    [InlineData("/Inventario/Crear")]
    [InlineData("/Inventario/Editar")]
    [InlineData("/Inventario/Eliminar")]
    [InlineData("/Inventario/AjustarStock")]
    [InlineData("/Configuracion/GuardarEmpresa")]
    [InlineData("/Configuracion/GuardarFacturaFisica")]
    [InlineData("/Configuracion/CargarCertificado")]
    [InlineData("/Configuracion/CambiarAmbiente")]
    [InlineData("/Configuracion/ProbarConectividad")]
    [InlineData("/Facturacion/Emitir")]
    [InlineData("/Facturacion/ConsultarEstado")]
    [InlineData("/Facturacion/Reenviar")]
    [InlineData("/Facturacion/Anular")]
    [InlineData("/Cuenta/CambiarPassword")]
    public async Task OperacionDeEstado_ConSesionYSinTokenAntiforgery_Devuelve400(string ruta)
    {
        var cliente = await CrearSesionAsync(PosAppFactory.UsuarioAdmin, PosAppFactory.PasswordAdmin);

        var respuesta = await cliente.PostAsync(ruta, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = "1",
            ["motivo"] = "prueba",
            ["ambiente"] = "Produccion"
        }));

        Assert.True(
            respuesta.StatusCode == HttpStatusCode.BadRequest,
            $"{ruta} devolvió {(int)respuesta.StatusCode} en lugar de 400 al no enviar token antiforgery.");
    }

    [Fact]
    public async Task Logout_SinTokenAntiforgery_Devuelve400YNoCierraLaSesion()
    {
        var cliente = await CrearSesionAsync(PosAppFactory.UsuarioAdmin, PosAppFactory.PasswordAdmin);

        var respuesta = await cliente.PostAsync("/Cuenta/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/Pos")).StatusCode);
    }

    [Fact]
    public async Task CambioDePasswordObligatorio_RedirigeAntesDePermitirOperar()
    {
        // Usuario con la marca de cambio obligatorio (como la cuenta inicial con contraseña temporal).
        var usuario = _app.CrearUsuarioConCambioObligatorio("usuario-cambio-obligatorio");

        var cliente = await CrearSesionAsync(usuario, PosAppFactory.PasswordAuxiliar);

        var respuesta = await cliente.GetAsync("/Pos");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Cuenta/CambiarPassword", respuesta.Headers.Location?.ToString() ?? string.Empty);
    }

    // ---------------------------------------------------------------- utilidades

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient cliente, string usuario, string password)
    {
        var html = await cliente.GetStringAsync("/Cuenta/Login");
        var token = PosAppFactory.ExtraerTokenAntiforgery(html);

        return await cliente.PostAsync("/Cuenta/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Usuario"] = usuario,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
    }

    private async Task<HttpClient> CrearSesionAsync(string usuario, string password)
    {
        var cliente = _app.CrearCliente();
        await LoginAsync(cliente, usuario, password);
        return cliente;
    }

    private async Task<string> ObtenerTokenAsync(HttpClient cliente, string ruta)
    {
        var html = await cliente.GetStringAsync(ruta);
        return PosAppFactory.ExtraerTokenAntiforgery(html);
    }

    private async Task<int> ContarVentasAsync()
    {
        // Misma instancia en memoria que usa la aplicación bajo prueba.
        using var scope = _app.Services.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<POSDbContext>();
        return await contexto.Ventas.CountAsync();
    }
}
