using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POS.Application.Interfaces;
using POS.Domain.Repositories;

namespace POS.UI.Services;

/// <summary>
/// Servicio en segundo plano para el despacho asíncrono y resiliente de facturas electrónicas encoladas cuando la DGII experimenta intermitencia o caída de conexión.
/// </summary>
public class DgiiQueueBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DgiiQueueBackgroundService> _logger;

    public DgiiQueueBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DgiiQueueBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DgiiQueueBackgroundService iniciado. Monitoreando cola de emisión DGII cada 30 segundos.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcesarColaAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error no controlado procesando la cola de emisión DGII.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("DgiiQueueBackgroundService detenido.");
    }

    private async Task ProcesarColaAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueRepo = scope.ServiceProvider.GetRequiredService<IEmisionDGIIQueueRepository>();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IElectronicInvoiceService>();

        var pendientes = await queueRepo.GetPendientesAsync(10, ct);

        foreach (var item in pendientes)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                _logger.LogInformation("Reintentando transmisión diferida a DGII para factura {FacturaId} (eNCF: {eNCF}, Intento: {Intento})",
                    item.FacturaId, item.eNCF, item.Intentos + 1);

                var response = await invoiceService.ReenviarAsync(item.eNCF, ct);

                if (response.Exitoso)
                {
                    await queueRepo.MarcarEnviadoAsync(item.Id, response.TrackId, ct);

                    _logger.LogInformation("Factura {FacturaId} (eNCF: {eNCF}) enviada exitosamente a DGII en segundo plano. TrackId: {TrackId}",
                        item.FacturaId, item.eNCF, response.TrackId);
                }
                else
                {
                    var errorMsg = response.Mensaje ?? "Error desconocido en despacho DGII";
                    await queueRepo.RegistrarFalloAsync(item.Id, errorMsg, ct);
                    _logger.LogWarning("Reintento diferido fallido para factura {FacturaId}: {Error}", item.FacturaId, errorMsg);
                }
            }
            catch (Exception ex)
            {
                await queueRepo.RegistrarFalloAsync(item.Id, ex.Message, ct);
                _logger.LogWarning(ex, "Excepción reintentando despacho de factura {FacturaId} a DGII: {Message}", item.FacturaId, ex.Message);
            }
        }
    }
}
