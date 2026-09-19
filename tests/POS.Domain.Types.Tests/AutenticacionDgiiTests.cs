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

    /// <summary>Última URL de negocio invocada (recepción, consulta, etc.).</summary>
    public string? UltimaUrlDeNegocio { get; private set; }

    /// <summary>Content-Type de la última petición de negocio (debe ser multipart/form-data en recepción).</summary>
    public string? UltimoContentTypeDeNegocio { get; private set; }

    /// <summary>Nombre de archivo contenido en la parte multipart de la última recepción.</summary>
    public string? UltimoNombreArchivoXml { get; private set; }

    /// <summary>Cuerpo JSON que responde el endpoint de negocio (configurable por prueba).</summary>
    public string CuerpoDeNegocio { get; set; } = "{\"trackId\":\"TR-1\",\"estado\":\"EnProceso\"}";

    /// <summary>Cuando está activo, toda petición lanza HttpRequestException simulando caída de red (envío incierto).</summary>
    public bool CaidaDeRed { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (CaidaDeRed)
            throw new HttpRequestException("No se pudo conectar con el servidor remoto.");

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
        UltimaUrlDeNegocio = url;
        UltimoContentTypeDeNegocio = request.Content?.Headers?.ContentType?.ToString();
        UltimoNombreArchivoXml = ExtraerNombreArchivoDelMultipart(request);
        return RespuestaJson(HttpStatusCode.OK, CuerpoDeNegocio);
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

    /// <summary>Extrae el nombre de archivo de la parte multipart sin dependencias de formatting.</summary>
    private static string? ExtraerNombreArchivoDelMultipart(HttpRequestMessage request)
    {
        var disposicion = request.Content?.Headers?.ContentType?.ToString();
        if (disposicion == null) return null;

        var crudo = request.Content!.Headers.ContentType!.ToString();
        // El filename viaja en cada parte; se busca en el encabezado de contenido del cuerpo.
        // Lectura síncrona del cuerpo para pruebas: el multipart es pequeño.
        var cuerpo = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var marca = "filename=";
        var idx = cuerpo.IndexOf(marca, StringComparison.Ordinal);
        if (idx < 0) return null;

        var resto = cuerpo[(idx + marca.Length)..].TrimStart('"');
        var fin = resto.IndexOf('"');
        return fin > 0 ? resto[..fin] : resto.Split(';')[0].Trim();
    }

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

    /// <summary>Autenticador de prueba: expone el contrato de doble token (uno por host).</summary>
    internal sealed class AutenticadorFalso : IDgiiAuthenticator
    {
        public Task<string> ObtenerTokenAsync(CancellationToken ct = default) => Task.FromResult("TOKEN-ECF");
        public Task<string> ObtenerTokenRFCEAsync(CancellationToken ct = default) => Task.FromResult("TOKEN-RFCE");
        public Task<string> RenovarTokenAsync(CancellationToken ct = default) => Task.FromResult("TOKEN-NUEVO");
    }

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

        var config = new DgiiConfig { Ambiente = AmbienteDgii.TestECF, ModoSimulador = false };

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
            // La renovación re-autentica AMBOS hosts (e-CF y RFCE): sus tokens no son intercambiables.
            Assert.Equal(3, handler.SemillasEmitidas);
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
            new DgiiConfig { Ambiente = AmbienteDgii.TestECF },
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

            var config = new DgiiConfig { Ambiente = AmbienteDgii.TestECF, ModoSimulador = false };
            var cliente = new DgiiApiClient(
                new HttpClient(handler), config,
                NullLogger<DgiiApiClient>.Instance, autenticador);

            var respuesta = await cliente.EnviarFacturaAsync(
                "<eCF/>", "13100000001E320000000001.xml", esFacturaConsumo: true, montoTotal: 100m);

            // El 401 disparó renovación + reintento: la petición terminó autenticada y exitosa.
            Assert.True(respuesta.EsExitoso, respuesta.Mensaje);
            Assert.Equal(200, respuesta.CodigoHttp);
            // Semillas: 1 (token e-CF inicial) + 1 (el envío RFCE autentica SU host) + 2 (renovación
            // de ambos hosts tras el 401). El token de e-CF y el de RFCE son independientes.
            Assert.Equal(4, handler.SemillasEmitidas);
            Assert.Equal(1, handler.PeticionesAutenticadas); // el reintento pasó
        }
        finally
        {
            File.Delete(rutaPfx);
        }
    }
}

/// <summary>
/// Pruebas de transmisión RFCE/e-CF (FASE 5, sub-fase 5.3): multipart con el nombre de archivo
/// oficial RNC+eNCF.xml, regla de los RD$250,000, resultado fiscal completo y envío incierto.
/// </summary>
public class TransmisionComprobantesTests
{
    private static (DgiiApiClient Cliente, HandlerDgiiFalso Handler) CrearCliente(bool simulador = false)
    {
        var handler = new HandlerDgiiFalso();
        var config = new DgiiConfig { Ambiente = AmbienteDgii.TestECF, ModoSimulador = simulador };
        return (new DgiiApiClient(new HttpClient(handler), config, NullLogger<DgiiApiClient>.Instance, new AutenticacionDgiiTests.AutenticadorFalso()), handler);
    }

