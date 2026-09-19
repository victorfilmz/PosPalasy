using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.DTOs;

namespace POS.Infrastructure.DGII;

/// <summary>
/// Cliente de transmisión de comprobantes a la API REST de la DGII según <c>api_rest.md</c>.
/// </summary>
/// <remarks>
/// <para>El envío es SIEMPRE multipart/form-data con el campo <c>xml</c> y nombre de archivo
/// <c>RNC + eNCF + ".xml"</c> (p. ej. <c>101672919E3100000001.xml</c>). La recepción ocurre en
/// DOS endpoints según la regla oficial de monto:</para>
/// <list type="bullet">
/// <item><c>≥ RD$250,000.00</c> o tipo no factura de consumo → e-CF completo (host ecf.dgii.gov.do).</item>
/// <item><c>&lt; RD$250,000.00</c> factura de consumo → RFCE, resumen del e-CF (host fc.dgii.gov.do).</item>
/// </list>
/// <para>El token Bearer lo aporta <see cref="IDgiiAuthenticator"/>; ante 401/403 se renueva UNA
/// vez y se reintenta (sin bucles: la persistencia del 401 la clasifica el orquestador).</para>
/// </remarks>
/// <summary>
/// Contrato de transmisión y consulta ante la DGII. El envío es multipart con nombre de archivo
/// oficial RNC+eNCF.xml; el endpoint lo decide la regla de los RD$250,000.
/// </summary>
public interface IDgiiApiClient
{
    /// <summary>
    /// Transmite un comprobante firmado. <paramref name="esFacturaConsumo"/> + <paramref name="montoTotal"/>
    /// determinan el endpoint (RFCE si es factura de consumo &lt; RD$250,000; e-CF completo en caso contrario).
    /// </summary>
    Task<DgiiApiResponse> EnviarFacturaAsync(
        string xml, string nombreArchivo, bool esFacturaConsumo, decimal montoTotal, CancellationToken ct = default);

    /// <summary>Consulta el resultado de un e-CF por TrackId (Aceptado, Aceptado Condicional, Rechazado, En proceso).</summary>
    Task<DgiiApiResponse> ConsultarEstadoAsync(string trackId, CancellationToken ct = default);

    /// <summary>Transmite la aprobación comercial (e-CF entre contribuyentes).</summary>
    Task<DgiiApiResponse> EnviarAprobacionComercialAsync(string xml, string nombreArchivo, CancellationToken ct = default);

    /// <summary>Transmite la anulación de rangos e-NCF (ANECF).</summary>
    Task<DgiiApiResponse> EnviarAnulacionAsync(string xml, string nombreArchivo, CancellationToken ct = default);

    /// <summary>
    /// Consulta el resumen RFCE por RNC emisor, e-NCF y código de seguridad (0=No encontrado,
    /// 1=Aceptado, 2=Rechazado).
    /// </summary>
    Task<DgiiApiResponse> ConsultarRFCEAsync(string rncEmisor, string encf, string codigoSeguridad, CancellationToken ct = default);
}

public class DgiiApiClient : IDgiiApiClient
{
    /// <summary>Umbral oficial que separa la recepción e-CF de la recepción RFCE.</summary>
    public static readonly decimal UmbralECF = 250_000.00m;

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

        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    /// <summary>
    /// Nombre de archivo oficial del comprobante transmitido: <c>RNC + eNCF + ".xml"</c>.
    /// La DGII lo exige en la parte del multipart; un nombre distinto puede provocar rechazo.
    /// </summary>
    public static string CrearNombreArchivoXml(string rncEmisor, string encf) => $"{rncEmisor}{encf}.xml";

    /// <summary>Decide el endpoint de recepción según la regla oficial de monto.</summary>
    public static string DecidirRecepcion(bool esFacturaConsumo, decimal montoTotal, DgiiEndpoints endpoints) =>
        esFacturaConsumo && montoTotal < UmbralECF ? endpoints.RecepcionRFCE : endpoints.RecepcionECF;

