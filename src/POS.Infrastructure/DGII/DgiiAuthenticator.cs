using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using POS.Domain.Common;
using POS.Infrastructure.Services;

namespace POS.Infrastructure.DGII;

/// <summary>
/// Contrato de autenticación contra la DGII: semilla → firmar con el certificado del emisor →
/// token Bearer (vigencia 1 hora según la especificación de la API).
/// </summary>
/// <remarks>
/// La API mantiene tokens SEPARADOS por host (e-CF y RFCE exponen cada uno su propio endpoint de
/// autenticación): el token de uno no sirve para el otro.
/// </remarks>
public interface IDgiiAuthenticator
{
    /// <summary>
    /// Devuelve un token vigente del host e-CF, reutilizando el caché mientras sirva (refresco
    /// temprano a los 55 minutos de vida). Thread-safe: bajo concurrencia, uno autentica y los
    /// demás esperan.
    /// </summary>
    Task<string> ObtenerTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Devuelve un token vigente del host RFCE (facturas de consumo &lt; RD$250,000), con la misma
    /// política de caché que el host e-CF.
    /// </summary>
    Task<string> ObtenerTokenRFCEAsync(CancellationToken ct = default);

    /// <summary>
    /// Invalida el caché de ambos hosts y obtiene tokens recién emitidos. Es la ruta de
    /// recuperación cuando la DGII responde 401/403: se reintenta UNA vez con el token renovado,
    /// no en bucle.
    /// </summary>
    Task<string> RenovarTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Autenticador de la API REST de la DGII (contrato de <c>api_rest.md</c>):
/// <list type="bullet">
/// <item><c>GET {host}/autenticacion/api/autenticacion/semilla</c> → XML <c>SemillaModel</c>.</item>
/// <item>Firmar el XML de semilla con el certificado digital del emisor (XML-DSig).</item>
/// <item><c>POST {host}/autenticacion/api/autenticacion/validarsemilla</c> (multipart, campo
/// <c>xml</c>) → JSON con <c>token</c> y <c>expira</c>.</item>
/// </list>
/// Cada host (e-CF y RFCE) mantiene su propio token en caché. El refresco temprano garantiza que
/// ninguna operación fiscal arranque con un token a menos de 5 minutos de vencer.
/// </summary>
public sealed class DgiiAuthenticator : IDgiiAuthenticator
{
    /// <summary>Vigencia declarada de los tokens de la DGII.</summary>
    public static readonly TimeSpan VigenciaToken = TimeSpan.FromHours(1);

    /// <summary>Anticipación con la que se renueva un token antes de vencer (55 min de vida).</summary>
    public static readonly TimeSpan RefrescoTemprano = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly DgiiConfig _config;
    private readonly IProveedorCertificadoDigital _proveedorCertificado;
    private readonly IFirmadorComprobanteECF _firmador;
    private readonly ILogger<DgiiAuthenticator> _logger;
    private readonly SemaphoreSlim _cerrojo = new(1, 1);

    private readonly TokenEnCache _tokenEcf = new();
    private readonly TokenEnCache _tokenRfce = new();

    public DgiiAuthenticator(
        HttpClient httpClient,
        DgiiConfig config,
        IProveedorCertificadoDigital proveedorCertificado,
        IFirmadorComprobanteECF firmador,
        ILogger<DgiiAuthenticator> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _proveedorCertificado = proveedorCertificado ?? throw new ArgumentNullException(nameof(proveedorCertificado));
        _firmador = firmador ?? throw new ArgumentNullException(nameof(firmador));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Sin BaseAddress fija: este cliente autentica CONTRA DOS HOSTS (e-CF y RFCE) y cada
        // llamada lleva su URL absoluta.
    }

    public Task<string> ObtenerTokenAsync(CancellationToken ct = default) =>
        ObtenerTokenDeAsync(_tokenEcf, EsECF: true, ct);

    public Task<string> ObtenerTokenRFCEAsync(CancellationToken ct = default) =>
        ObtenerTokenDeAsync(_tokenRfce, EsECF: false, ct);