    [Fact]
    public async Task Regla250k_ConsumoMenorVaAlHostRFCE_YConsumoMayorAlHostECF()
    {
        var (cliente, handler) = CrearCliente();

        // Factura de consumo menor: RFCE (fc.dgii.gov.do).
        await cliente.EnviarFacturaAsync("<eCF/>", "13100000001E320000000001.xml", true, 100_000m);
        Assert.Contains("fc.dgii.gov.do/testecf/recepcionfc/api/recepcion/ecf", handler.UltimaUrlDeNegocio);

        // Factura de consumo que alcanza el umbral: e-CF completo (ecf.dgii.gov.do).
        await cliente.EnviarFacturaAsync("<eCF/>", "13100000001E320000000002.xml", true, 250_000m);
        Assert.Contains("ecf.dgii.gov.do/testecf/recepcion/api/facturaselectronicas", handler.UltimaUrlDeNegocio);

        // Otro tipo de comprobante (crédito fiscal) aunque sea pequeño: SIEMPRE e-CF completo.
        await cliente.EnviarFacturaAsync("<eCF/>", "13100000001E310000000003.xml", false, 5_000m);
        Assert.Contains("ecf.dgii.gov.do/testecf/recepcion/api/facturaselectronicas", handler.UltimaUrlDeNegocio);
    }

    [Fact]
    public async Task Multipart_LlevaElNombreOficialRncMasEncf()
    {
        var (cliente, handler) = CrearCliente();

        await cliente.EnviarFacturaAsync("<eCF/>", "13100000001E310000000001.xml", false, 10m);

        Assert.Equal("multipart/form-data", handler.UltimoContentTypeDeNegocio!.Split(';')[0]);
        Assert.Equal("13100000001E310000000001.xml", handler.UltimoNombreArchivoXml);
    }

    [Fact]
    public async Task ResultadoRFCE_MapeaEstadoCodigoMensajesYSecuencia()
    {
        var (cliente, handler) = CrearCliente();
        handler.CuerpoDeNegocio = "{\"codigo\":2,\"estado\":\"Aceptado Condicional\"," +
            "\"mensajes\":[{\"codigo\":\"1\",\"valor\":\"El RNC del comprador no existe\"}]," +
            "\"encf\":\"E320000000001\",\"secuenciaUtilizada\":true}";

        var respuesta = await cliente.EnviarFacturaAsync("<eCF/>", "13100000001E320000000001.xml", true, 100m);

        Assert.True(respuesta.EsExitoso);
        Assert.Equal("E320000000001", respuesta.eNCF);
        Assert.Equal("Aceptado Condicional", respuesta.Estado);
        Assert.True(respuesta.SecuenciaUtilizada);
        Assert.Contains("El RNC del comprador no existe", respuesta.Mensaje);
    }

    [Fact]
    public async Task ResultadoRFCE_RechazadoConSecuenciaReutilizable()
    {
        var (cliente, handler) = CrearCliente();
        handler.CuerpoDeNegocio = "{\"codigo\":3,\"estado\":\"Rechazado\"," +
            "\"mensajes\":[{\"codigo\":\"9\",\"valor\":\"Error en la firma\"}],\"secuenciaUtilizada\":false}";

        var respuesta = await cliente.EnviarFacturaAsync("<eCF/>", "13100000001E320000000001.xml", true, 100m);

        Assert.True(respuesta.EsExitoso);              // la RECEPCIÓN fue exitosa; el resultado es Rechazado
        Assert.Equal("Rechazado", respuesta.Estado);
        Assert.False(respuesta.SecuenciaUtilizada);    // la secuencia puede reutilizarse
    }

    [Fact]
    public async Task ConsultaResultadoECF_UsaParametroTrackidYTraeSecuencia()
    {
        var (cliente, handler) = CrearCliente();
        handler.CuerpoDeNegocio = "{\"trackId\":\"TR-9\",\"codigo\":1,\"estado\":\"Aceptado\"," +
            "\"eNCF\":\"E310000000001\",\"secuenciaUtilizada\":true}";

        var respuesta = await cliente.ConsultarEstadoAsync("TR-9");

        Assert.Contains("consultaresultado/api/consultas/estado?trackid=TR-9", handler.UltimaUrlDeNegocio);
        Assert.Equal("Aceptado", respuesta.Estado);
        Assert.Equal("E310000000001", respuesta.eNCF);
        Assert.True(respuesta.SecuenciaUtilizada);
    }

    [Fact]
    public async Task EnvioIncierto_SinRespuestaDelServidor()
    {
        var (cliente, handler) = CrearCliente();
        handler.CaidaDeRed = true;   // la petición no obtiene respuesta alguna: no se sabe si llegó

        var respuesta = await cliente.EnviarFacturaAsync("<eCF/>", "13100000001E310000000001.xml", false, 10m);

        Assert.False(respuesta.EsExitoso);
        Assert.Equal(0, respuesta.CodigoHttp);         // sin código HTTP: no se sabe si llegó
        Assert.Contains("conexión", respuesta.Mensaje, StringComparison.OrdinalIgnoreCase);
    }
}
