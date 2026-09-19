using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging.Abstractions;
using POS.Domain.Common;
using POS.Infrastructure.DGII;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;
using Xunit;

namespace POS.Domain.Types.Tests;

/// <summary>
/// Handler HTTP falso que simula los endpoints de la DGII sin red: semilla (XML), validarsemilla
/// (token) y un endpoint de negocio que exige <c>Authorization: Bearer</c> y puede programarse
/// para responder 401 las primeras N veces (prueba de renovación y reintento).
/// </summary>
internal sealed class HandlerDgiiFalso : HttpMessageHandler
{
    public int SemillasEmitidas { get; private set; }

    public string? UltimaSemillaFirmadaRecibida { get; private set; }

    /// <summary>Invocaciones autenticadas al endpoint de negocio (con token y sin 401 programado).</summary>
    public int PeticionesAutenticadas { get; private set; }

    /// <summary>Número de 401 que el endpoint de negocio responde antes de aceptar.</summary>
    public int Fallos401Pendientes { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;

        if (url.Contains("autenticacion/semilla", StringComparison.OrdinalIgnoreCase))
        {
            SemillasEmitidas++;
            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><SemillaModel>" +
                      "<valor>" + Guid.NewGuid().ToString("D") + "</valor>" +
                      "<fecha>2026-09-19T10:00:00.0000000-04:00</fecha></SemillaModel>";
            return RespuestaXml(HttpStatusCode.OK, xml);
        }
        if (url.Contains("validarsemilla", StringComparison.OrdinalIgnoreCase))
        {
            UltimaSemillaFirmadaRecibida = await ExtraerSemillaDelMultipartAsync(request);
            return RespuestaJson(HttpStatusCode.OK,
                "{\"token\":\"TOKEN-DE-PRUEBA-" + SemillasEmitidas + "\"," +
                "\"expira\":\"2026-09-19T21:30:00Z\",\"expedido\":\"2026-09-19T20:30:00Z\"}");
        }

        // Endpoint de negocio: sin Bearer la DGII responde 401.
        var auth = request.Headers.Authorization;
        if (auth == null || auth.Scheme != "Bearer" || string.IsNullOrWhiteSpace(auth.Parameter))
            return RespuestaJson(HttpStatusCode.Unauthorized, "{\"error\":\"sin token\"}");

        if (Fallos401Pendientes > 0)
        {
            Fallos401Pendientes--;
            return RespuestaJson(HttpStatusCode.Unauthorized, "{\"error\":\"token vencido\"}");
        }

        PeticionesAutenticadas++;
        return RespuestaJson(HttpStatusCode.OK, "{\"trackId\":\"TR-1\",\"estado\":\"EnProceso\"}");
    }

    private static HttpResponseMessage RespuestaXml(HttpStatusCode codigo, string cuerpo) =>
        new(codigo)
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/xml")
        };

    private static HttpResponseMessage RespuestaJson(HttpStatusCode codigo, string cuerpo) =>
        new(codigo)
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
        };

    /// <summary>Extrae el XML de semilla del cuerpo multipart sin dependencias de formatting.</summary>
    private static async Task<string> ExtraerSemillaDelMultipartAsync(HttpRequestMessage request)
    {
        var crudo = await request.Content!.ReadAsStringAsync();

        var inicio = crudo.IndexOf("<?xml", StringComparison.Ordinal);
        if (inicio < 0)
            inicio = crudo.IndexOf("<SemillaModel", StringComparison.Ordinal);

        var fin = crudo.LastIndexOf("</SemillaModel>", StringComparison.Ordinal);

        return inicio >= 0 && fin > inicio
            ? crudo[inicio..(fin + "</SemillaModel>".Length)]
            : crudo;
    }
}

/// <summary>
/// Pruebas del autenticador DGII (FASE 5, sub-fase 5.2): la semilla se firma con el certificado del
/// emisor y la firma es verificable, el token se cachea dentro de su ventana y el cliente de envío
/// renueva UNA vez ante 401/403 y reintenta.
/// </summary>
public class AutenticacionDgiiTests
{
    private const string Password = "clave-auth";

