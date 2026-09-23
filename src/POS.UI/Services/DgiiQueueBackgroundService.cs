using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POS.Application.Interfaces;
using POS.Domain.Common;
using POS.Domain.Enums;
using POS.Domain.Repositories;

namespace POS.UI.Services;

/// <summary>
/// Despacho en segundo plano de los comprobantes que no pudieron confirmarse al momento de la venta.
/// </summary>
/// <remarks>
/// Propiedades que debe cumplir y que este servicio implementa:
/// <list type="bullet">
/// <item>Solo toma elementos elegibles: sin enviar, no definitivos, fuera de lease y de su ventana de
/// espera progresiva.</item>
/// <item>La exclusión mutua no depende del intervalo del bucle: el elemento se reclama con lease
/// atómico dentro del propio envío, de modo que el envío inmediato de la venta y este trabajador
/// nunca transmiten el mismo comprobante a la vez.</item>
/// <item>Si el proceso muere, el lease vence y el documento se retoma: nada queda a medias.</item>
/// <item>Un error permanente no provoca reintentos automáticos infinitos.</item>
/// </list>
/// </remarks>
public class DgiiQueueBackgroundService : BackgroundService
{
    private static readonly TimeSpan IntervaloEntrePasadas = TimeSpan.FromSeconds(15);
    private const int MaximoPorPasada = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DgiiQueueBackgroundService> _logger;

    /// <summary>
    /// Último ciclo completado (UTC). Lo consulta el health check: si el worker lleva más de
    /// 3 intervalos sin completar un ciclo, el sistema está degradado aunque responda HTTP.
    /// </summary>
    public DateTimeOffset? UltimoCicloCompletadoUtc { get; private set; }

    public DgiiQueueBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DgiiQueueBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DgiiQueueBackgroundService iniciado. Revisa la cola de emisión DGII cada {Segundos} segundos.",
            IntervaloEntrePasadas.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcesarColaAsync(stoppingToken);
                UltimoCicloCompletadoUtc = DateTimeOffset.UtcNow;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error no controlado procesando la cola de emisión DGII.");
            }

            try
            {
                await Task.Delay(IntervaloEntrePasadas, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("DgiiQueueBackgroundService detenido.");
    }

    /// <summary>
    /// Alerta de agotamiento de secuencias (producción): si una serie consumió el 90% de su rango
    /// autorizado, avisa ANTES de que la venta falle en pleno pico con SECUENCIA_AGOTADA. Aviso
    /// como Warning en cada ciclo (el worker corre cada 15 s; el log diario rota solo).
    /// </summary>
    private async Task RevisarAgotamientoSecuenciasAsync(IServiceScope scope, CancellationToken ct)
    {
        var secuenciaRepo = scope.ServiceProvider.GetRequiredService<ISecuenciaECFRepository>();
        foreach (var serie in await secuenciaRepo.ObtenerTodasAsync(ct))
        {
            if (!serie.HastaAutorizado.HasValue) continue;
            var desde = serie.DesdeAutorizado ?? 1;
            var rango = serie.HastaAutorizado.Value - desde + 1;
            if (rango <= 0) continue;

            var restantes = serie.HastaAutorizado.Value - serie.Ultimo;
            if (restantes <= rango * 0.1)
            {
                _logger.LogWarning(
                    "SECUENCIAS POR AGOTAR: la serie {Serie} (tipo {Tipo}) tiene {Restantes} de {Rango} números disponibles " +
                    "({Porcentaje:P0} restante). Solicite nuevas secuencias a la DGII antes de facturar con error SECUENCIA_AGOTADA.",
                    serie.Serie, serie.TipoECF, restantes, rango, (double)restantes / rango);
            }
        }
    }

    private async Task ProcesarColaAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueRepo = scope.ServiceProvider.GetRequiredService<IEmisionDGIIQueueRepository>();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IElectronicInvoiceService>();

        await RevisarAgotamientoSecuenciasAsync(scope, ct);

        var ahora = DateTime.UtcNow;
        var pendientes = await queueRepo.ObtenerElegiblesAsync(MaximoPorPasada, ahora, ct);

        if (pendientes.Count == 0)
            return;

        _logger.LogInformation("Cola DGII: {Cantidad} comprobante(s) elegible(s) para transmisión.", pendientes.Count);

        foreach (var item in pendientes)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                // El lease lo toma el propio envío; si otro proceso ya lo tiene, aquí no se hace nada.
                var response = await invoiceService.EnviarAsync(item.FacturaId, ct);

                if (response.Exitoso)
                {
                    _logger.LogInformation(
                        "Comprobante {eNCF} confirmado por la DGII en segundo plano. TrackId: {TrackId}",
                        item.eNCF,
                        response.TrackId);
                }
                else if (response.EsRecuperable)
                {
                    _logger.LogWarning(
                        "Transmisión diferida no confirmada para {eNCF} (recuperable): {Mensaje}",
                        item.eNCF,
                        response.Mensaje);
                }
                else
                {
                    _logger.LogError(
                        "Transmisión diferida rechazada para {eNCF} (no recuperable): {Mensaje}",
                        item.eNCF,
                        response.Mensaje);
                }
            }
            catch (ReglaDeNegocioException ex)
            {
                // Documento inexistente o anulado: reintentar no tiene sentido.
                await queueRepo.MarcarFalloAsync(
                    item.Id,
                    ex.Message,
                    null,
                    esRecuperable: false,
                    proximoIntentoUtc: null,
                    ct);

                _logger.LogError(ex, "Elemento de cola {Id} marcado como definitivo: {Mensaje}", item.Id, ex.Message);
            }
            catch (Exception ex)
            {
                var proximo = PoliticaReintentoCola.ProximoIntento(
                    item.Intentos + 1,
                    DateTime.UtcNow,
                    item.Id);

                await queueRepo.MarcarFalloAsync(
                    item.Id,
                    ex.Message,
                    null,
                    esRecuperable: true,
                    proximoIntentoUtc: proximo,
                    ct);

                _logger.LogWarning(
                    ex,
                    "Fallo al transmitir el comprobante {eNCF}. Próximo intento: {Proximo} UTC.",
                    item.eNCF,
                    proximo);
            }
        }
    }
}
