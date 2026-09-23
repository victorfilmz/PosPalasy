using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using POS.Infrastructure.Services;

namespace POS.UI.Services;

/// <summary>
/// Health check del certificado digital: el sistema puede cargarlo y no está vencido.
/// Degradado (no Unhealthy) si el certificado falta o expiró: la app sigue operativa para
/// ventas locales — los comprobantes quedan en cola con reintento por diseño.
/// </summary>
public sealed class CertificadoDigitalHealthCheck : IHealthCheck
{
    private readonly IProveedorCertificadoDigital _proveedor;

    public CertificadoDigitalHealthCheck(IProveedorCertificadoDigital proveedor) => _proveedor = proveedor;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var certificado = _proveedor.ObtenerCertificado();
            if (certificado is null)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Sin certificado digital: los comprobantes no se firman y quedan en cola."));
            }

            if (certificado.NotAfter < DateTime.UtcNow)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Certificado digital vencido desde {certificado.NotAfter:dd-MM-yyyy}."));
            }

            var dias = (certificado.NotAfter - DateTime.UtcNow).TotalDays;
            return Task.FromResult(dias <= 30
                ? HealthCheckResult.Degraded($"Certificado digital vence en {dias:F0} días ({certificado.NotAfter:dd-MM-yyyy}).")
                : HealthCheckResult.Healthy($"Certificado digital vigente hasta {certificado.NotAfter:dd-MM-yyyy}."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Error al cargar el certificado: " + ex.Message));
        }
    }
}

/// <summary>
/// Health check del worker de la cola DGII: si lleva más de 3 intervalos sin completar un ciclo,
/// los comprobantes dejan de salir aunque la app responda HTTP. Degradado, no Unhealthy: el
/// siguiente envío manual de una venta seguirá intentando transmitir.
/// </summary>
public sealed class WorkerColaDgiiHealthCheck : IHealthCheck
{
    private readonly DgiiQueueBackgroundService? _worker;

    public WorkerColaDgiiHealthCheck(DgiiQueueBackgroundService? worker) => _worker = worker;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_worker is null)
            return Task.FromResult(HealthCheckResult.Degraded("Worker de la cola DGII no registrado."));

        var ultimo = _worker.UltimoCicloCompletadoUtc;
        if (ultimo is null)
            return Task.FromResult(HealthCheckResult.Healthy("Worker de la cola DGII arrancando."));

        var desdeUltimo = DateTimeOffset.UtcNow - ultimo.Value;
        return desdeUltimo > TimeSpan.FromSeconds(45)
            ? Task.FromResult(HealthCheckResult.Degraded($"Sin ciclo completo hace {desdeUltimo.TotalSeconds:F0} s."))
            : Task.FromResult(HealthCheckResult.Healthy("Worker de la cola DGII activo."));
    }
}
