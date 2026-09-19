using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using POS.Infrastructure.Services;
using Xunit;

namespace POS.UI.SecurityTests;

/// <summary>
/// E2E de la pantalla Configuración → Certificado: el usuario debe ver el diagnóstico operativo del
/// MISMO proveedor que usa la firma de comprobantes (¿puede firmar?) ANTES de vender en modo real.
/// </summary>
/// <remarks>
/// Usa una fábrica derivada con <c>Certificado:DirectorioDatos</c> aislado en una carpeta temporal,
/// de modo que el estado inicial ("sin certificado") sea determinista y el test pueda instalar un
/// certificado válido de prueba sin tocar los datos reales de la máquina.
/// </remarks>
public class ConfiguracionCertificadoEndToEndTests : IClassFixture<CertificadoAppFactory>
{
    private readonly CertificadoAppFactory _app;

    public ConfiguracionCertificadoEndToEndTests(CertificadoAppFactory app) => _app = app;

    private async Task<HttpClient> SesionSuperAdminAsync()
    {
        var cliente = _app.CrearCliente();

        var html = await cliente.GetStringAsync("/Cuenta/Login");
        var token = PosAppFactory.ExtraerTokenAntiforgery(html);

        var respuesta = await cliente.PostAsync(
            "/Cuenta/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Usuario"] = PosAppFactory.UsuarioAdmin,
                ["Password"] = PosAppFactory.PasswordAdmin,
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return cliente;
    }

    [Fact]
    public async Task SinCertificado_LaPantallaDiceQueNoPuedeFirmarConElDiagnosticoDelProveedor()
    {
        var cliente = await SesionSuperAdminAsync();
        var html = await cliente.GetStringAsync("/Configuracion/Certificado");

        Assert.Contains("NO puede firmar", html);
        // El diagnóstico es el del proveedor real (misma resolución de rutas que la firma).
        Assert.Contains("No hay certificado digital", html);
    }

    [Fact]
    public async Task ConCertificadoValido_LaPantallaConfirmaQuePuedeFirmar()
    {
        var ruta = _app.InstalarCertificadoDePrueba();
        try
        {
            var cliente = await SesionSuperAdminAsync();
            var html = await cliente.GetStringAsync("/Configuracion/Certificado");

            Assert.Contains("puede firmar comprobantes", html);
            // El diagnóstico muestra el sujeto del certificado instalado.
            Assert.Contains("PosPalasy Emisor E2E", html);
        }
        finally
        {
            _app.QuitarCertificado(ruta);
        }
    }
}

/// <summary>
/// Fábrica E2E con directorio de certificados aislado y contraseña de prueba: el diagnóstico del
/// proveedor de firma es reproducible (arranca sin certificado; el test instala el que necesita).
/// </summary>
public class CertificadoAppFactory : PosAppFactory
{
    private const string PasswordPrueba = "clave-e2e";

    public string DirectorioCertificados { get; } =
        Path.Combine(Path.GetTempPath(), "pospalasy-cert-e2e-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, configuracion) =>
        {
            configuracion.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Certificado:DirectorioDatos"] = DirectorioCertificados,
                ["Certificado:RutaCertificado"] = "emisor.pfx",
                ["Certificado:Password"] = PasswordPrueba
            });
        });
    }

    /// <summary>Instala un certificado .pfx válido de prueba en el directorio aislado.</summary>
    public string InstalarCertificadoDePrueba()
    {
        Directory.CreateDirectory(DirectorioCertificados);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=PosPalasy Emisor E2E", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var ruta = Path.Combine(DirectorioCertificados, "emisor.pfx");
        File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pkcs12, PasswordPrueba));
        return ruta;
    }

    public void QuitarCertificado(string ruta) => File.Delete(ruta);
}

/// <summary>
/// Fábrica E2E donde la contraseña configurada NO coincide con la del certificado instalado:
/// reproduce el error operativo más común (contraseña del .pfx mal cargada en user-secrets).
/// </summary>
public class CertificadoPasswordErroneaFactory : CertificadoAppFactory
{
    private const string PasswordIncorrecta = "esta-no-es-la-clave-del-pfx";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Se añade DESPUÉS de la configuración de la base: esta fuente gana y la app intentará
        // abrir el .pfx con una contraseña que no es la suya.
        builder.ConfigureAppConfiguration((_, configuracion) =>
        {
            configuracion.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Certificado:Password"] = PasswordIncorrecta
            });
        });
    }
}

/// <summary>
/// E2E del caso borde más común de certificado: el archivo existe pero la contraseña configurada
/// es errónea. El operador debe ver ANTES de vender que el sistema NO puede firmar y cuál es la
/// causa exacta — no un aviso engañoso como "Certificado Expirado".
/// </summary>
public class ConfiguracionCertificadoPasswordErroneaTests : IClassFixture<CertificadoPasswordErroneaFactory>
{
    private readonly CertificadoPasswordErroneaFactory _app;

    public ConfiguracionCertificadoPasswordErroneaTests(CertificadoPasswordErroneaFactory app) => _app = app;

    private async Task<HttpClient> SesionSuperAdminAsync()
    {
        var cliente = _app.CrearCliente();

        var html = await cliente.GetStringAsync("/Cuenta/Login");
        var token = PosAppFactory.ExtraerTokenAntiforgery(html);

        var respuesta = await cliente.PostAsync(
            "/Cuenta/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Usuario"] = PosAppFactory.UsuarioAdmin,
                ["Password"] = PosAppFactory.PasswordAdmin,
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return cliente;
    }

    [Fact]
    public async Task ConPasswordErronea_LaPantallaMuestraQueNoPuedeFirmarYLaCausa()
    {
        // El .pfx se exporta con SU contraseña correcta; la app está configurada con otra.
        var ruta = _app.InstalarCertificadoDePrueba();
        try
        {
            var cliente = await SesionSuperAdminAsync();
            var html = await cliente.GetStringAsync("/Configuracion/Certificado");

            // El aviso operativo es que NO puede firmar, con la causa exacta (contraseña).
            Assert.Contains("El sistema NO puede firmar comprobantes.", html);
            Assert.Contains("contraseña", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No Puede Firmar", html);   // badge del encabezado

            // Y la pantalla NO confirma la capacidad de firma.
            Assert.DoesNotContain("El sistema puede firmar comprobantes", html);
        }
        finally
        {
            _app.QuitarCertificado(ruta);
        }
    }
}
