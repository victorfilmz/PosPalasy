using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using POS.Infrastructure.DGII;

namespace POS.UI.Services;

/// <summary>
/// Health check de conectividad con la DGII, con distinción de modos:
///  - Simulador (DGII:ModoSimulador=true): Healthy — no se necesita red; el cliente DGII
///    responde localmente. Así el /health no marca Degradado por falta de internet en un
///    punto de venta que deliberadamente opera sin DGII real.
///  - Real: prueba activa de red contra el host e-CF del ambiente (HEAD/GET con timeout corto).
///    Healthy si responde; Degradado si no hay conectividad (la app sigue operativa: cola con
///    reintento y contingencia RFCE por diseño).
/// </summary>
public sealed class DgiiConectividadHealthCheck : IHealthCheck
{
    private static readonly TimeSpan TimeoutPrueba = TimeSpan.FromSeconds(4);

    private readonly DgiiConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>true si la app opera con el simulador local (sin red DGII).</summary>
    public bool EsSimulador => _config.ModoSimulador;

    /// <summary>Ambiente DGII configurado (TestECF / CertECF / Produccion).</summary>
    public string Ambiente => _config.Ambiente.ToString();

    /// <summary>Último resultado del check (texto corto para UI); null si nunca corrió.</summary>
    public volatile string? UltimoDetalle;

    public DgiiConectividadHealthCheck(DgiiConfig config, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_config.ModoSimulador)
        {
            UltimoDetalle = "Modo simulador: sin dependencia de red";
            return HealthCheckResult.Healthy(
                $"DGII en MODO SIMULADOR — sin dependencia de red (ambiente configurado: {_config.Ambiente}).");
        }

        var url = _config.HostECF();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeoutPrueba);
            var cliente = _httpClientFactory.CreateClient("DgiiAutenticacion");
            using var respuesta = await cliente.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            // Cualquier respuesta HTTP (aunque sea 4xx/5xx) prueba que hay ruta y DNS hacia la DGII.
            UltimoDetalle = $"Conectividad OK — HTTP {(int)respuesta.StatusCode}";
            return HealthCheckResult.Healthy(
                $"DGII REAL ({_config.Ambiente}) accesible en {url} — HTTP {(int)respuesta.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            UltimoDetalle = $"Sin respuesta de {url} en {TimeoutPrueba.TotalSeconds:F0} s";
            return HealthCheckResult.Degraded(
                $"DGII REAL ({_config.Ambiente}): sin respuesta de {url} en {TimeoutPrueba.TotalSeconds:F0} s — " +
                "las transmisiones quedan en cola con reintento automático.");
        }
        catch (HttpRequestException ex)
        {
            UltimoDetalle = "Sin conectividad hacia " + url;
            return HealthCheckResult.Degraded(
                $"DGII REAL ({_config.Ambiente}): sin conectividad hacia {url} ({ex.Message}) — " +
                "las transmisiones quedan en cola con reintento automático.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"DGII REAL ({_config.Ambiente}): error probando {url}: {ex.Message}");
        }
    }
}
