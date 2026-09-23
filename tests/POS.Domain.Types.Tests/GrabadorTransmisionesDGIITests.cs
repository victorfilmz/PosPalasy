using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using POS.Infrastructure.DGII;
using POS.Infrastructure.DGII.Recording;
using Xunit;

namespace POS.Domain.Types.Tests;

/// <summary>
/// Cofre de contratos DGII — los fixtures de este archivo son los contratos JSON de la KB
/// (Descripción Técnica Servicios DGII v1.7); el primer contacto real con testecf grabará las
/// respuestas reales para actualizarlos si difieren. Cadena de handlers:
/// HandlerFalsoDeContrato → GrabadorTransmisionesHandler → cliente.
/// </summary>
public sealed class GrabadorTransmisionesDGIITests : IDisposable
{
    private readonly string _directorioTemporal;

    public GrabadorTransmisionesDGIITests()
    {
        _directorioTemporal = Path.Combine(Path.GetTempPath(), "cofre-dgii-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directorioTemporal);
    }

    public void Dispose() => Directory.Delete(_directorioTemporal, recursive: true);

    // --- Contratos de la KB (Descripción Técnica Servicios DGII v1.7) ---

    private const string ContratoRecepcionECF =
        "{\"trackId\":\"d2b6e27c-3908-46f3-afaa-2207b9501b4b\",\"error\":\"\",\"mensaje\":\"Comprobante recibido\"}";

    private const string ContratoRecepcionRFCE =
        "{\"codigo\":1,\"estado\":\"Aceptado\",\"mensajes\":[{\"codigo\":\"0\",\"valor\":\"\"}],\"encf\":\"E320000000001\",\"secuenciaUtilizada\":true}";

    private const string ContratoConsultaResultado =
        "{\"trackId\":\"d2b6e27c-3908-46f3-afaa-2207b9501b4b\",\"codigo\":1,\"estado\":\"Aceptado\",\"rnc\":\"130862346\",\"eNCF\":\"E310005000201\",\"secuenciaUtilizada\":true,\"fechaRecepcion\":\"2023-08-15T06:06:57\",\"mensajes\":[{\"valor\":\"\",\"codigo\":0}]}";

    private const string ContratoConsultaRFCE =
        "{\"rnc\":\"131880738\",\"encf\":\"E320000000001\",\"secuenciaUtilizada\":true,\"codigo\":\"1\",\"estado\":\"Aceptado\",\"mensajes\":[{\"valor\":\"\",\"codigo\":0}]}";

    private const string ContratoAprobacionComercial =
        "{\"mensaje\":[\"Aprobada comercial\"],\"estado\":\"Aprobada\",\"codigo\":\"1\"}";

    // --- Contratos: el parser de DgiiApiClient interpreta correctamente cada respuesta de la KB ---

    [Fact]
    public async Task Contrato_RecepcionECF_ParseaTrackId()
    {
        var (cliente, handler) = CrearCliente(ContratoRecepcionECF);
        var api = new DgiiApiClient(cliente, ConfigReal(), NullLogger<DgiiApiClient>.Instance);

        var respuesta = await api.EnviarFacturaAsync("<xml/>", "13100000001E3100000001.xml",
            esFacturaConsumo: false, montoTotal: 500_000m);

        Assert.True(respuesta.EsExitoso);
        Assert.Equal(200, respuesta.CodigoHttp);
        Assert.Equal("d2b6e27c-3908-46f3-afaa-2207b9501b4b", respuesta.TrackId);
        var peticion = Assert.Single(((HandlerFalsoDeContrato)handler).Peticiones);
        Assert.Equal("POST", peticion.Metodo);
        Assert.Contains("/recepcion/api/facturaselectronicas", peticion.Url);
    }

    [Fact]
    public async Task Contrato_RecepcionRFCE_ParseaCodigoYSecuencia()
    {
        var (cliente, _) = CrearCliente(ContratoRecepcionRFCE);
        var api = new DgiiApiClient(cliente, ConfigReal(), NullLogger<DgiiApiClient>.Instance);

        var respuesta = await api.EnviarFacturaAsync("<xml/>", "13100000001E3200000001.xml",
            esFacturaConsumo: true, montoTotal: 100m);

        Assert.True(respuesta.EsExitoso);
        Assert.Equal("E320000000001", respuesta.eNCF);
        Assert.True(respuesta.SecuenciaUtilizada);
    }

    [Fact]
    public async Task Contrato_ConsultaResultado_ParseaEstadoAceptado()
    {
        var (cliente, _) = CrearCliente(ContratoConsultaResultado);
        var api = new DgiiApiClient(cliente, ConfigReal(), NullLogger<DgiiApiClient>.Instance);

        var respuesta = await api.ConsultarEstadoAsync("d2b6e27c-3908-46f3-afaa-2207b9501b4b");

        Assert.True(respuesta.EsExitoso);
        Assert.Equal("Aceptado", respuesta.Estado);
        Assert.Equal("E310005000201", respuesta.eNCF);
        Assert.True(respuesta.SecuenciaUtilizada);
    }

    [Fact]
    public async Task Contrato_ConsultaRFCE_MapeaCodigo1AAceptado()
    {
        var (cliente, _) = CrearCliente(ContratoConsultaRFCE);
        var api = new DgiiApiClient(cliente, ConfigReal(), NullLogger<DgiiApiClient>.Instance);

        var respuesta = await api.ConsultarRFCEAsync("131880738", "E320000000001", "Ab3dEf");

        Assert.Equal("Aceptado", respuesta.Estado);
    }

    [Fact]
    public async Task Contrato_AprobacionComercial_ParseaEstadoYMensajes()
    {
        var (cliente, _) = CrearCliente(ContratoAprobacionComercial);
        var api = new DgiiApiClient(cliente, ConfigReal(), NullLogger<DgiiApiClient>.Instance);

        var respuesta = await api.EnviarAprobacionComercialAsync("<xml/>", "acecf.xml");

        Assert.True(respuesta.EsExitoso);
        Assert.Equal("Aprobada", respuesta.Estado);
        Assert.Equal("Aprobada comercial", respuesta.Mensaje);
    }

    // --- Grabador: captura, persistencia y fail-safe ---

    [Fact]
    public async Task Grabador_CapturaMetodoUrlCodigoYCuerpo()
    {
        var ruta = Path.Combine(_directorioTemporal, "cofre.json");
        var grabador = new GrabadorTransmisionesDGII(ruta, NullLogger<GrabadorTransmisionesDGII>.Instance);
        var (cliente, handler) = CrearCliente(ContratoRecepcionECF, grabador);
        var api = new DgiiApiClient(cliente, ConfigReal(), NullLogger<DgiiApiClient>.Instance);

        await api.EnviarFacturaAsync("<xml/>", "13100000001E3100000001.xml", false, 500_000m);

        Assert.Equal(Path.Combine(_directorioTemporal, "cofre.json"), grabador.RutaArchivo);
        var registros = LeerCofre(ruta);
        var r = Assert.Single(registros);
        Assert.Equal("POST", r.Metodo);
        Assert.Contains("/recepcion/api/facturaselectronicas", r.Url);
        Assert.Equal(200, r.CodigoHttp);
        Assert.Equal(ContratoRecepcionECF, r.Cuerpo);
        Assert.Single(((HandlerFalsoDeContrato)handler).Peticiones);
    }

    [Fact]
    public async Task Grabador_PersisteInmediatamente_SobreviveCorteDeProceso()
    {
        var ruta = Path.Combine(_directorioTemporal, "cofre-corte.json");
        var grabador = new GrabadorTransmisionesDGII(ruta, NullLogger<GrabadorTransmisionesDGII>.Instance);

        var (cliente, _) = CrearCliente(ContratoRecepcionECF, grabador);
        var api = new DgiiApiClient(cliente, ConfigReal(), NullLogger<DgiiApiClient>.Instance);
        await api.EnviarFacturaAsync("<xml/>", "13100000001E3100000001.xml", false, 500_000m);

        // Corte seguro: un grabador NUEVO (como tras un reinicio del proceso) encuentra el registro.
        Assert.Single(LeerCofre(ruta));
    }

    [Fact]
    public async Task Grabador_FailSafe_ArchivoBloqueadoNoRompeLaTransmision()
    {
        var ruta = Path.Combine(_directorioTemporal, "cofre-bloqueado.json");
        var grabador = new GrabadorTransmisionesDGII(ruta, NullLogger<GrabadorTransmisionesDGII>.Instance);
        var (cliente, _) = CrearCliente(ContratoRecepcionECF, grabador);
        var api = new DgiiApiClient(cliente, ConfigReal(), NullLogger<DgiiApiClient>.Instance);

        // El archivo está bloqueado con FileShare.None: la grabación no puede escribir.
        using (File.Open(ruta, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var respuesta = await api.EnviarFacturaAsync("<xml/>", "13100000001E3100000001.xml", false, 500_000m);

            // La transmisión sigue intacta pese al fallo de grabación.
            Assert.True(respuesta.EsExitoso, respuesta.Mensaje);
            Assert.Equal("d2b6e27c-3908-46f3-afaa-2207b9501b4b", respuesta.TrackId);
        }
    }

    // --- Replay: un cofre grabado reproduce las respuestas verbatim, sin red ---

    [Fact]
    public async Task Reproductor_ReproduceRespuestasGrabadas()
    {
        var ruta = Path.Combine(_directorioTemporal, "cofre-replay.json");
        var grabador = new GrabadorTransmisionesDGII(ruta, NullLogger<GrabadorTransmisionesDGII>.Instance);

        var (clienteGrabacion, _) = CrearCliente(ContratoRecepcionECF, grabador);
        var apiGrabacion = new DgiiApiClient(clienteGrabacion, ConfigReal(), NullLogger<DgiiApiClient>.Instance);
        await apiGrabacion.EnviarFacturaAsync("<xml/>", "13100000001E3100000001.xml", false, 500_000m);

        var (clienteReplay, _) = CrearCliente(null, handlerReproductor: new ReproductorTransmisionesDGII(LeerCofre(ruta)));
        var apiReplay = new DgiiApiClient(clienteReplay, ConfigReal(), NullLogger<DgiiApiClient>.Instance);

        var respuesta = await apiReplay.EnviarFacturaAsync("<xml/>", "13100000001E3100000001.xml", false, 500_000m);

        Assert.True(respuesta.EsExitoso);
        Assert.Equal("d2b6e27c-3908-46f3-afaa-2207b9501b4b", respuesta.TrackId);
    }

    // --- Ayudantes ---

    private static DgiiConfig ConfigReal() => new() { ModoSimulador = false, TimeoutSeconds = 5 };

    private static (HttpClient Cliente, HttpMessageHandler Handler) CrearCliente(
        string? cuerpo, GrabadorTransmisionesDGII? grabador = null, HttpMessageHandler? handlerReproductor = null)
    {
        var handler = handlerReproductor ?? new HandlerFalsoDeContrato(cuerpo);
        HttpClient cliente;
        if (grabador != null)
        {
            var grabadorHandler = new GrabadorTransmisionesHandler(grabador) { InnerHandler = handler };
            cliente = new HttpClient(grabadorHandler);
        }
        else
        {
            cliente = new HttpClient(handler);
        }
        return (cliente, handler);
    }

    private static List<RegistroTransmisionDGII> LeerCofre(string ruta) =>
        System.Text.Json.JsonSerializer.Deserialize<List<RegistroTransmisionDGII>>(File.ReadAllText(ruta))!;
}

/// <summary>Handler falso mínimo: sirve un cuerpo fijo a cualquier URL.</summary>
internal sealed class HandlerFalsoDeContrato : HttpMessageHandler
{
    private readonly string? _cuerpo;
    public List<(string Metodo, string Url)> Peticiones { get; } = new();

    public HandlerFalsoDeContrato(string? cuerpo) => _cuerpo = cuerpo;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Peticiones.Add((request.Method.Method, request.RequestUri?.ToString() ?? ""));
        var respuesta = new HttpResponseMessage(HttpStatusCode.OK);
        if (_cuerpo != null)
            respuesta.Content = new StringContent(_cuerpo, Encoding.UTF8, "application/json");
        return Task.FromResult(respuesta);
    }
}
