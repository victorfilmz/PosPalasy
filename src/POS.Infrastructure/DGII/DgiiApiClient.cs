using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.DTOs;

namespace POS.Infrastructure.DGII;

public class DgiiConfig
{
    public string BaseUrl { get; set; } = "https://dfe.dgii.gov.do";
    public string RecepcionEndpoint { get; set; } = "/fe/recepcion/api/ecf";
    public string ResultadoEndpoint { get; set; } = "/fe/recepcion/api/ecf/resultado";
    public string AprobacionComercialEndpoint { get; set; } = "/fe/aprobacioncomercial/api/ecf";
    public string AnulacionEndpoint { get; set; } = "/fe/anulacion/api/ecf";
    public string AutenticacionEndpoint { get; set; } = "/fe/autenticacion/api";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
    public bool ModoSimulador { get; set; } = false;
}

public interface IDgiiApiClient
{
    Task<DgiiApiResponse> EnviarFacturaAsync(string xmlBase64, string hash, CancellationToken ct = default);
    Task<DgiiApiResponse> ConsultarEstadoAsync(string trackId, CancellationToken ct = default);
    Task<DgiiApiResponse> EnviarAprobacionComercialAsync(string xmlBase64, string hash, CancellationToken ct = default);
    Task<DgiiApiResponse> EnviarAnulacionAsync(string xmlBase64, string hash, CancellationToken ct = default);
}

/// <summary>
/// Cliente HTTP para la API REST en formato JSON de la DGII de República Dominicana (NO SOAP).
/// </summary>
public class DgiiApiClient : IDgiiApiClient
{
    private readonly HttpClient _httpClient;
    private readonly DgiiConfig _config;
    private readonly IDgiiAuthenticator? _autenticador;
    private readonly ILogger<DgiiApiClient> _logger;