    private static string CrearPfxTemporal()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=Auth DGII Pruebas", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var ruta = Path.Combine(Path.GetTempPath(), $"pospalasy-auth-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pkcs12, Password));
        return ruta;
    }

    private static (DgiiAuthenticator Autenticador, HandlerDgiiFalso Handler, string RutaPfx) CrearAutenticador()
    {
        var handler = new HandlerDgiiFalso();
        var rutaPfx = CrearPfxTemporal();

        var config = new DgiiConfig
        {
            BaseUrl = "https://dgii.test",
            AutenticacionEndpoint = "/autenticacion/api",
            ModoSimulador = false
        };

        var autenticador = new DgiiAuthenticator(
            new HttpClient(handler),
            config,
            new ProveedorCertificadoDigital(rutaPfx, Password),
            new FirmadorComprobanteECF(new XmlDigitalSigner()),
            NullLogger<DgiiAuthenticator>.Instance);

        return (autenticador, handler, rutaPfx);
    }

    [Fact]
    public async Task FlujoCompleto_ObtieneTokenYLaSemillaFirmadaVerificaCriptograficamente()
    {
        var (autenticador, handler, rutaPfx) = CrearAutenticador();
        try
        {
            var token = await autenticador.ObtenerTokenAsync();

            Assert.Equal("TOKEN-DE-PRUEBA-1", token);
            Assert.Equal(1, handler.SemillasEmitidas);
            Assert.False(string.IsNullOrWhiteSpace(handler.UltimaSemillaFirmadaRecibida));

            // La DGII verificará la firma de la semilla con el certificado del emisor: aquí se
            // comprueba exactamente eso sobre lo que el autenticador envió.
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(handler.UltimaSemillaFirmadaRecibida!);

            var nodo = doc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
            Assert.Equal(1, nodo.Count);

            var signedXml = new SignedXml(doc);
            signedXml.LoadXml((XmlElement)nodo[0]!);

            using var cert = X509CertificateLoader.LoadPkcs12FromFile(rutaPfx, Password);
            Assert.True(signedXml.CheckSignature(cert, true), "La semilla firmada debe verificar con el certificado del emisor.");
        }
        finally
        {
            File.Delete(rutaPfx);
        }
    }

    [Fact]
    public async Task TokenSeReutilizaDentroDeSuVentana_SinNuevasAutenticaciones()
    {
        var (autenticador, handler, rutaPfx) = CrearAutenticador();
        try
        {
            var primero = await autenticador.ObtenerTokenAsync();
            var segundo = await autenticador.ObtenerTokenAsync();

            Assert.Equal(primero, segundo);
            Assert.Equal(1, handler.SemillasEmitidas);   // una sola autenticación para ambos usos
        }
        finally
        {
            File.Delete(rutaPfx);
        }
    }

    [Fact]
    public async Task RenovarToken_FuerzaNuevaAutenticacion()
    {
        var (autenticador, handler, rutaPfx) = CrearAutenticador();
        try
        {
            var primero = await autenticador.ObtenerTokenAsync();
            var renovado = await autenticador.RenovarTokenAsync();

            Assert.NotEqual(primero, renovado);
            Assert.Equal(2, handler.SemillasEmitidas);
        }
        finally
        {
            File.Delete(rutaPfx);
        }
    }

    [Fact]
    public async Task SinCertificado_LaAutenticacionFallaConErrorClaro()
    {
        var handler = new HandlerDgiiFalso();
        var autenticador = new DgiiAuthenticator(
            new HttpClient(handler),
            new DgiiConfig { BaseUrl = "https://dgii.test", AutenticacionEndpoint = "/autenticacion/api" },
            new ProveedorCertificadoDigital(
                Path.Combine(Path.GetTempPath(), "pospalasy-no-existe", "emisor.pfx"), null),
            new FirmadorComprobanteECF(new XmlDigitalSigner()),
            NullLogger<DgiiAuthenticator>.Instance);

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => autenticador.ObtenerTokenAsync());
        Assert.Equal("CERTIFICADO_AUSENTE", ex.Codigo);
        Assert.Equal(0, handler.SemillasEmitidas);   // ni siquiera pidió semilla
    }

    [Fact]
    public async Task ClienteDgii_RenuevaElTokenUnaVezYReintentaAnte401()
    {
        var (autenticador, handler, rutaPfx) = CrearAutenticador();
        try
        {
            // Primer uso: deja el token cacheado. La llamada de negocio responderá 401 UNA vez.
            await autenticador.ObtenerTokenAsync();
            handler.Fallos401Pendientes = 1;

            var config = new DgiiConfig
            {
                BaseUrl = "https://dgii.test",
                RecepcionEndpoint = "/recepcion/ecf",
                ModoSimulador = false
            };
            var cliente = new DgiiApiClient(
                new HttpClient(handler), config,
                NullLogger<DgiiApiClient>.Instance, autenticador);

            var respuesta = await cliente.EnviarFacturaAsync("eGFwby4=", "A7B3C9");

            // El 401 disparó renovación + reintento: la petición terminó autenticada y exitosa.
            Assert.True(respuesta.EsExitoso, respuesta.Mensaje);
            Assert.Equal(200, respuesta.CodigoHttp);
            Assert.Equal(2, handler.SemillasEmitidas);      // token inicial + renovación
            Assert.Equal(1, handler.PeticionesAutenticadas); // el reintento pasó
        }
        finally
        {
            File.Delete(rutaPfx);
        }
    }
}