    public async Task<string> RenovarTokenAsync(CancellationToken ct = default)
    {
        // Renueva el par completo: un 401 en una operación de cualquiera de los hosts invalida el
        // caché del host correspondiente; renovar ambos mantiene simple el contrato del cliente.
        await _cerrojo.WaitAsync(ct);
        try
        {
            await AutenticarAsync(_tokenEcf, EsECF: true, ct);
            return await AutenticarAsync(_tokenRfce, EsECF: false, ct);
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    private async Task<string> ObtenerTokenDeAsync(TokenEnCache cache, bool EsECF, CancellationToken ct)
    {
        // Vía rápida sin candado: el token sirve mientras le quede más del margen de refresco.
        if (cache.Token != null && DateTime.UtcNow < cache.ExpiraUtc - RefrescoTemprano)
            return cache.Token;

        await _cerrojo.WaitAsync(ct);
        try
        {
            // Doble verificación: otro hilo pudo haber renovado mientras esperábamos el candado.
            if (cache.Token != null && DateTime.UtcNow < cache.ExpiraUtc - RefrescoTemprano)
                return cache.Token;

            return await AutenticarAsync(cache, EsECF, ct);
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    private async Task<string> AutenticarAsync(TokenEnCache cache, bool EsECF, CancellationToken ct)
    {
        var certificado = _proveedorCertificado.ObtenerCertificado()
            ?? throw new ReglaDeNegocioException(
                "No hay certificado digital instalado: la DGII exige firmar la semilla con el " +
                "certificado del emisor para emitir el token de autenticación. " + _proveedorCertificado.DescribirEstado(),
                "CERTIFICADO_AUSENTE");

        var semillaXml = await DescargarSemillaAsync(EsECF, ct);
        var semillaFirmada = _firmador.Firmar(semillaXml, certificado);
        var token = await ValidarSemillaAsync(EsECF, semillaFirmada, ct);

        cache.Token = token;
        cache.ExpiraUtc = DateTime.UtcNow.Add(VigenciaToken);

        _logger.LogInformation(
            "Token DGII renovado para el host {Host} (vigencia 1 hora, refresco a los 55 minutos).",
            EsECF ? "e-CF" : "RFCE");
        return token;
    }

    private async Task<string> DescargarSemillaAsync(bool EsECF, CancellationToken ct)
    {
        var endpoint = EsECF ? _config.Endpoints().SemillaECF : _config.Endpoints().SemillaRFCE;

        try
        {
            using var respuesta = await _httpClient.GetAsync(endpoint, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            if (!respuesta.IsSuccessStatusCode)
                throw new ReglaDeNegocioException(
                    $"La DGII no entregó la semilla de autenticación (HTTP {(int)respuesta.StatusCode}).",
                    "DGII_SEMILLA_FALLO");

            // El valor de la semilla viaja dentro del XML SemillaModel; se firma el XML COMPLETO.
            var doc = XDocument.Parse(cuerpo);
            var raiz = doc.Root ?? throw new ReglaDeNegocioException(
                "La semilla de la DGII no es un XML reconocible.", "DGII_SEMILLA_INVALIDA");
            var valor = raiz.Element("valor")?.Value;
            if (string.IsNullOrWhiteSpace(valor))
                throw new ReglaDeNegocioException(
                    "La semilla de la DGII no trae valor.", "DGII_SEMILLA_INVALIDA");

            _logger.LogInformation("Semilla DGII obtenida (valor {Valor}).", valor);
            return cuerpo;
        }
        catch (System.Xml.XmlException ex)
        {
            throw new ReglaDeNegocioException(
                $"La semilla de la DGII no es un XML válido: {ex.Message}", "DGII_SEMILLA_INVALIDA");
        }
    }

    private async Task<string> ValidarSemillaAsync(bool EsECF, string semillaFirmada, CancellationToken ct)
    {
        var endpoint = EsECF ? _config.Endpoints().ValidarSemillaECF : _config.Endpoints().ValidarSemillaRFCE;

        using var contenido = new MultipartFormDataContent
        {
            { new StringContent(semillaFirmada, Encoding.UTF8, "text/xml"), "xml", "semilla_firmada.xml" }
        };

        try
        {
            using var respuesta = await _httpClient.PostAsync(endpoint, contenido, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            if (!respuesta.IsSuccessStatusCode)
                throw new ReglaDeNegocioException(
                    $"La DGII rechazó la semilla firmada (HTTP {(int)respuesta.StatusCode}): {cuerpo}",
                    "DGII_TOKEN_FALLO");

            using var json = JsonDocument.Parse(CuerpoOJsonVacio(cuerpo));
            var token = json.RootElement.TryGetProperty("token", out var propToken)
                ? propToken.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(token))
                throw new ReglaDeNegocioException(
                    "La respuesta de validación de la DGII no trae token.", "DGII_TOKEN_INVALIDO");

            // "expira" es informativo: la política local fija la vigencia con margen de seguridad.
            return token!;
        }
        catch (JsonException ex)
        {
            throw new ReglaDeNegocioException(
                $"La respuesta de token de la DGII no es JSON válido: {ex.Message}", "DGII_TOKEN_INVALIDO");
        }
    }

    private static string CuerpoOJsonVacio(string cuerpo) => string.IsNullOrWhiteSpace(cuerpo) ? "{}" : cuerpo;

    private sealed class TokenEnCache
    {
        public string? Token;
        public DateTime ExpiraUtc = DateTime.MinValue;
    }
}