    public async Task<DgiiApiResponse> EnviarFacturaAsync(
        string xml, string nombreArchivo, bool esFacturaConsumo, decimal montoTotal, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("El XML del comprobante es requerido para transmitir.", nameof(xml));
        if (string.IsNullOrWhiteSpace(nombreArchivo))
            throw new ArgumentException("El nombre de archivo RNC+eNCF.xml es requerido por la DGII.", nameof(nombreArchivo));

        var endpoints = _config.Endpoints();
        var endpoint = DecidirRecepcion(esFacturaConsumo, montoTotal, endpoints);

        if (_config.ModoSimulador)
        {
            _logger.LogInformation(
                "SIMULADOR DGII: comprobante {Archivo} (monto {Monto}) hacia {Endpoint}",
                nombreArchivo, montoTotal, endpoint);
            return new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                TrackId = Guid.NewGuid().ToString("N")[..15].ToUpperInvariant(),
                Estado = "En proceso",
                Mensaje = "Comprobante recibido por DGII (Simulación)",
                RawResponse = "{\"trackId\":\"SIMULADO\",\"estado\":\"En proceso\"}"
            };
        }

        _logger.LogInformation(
            "Transmitiendo comprobante {Archivo} (monto {Monto}) a {Endpoint}",
            nombreArchivo, montoTotal, endpoint);