    public DgiiApiClient(HttpClient httpClient, DgiiConfig config, ILogger<DgiiApiClient> logger, IDgiiAuthenticator? autenticador = null)
    {
        _autenticador = autenticador;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_httpClient.BaseAddress == null && !string.IsNullOrWhiteSpace(_config.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_config.BaseUrl);
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    public async Task<DgiiApiResponse> EnviarFacturaAsync(string xmlBase64, string hash, CancellationToken ct = default)
    {
        if (_config.ModoSimulador)
        {
            _logger.LogInformation("SIMULADOR DGII: Simulando envío de factura con hash {Hash}", hash);
            return new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                TrackId = Guid.NewGuid().ToString("N").Substring(0, 15).ToUpper(),
                Estado = "EnProceso",
                Mensaje = "Factura recibida por DGII (Simulación)",
                RawResponse = "{\"trackId\":\"SIMULADO\",\"estado\":\"EnProceso\"}"
            };
        }

        var payload = new
        {
            xml = xmlBase64,
            hash = hash
        };

        var json = JsonSerializer.Serialize(payload);

        _logger.LogInformation("Enviando e-CF a DGII endpoint {Endpoint} con hash {Hash}", _config.RecepcionEndpoint, hash);

        try
        {
            var response = await PostConTokenAsync(
                _config.RecepcionEndpoint,
                () => new StringContent(json, Encoding.UTF8, "application/json"),
                ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Respuesta DGII recepción (HTTP {StatusCode}): {Response}", response.StatusCode, responseBody);

            string? trackId = null;
            string? estado = null;

            try
            {
                using var jsonDoc = JsonDocument.Parse(responseBody);
                if (jsonDoc.RootElement.TryGetProperty("TrackId", out var trackProp) ||
                    jsonDoc.RootElement.TryGetProperty("trackId", out trackProp))
                {
                    trackId = trackProp.GetString();
                }

                if (jsonDoc.RootElement.TryGetProperty("Estado", out var estadoProp) ||
                    jsonDoc.RootElement.TryGetProperty("estado", out estadoProp))
                {
                    estado = estadoProp.GetString();
                }
            }
            catch (JsonException)
            {
                // La respuesta no era JSON estándar o venía vacía
            }

            return new DgiiApiResponse
            {
                EsExitoso = response.IsSuccessStatusCode,
                CodigoHttp = (int)response.StatusCode,
                TrackId = trackId,
                Estado = estado ?? (response.IsSuccessStatusCode ? "EnProceso" : "Error"),
                Mensaje = response.IsSuccessStatusCode ? "Factura recibida por DGII" : $"Error DGII HTTP {(int)response.StatusCode}",
                RawResponse = responseBody
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción de comunicación al enviar e-CF a DGII");
            return new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 500,
                Mensaje = $"Error de conexión con DGII: {ex.Message}",
                RawResponse = ex.ToString()
            };
        }
    }

    public async Task<DgiiApiResponse> ConsultarEstadoAsync(string trackId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            throw new ArgumentException("El TrackId es requerido para consultar estado.", nameof(trackId));

        if (_config.ModoSimulador)
        {
            _logger.LogInformation("SIMULADOR DGII: Simulando consulta de estado para TrackId {TrackId}", trackId);
            return new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                TrackId = trackId,
                Estado = "Aceptado",
                Mensaje = "Estado consultado exitosamente (Simulación)",
                RawResponse = "{\"trackId\":\"" + trackId + "\",\"estado\":\"Aceptado\"}"
            };
        }

        var endpoint = $"{_config.ResultadoEndpoint.TrimEnd('/')}/{trackId}";
        _logger.LogInformation("Consultando resultado DGII para TrackId: {TrackId}", trackId);

        try
        {
            var response = await GetConTokenAsync(endpoint, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            string? estado = null;
            try
            {
                using var jsonDoc = JsonDocument.Parse(responseBody);
                if (jsonDoc.RootElement.TryGetProperty("Estado", out var estadoProp) ||
                    jsonDoc.RootElement.TryGetProperty("estado", out estadoProp))
                {
                    estado = estadoProp.GetString();
                }
            }
            catch (JsonException) { }

            return new DgiiApiResponse
            {
                EsExitoso = response.IsSuccessStatusCode,
                CodigoHttp = (int)response.StatusCode,
                TrackId = trackId,
                Estado = estado,
                RawResponse = responseBody
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar estado DGII para TrackId {TrackId}", trackId);
            return new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 500,
                TrackId = trackId,
                Mensaje = ex.Message
            };
        }
    }

    public async Task<DgiiApiResponse> EnviarAprobacionComercialAsync(string xmlBase64, string hash, CancellationToken ct = default)
    {        var payload = new { xml = xmlBase64, hash };
        var json = JsonSerializer.Serialize(payload);

        var response = await PostConTokenAsync(
            _config.AprobacionComercialEndpoint,
            () => new StringContent(json, Encoding.UTF8, "application/json"),
            ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        return new DgiiApiResponse
        {
            EsExitoso = response.IsSuccessStatusCode,
            CodigoHttp = (int)response.StatusCode,
            RawResponse = responseBody
        };
    }

    public async Task<DgiiApiResponse> EnviarAnulacionAsync(string xmlBase64, string hash, CancellationToken ct = default)
    {
        var payload = new { xml = xmlBase64, hash };
        var json = JsonSerializer.Serialize(payload);

        _logger.LogInformation("Enviando ANECF a DGII endpoint {Endpoint} con hash {Hash}", _config.AnulacionEndpoint, hash);

        try
        {
            var response = await PostConTokenAsync(
                _config.AnulacionEndpoint,
                () => new StringContent(json, Encoding.UTF8, "application/json"),
                ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            string? trackId = null;
            try
            {
                using var jsonDoc = JsonDocument.Parse(responseBody);
                if (jsonDoc.RootElement.TryGetProperty("TrackId", out var trackProp) ||
                    jsonDoc.RootElement.TryGetProperty("trackId", out trackProp))
                {
                    trackId = trackProp.GetString();
                }
            }
            catch (JsonException) { }

            return new DgiiApiResponse
            {
                EsExitoso = response.IsSuccessStatusCode,
                CodigoHttp = (int)response.StatusCode,
                TrackId = trackId,
                Mensaje = response.IsSuccessStatusCode ? "Solicitud de anulación recibida por DGII" : $"Error DGII HTTP {(int)response.StatusCode}",
                RawResponse = responseBody
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar ANECF a DGII");
            return new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 500,
                Mensaje = $"Error de conexión con DGII: {ex.Message}",
                RawResponse = ex.ToString()
            };
        }
    }

    /// <summary>
    /// POST autenticado: adjunta el token Bearer vigente y, ante 401/403, renueva el token UNA vez
    /// y reintenta. Más de un ciclo de renovación indicaría credenciales realmente inválidas: eso
    /// lo clasifica el servicio orquestador como error permanente.
    /// </summary>
    private async Task<HttpResponseMessage> PostConTokenAsync(
        string endpoint, Func<HttpContent> crearContenido, CancellationToken ct)
    {
        var respuesta = await EnviarConTokenAsync(HttpMethod.Post, endpoint, crearContenido(), ct);

        if ((int)respuesta.StatusCode is 401 or 403 && _autenticador != null)
        {
            respuesta.Dispose();
            await _autenticador.RenovarTokenAsync(ct);
            respuesta = await EnviarConTokenAsync(HttpMethod.Post, endpoint, crearContenido(), ct);
        }

        return respuesta;
    }

    private async Task<HttpResponseMessage> GetConTokenAsync(string endpoint, CancellationToken ct)
    {
        var respuesta = await EnviarConTokenAsync(HttpMethod.Get, endpoint, contenido: null, ct);

        if ((int)respuesta.StatusCode is 401 or 403 && _autenticador != null)
        {
            respuesta.Dispose();
            await _autenticador.RenovarTokenAsync(ct);
            respuesta = await EnviarConTokenAsync(HttpMethod.Get, endpoint, contenido: null, ct);
        }

        return respuesta;
    }

    private async Task<HttpResponseMessage> EnviarConTokenAsync(
        HttpMethod metodo, string endpoint, HttpContent? contenido, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, endpoint) { Content = contenido };

        var token = _autenticador == null ? null : await _autenticador.ObtenerTokenAsync(ct);
        if (!string.IsNullOrWhiteSpace(token))
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _httpClient.SendAsync(peticion, ct);
    }
}