        try
        {
            var respuesta = await PostMultipartConTokenAsync(
                endpoint,
                xml,
                nombreArchivo,
                ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            return ParsearRespuestaRecepcion(respuesta, cuerpo);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Excepción de comunicación al transmitir {Archivo} a la DGII", nombreArchivo);
            return new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 0,
                Mensaje = $"Error de conexión con DGII: {ex.Message}",
                RawResponse = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Consulta el resultado de un e-CF por TrackId
    /// (estados: En proceso, Aceptado, Aceptado Condicional, Rechazado, No encontrado).
    /// </summary>
    public async Task<DgiiApiResponse> ConsultarEstadoAsync(string trackId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            throw new ArgumentException("El TrackId es requerido para consultar el resultado.", nameof(trackId));

        if (_config.ModoSimulador)
        {
            _logger.LogInformation("SIMULADOR DGII: consulta de resultado para TrackId {TrackId}", trackId);
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

        var endpoint = $"{_config.Endpoints().ConsultaResultadoECF}?trackid={Uri.EscapeDataString(trackId)}";
        _logger.LogInformation("Consultando resultado DGII para TrackId: {TrackId}", trackId);

        try
        {
            var respuesta = await GetConTokenAsync(endpoint, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            string? estado = null;
            string? eNCF = null;
            bool? secuenciaUtilizada = null;
            if (TryParsearJson(cuerpo, out var json))
            {
                estado = LeerCadena(json, "estado");
                eNCF = LeerCadena(json, "eNCF");
                secuenciaUtilizada = LeerBooleano(json, "secuenciaUtilizada");
            }

            return new DgiiApiResponse
            {
                EsExitoso = respuesta.IsSuccessStatusCode,
                CodigoHttp = (int)respuesta.StatusCode,
                TrackId = trackId,
                Estado = estado,
                eNCF = eNCF,
                SecuenciaUtilizada = secuenciaUtilizada,
                Mensaje = respuesta.IsSuccessStatusCode ? null : $"Error HTTP {(int)respuesta.StatusCode}: {cuerpo}",
                RawResponse = cuerpo
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Error al consultar resultado DGII para TrackId {TrackId}", trackId);
            return new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 0,
                TrackId = trackId,
                Mensaje = ex.Message
            };
        }
    }

    /// <summary>
    /// Consulta el resumen RFCE por RNC emisor, e-NCF y código de seguridad
    /// (estados: 0=No encontrado, 1=Aceptado, 2=Rechazado).
    /// </summary>
    public async Task<DgiiApiResponse> ConsultarRFCEAsync(
        string rncEmisor, string encf, string codigoSeguridad, CancellationToken ct = default)
    {
        if (_config.ModoSimulador)
        {
            _logger.LogInformation("SIMULADOR DGII: consulta RFCE para e-NCF {ENCF}", encf);
            return new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                eNCF = encf,
                Estado = "Aceptado",
                Mensaje = "Consulta RFCE simulada",
                RawResponse = "{\"rnc\":\"\",\"encf\":\"" + encf + "\",\"codigo\":\"1\",\"estado\":\"Aceptado\"}"
            };
        }

        var endpoints = _config.Endpoints();
        var endpoint = $"{endpoints.ConsultaRFCE}?RNC_Emisor={Uri.EscapeDataString(rncEmisor)}" +
                       $"&ENCF={Uri.EscapeDataString(encf)}" +
                       $"&Cod_Seguridad_eCF={Uri.EscapeDataString(codigoSeguridad)}";

        _logger.LogInformation("Consultando resumen RFCE para e-NCF {ENCF}", encf);

        try
        {
            var respuesta = await GetConTokenAsync(endpoint, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            string? estado = null;
            string? eNCF = null;
            bool? secuenciaUtilizada = null;
            if (TryParsearJson(cuerpo, out var json))
            {
                estado = LeerCadena(json, "estado") ?? (LeerNumero(json, "codigo") is { } codigo
                    ? codigo switch { 1 => "Aceptado", 2 => "Rechazado", _ => "No encontrado" }
                    : null);
                eNCF = LeerCadena(json, "encf") ?? LeerCadena(json, "eNCF");
                secuenciaUtilizada = LeerBooleano(json, "secuenciaUtilizada");
            }

            return new DgiiApiResponse
            {
                EsExitoso = respuesta.IsSuccessStatusCode,
                CodigoHttp = (int)respuesta.StatusCode,
                eNCF = eNCF,
                Estado = estado,
                SecuenciaUtilizada = secuenciaUtilizada,
                Mensaje = respuesta.IsSuccessStatusCode ? null : $"Error HTTP {(int)respuesta.StatusCode}: {cuerpo}",
                RawResponse = cuerpo
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Error al consultar resumen RFCE para e-NCF {ENCF}", encf);
            return new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 0,
                Mensaje = ex.Message
            };
        }
    }

    public async Task<DgiiApiResponse> EnviarAprobacionComercialAsync(string xml, string nombreArchivo, CancellationToken ct = default)
    {
        if (_config.ModoSimulador)
        {
            _logger.LogInformation("SIMULADOR DGII: aprobación comercial {Archivo}", nombreArchivo);
            return new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                Estado = "Aprobada",
                Mensaje = "Aprobación comercial recibida (Simulación)",
                RawResponse = "{\"estado\":\"Aprobada\",\"codigo\":\"1\"}"
            };
        }

        try
        {
            var respuesta = await PostMultipartConTokenAsync(
                _config.Endpoints().AprobacionComercial, xml, nombreArchivo, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            string? estado = null;
            string? mensaje = null;
            if (TryParsearJson(cuerpo, out var json))
            {
                estado = LeerCadena(json, "estado");
                if (json.RootElement.TryGetProperty("mensaje", out var propMensaje) &&
                    propMensaje.ValueKind == JsonValueKind.Array &&
                    propMensaje.GetArrayLength() > 0)
                {
                    mensaje = string.Join(" | ", propMensaje.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()));
                }
            }

            return new DgiiApiResponse
            {
                EsExitoso = respuesta.IsSuccessStatusCode,
                CodigoHttp = (int)respuesta.StatusCode,
                Estado = estado,
                Mensaje = mensaje ?? (respuesta.IsSuccessStatusCode ? "Aprobación comercial recibida" : $"Error HTTP {(int)respuesta.StatusCode}: {cuerpo}"),
                RawResponse = cuerpo
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 0,
                Mensaje = $"Error de conexión con DGII: {ex.Message}",
                RawResponse = ex.ToString()
            };
        }
    }

    /// <summary>Transmite la anulación de rangos (ANECF) por multipart al endpoint oficial.</summary>
    public async Task<DgiiApiResponse> EnviarAnulacionAsync(string xml, string nombreArchivo, CancellationToken ct = default)
    {
        if (_config.ModoSimulador)
        {
            _logger.LogInformation("SIMULADOR DGII: anulación de rangos {Archivo}", nombreArchivo);
            return new DgiiApiResponse
            {
                EsExitoso = true,
                CodigoHttp = 200,
                TrackId = Guid.NewGuid().ToString("N")[..15].ToUpperInvariant(),
                Mensaje = "Solicitud de anulación recibida (Simulación)",
                RawResponse = "{\"trackId\":\"SIMULADO\"}"
            };
        }

        try
        {
            var respuesta = await PostMultipartConTokenAsync(
                _config.Endpoints().AnulacionRangos, xml, nombreArchivo, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            string? trackId = null;
            if (TryParsearJson(cuerpo, out var json))
                trackId = LeerCadena(json, "trackId");

            return new DgiiApiResponse
            {
                EsExitoso = respuesta.IsSuccessStatusCode,
                CodigoHttp = (int)respuesta.StatusCode,
                TrackId = trackId,
                Mensaje = respuesta.IsSuccessStatusCode
                    ? "Solicitud de anulación recibida por DGII"
                    : $"Error HTTP {(int)respuesta.StatusCode}: {cuerpo}",
                RawResponse = cuerpo
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Error al transmitir ANECF a la DGII");
            return new DgiiApiResponse
            {
                EsExitoso = false,
                CodigoHttp = 0,
                Mensaje = $"Error de conexión con DGII: {ex.Message}",
                RawResponse = ex.ToString()
            };
        }
    }

    /// <summary>
    /// POST multipart autenticado con el campo <c>xml</c> y el nombre de archivo oficial
    /// <c>RNC+eNCF.xml</c>. Ante 401/403 renueva el token UNA vez y reintenta.
    /// </summary>
    private async Task<HttpResponseMessage> PostMultipartConTokenAsync(
        string endpoint, string xml, string nombreArchivo, CancellationToken ct)
    {
        var respuesta = await EnviarMultipartAsync(endpoint, xml, nombreArchivo, ct);

        if ((int)respuesta.StatusCode is 401 or 403 && _autenticador != null)
        {
            respuesta.Dispose();
            await _autenticador.RenovarTokenAsync(ct);
            respuesta = await EnviarMultipartAsync(endpoint, xml, nombreArchivo, ct);
        }

        return respuesta;
    }

    private async Task<HttpResponseMessage> EnviarMultipartAsync(
        string endpoint, string xml, string nombreArchivo, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new MultipartFormDataContent
            {
                {
                    new StringContent(xml, Encoding.UTF8, "text/xml"),
                    "xml", nombreArchivo
                }
            }
        };

        var token = await TokenDeHostAsync(endpoint, ct);
        if (!string.IsNullOrWhiteSpace(token))
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _httpClient.SendAsync(peticion, ct);
    }

    private async Task<HttpResponseMessage> GetConTokenAsync(string endpoint, CancellationToken ct)
    {
        var respuesta = await EnviarGetAsync(endpoint, ct);

        if ((int)respuesta.StatusCode is 401 or 403 && _autenticador != null)
        {
            respuesta.Dispose();
            await _autenticador.RenovarTokenAsync(ct);
            respuesta = await EnviarGetAsync(endpoint, ct);
        }

        return respuesta;
    }

    private async Task<HttpResponseMessage> EnviarGetAsync(string endpoint, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, endpoint);
        peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var token = await TokenDeHostAsync(endpoint, ct);
        if (!string.IsNullOrWhiteSpace(token))
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _httpClient.SendAsync(peticion, ct);
    }

    /// <summary>
    /// El token NO es intercambiable entre hosts: e-CF y RFCE autentican por separado, así que el
    /// token se pide del host al que apunta la URL de la llamada.
    /// </summary>
    private async Task<string?> TokenDeHostAsync(string endpoint, CancellationToken ct)
    {
        if (_autenticador == null) return null;
        var esRFCE = endpoint.StartsWith(_config.Endpoints().HostRFCE, StringComparison.OrdinalIgnoreCase);
        return esRFCE
            ? await _autenticador.ObtenerTokenRFCEAsync(ct)
            : await _autenticador.ObtenerTokenAsync(ct);
    }

    /// <summary>
    /// Convierte la respuesta HTTP de recepción en el resultado fiscal completo: exito = 2xx,
    /// código de estado (1/2/3), mensajes de la DGII y marca de secuencia utilizada.
    /// </summary>
    private static DgiiApiResponse ParsearRespuestaRecepcion(HttpResponseMessage respuesta, string cuerpo)
    {
        var resultado = new DgiiApiResponse
        {
            CodigoHttp = (int)respuesta.StatusCode,
            EsExitoso = respuesta.IsSuccessStatusCode,
            RawResponse = cuerpo
        };

        if (!TryParsearJson(cuerpo, out var json))
        {
            resultado.Mensaje = respuesta.IsSuccessStatusCode
                ? "Comprobante recibido por la DGII"
                : $"Error HTTP {(int)respuesta.StatusCode}: {cuerpo}";
            return resultado;
        }

        resultado.TrackId = LeerCadena(json, "trackId");
        resultado.Estado = LeerCadena(json, "estado");
        resultado.eNCF = LeerCadena(json, "encf") ?? LeerCadena(json, "eNCF");
        resultado.SecuenciaUtilizada = LeerBooleano(json, "secuenciaUtilizada");

        // Resultado e-CF (por TrackId): "error" y "mensaje" son escalares.
        var error = LeerCadena(json, "error");
        var mensaje = LeerCadena(json, "mensaje");

        // Resultado RFCE: "mensajes" es un arreglo [{codigo, valor}].
        if (json.RootElement.TryGetProperty("mensajes", out var propMensajes) &&
            propMensajes.ValueKind == JsonValueKind.Array)
        {
            var textos = propMensajes.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Object
                    ? (LeerCadena(e, "valor") is { Length: > 0 } v ? $"{LeerCadena(e, "codigo") ?? ""}: {v}".Trim(' ', ':') : LeerCadena(e, "codigo"))
                    : e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            if (textos.Count > 0)
                mensaje = string.Join(" | ", textos);
        }

        if (resultado.Estado == null && LeerNumero(json, "codigo") is { } codigo)
            resultado.Estado = codigo switch
            {
                1 => "Aceptado",
                2 => "Aceptado Condicional",
                3 => "Rechazado",
                _ => null
            };

        resultado.Mensaje = !string.IsNullOrWhiteSpace(mensaje)
            ? mensaje
            : !string.IsNullOrWhiteSpace(error)
                ? error
                : respuesta.IsSuccessStatusCode
                    ? "Comprobante recibido por la DGII"
                    : $"Error HTTP {(int)respuesta.StatusCode}: {cuerpo}";

        return resultado;
    }

    private static bool TryParsearJson(string cuerpo, out JsonDocument json)
    {
        if (string.IsNullOrWhiteSpace(cuerpo))
        {
            json = null!;
            return false;
        }
        try
        {
            json = JsonDocument.Parse(cuerpo);
            return true;
        }
        catch (JsonException)
        {
            json = null!;
            return false;
        }
    }

    private static string? LeerCadena(JsonDocument json, string propiedad) =>
        json.RootElement.TryGetProperty(propiedad, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static string? LeerCadena(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? LeerNumero(JsonDocument json, string propiedad) =>
        json.RootElement.TryGetProperty(propiedad, out var prop) &&
        prop.ValueKind == JsonValueKind.Number &&
        prop.TryGetInt32(out var valor)
            ? valor
            : null;

    private static bool? LeerBooleano(JsonDocument json, string propiedad)
    {
        if (!json.RootElement.TryGetProperty(propiedad, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
