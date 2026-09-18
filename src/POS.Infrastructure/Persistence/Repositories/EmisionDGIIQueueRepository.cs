using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Cola de emisión hacia la DGII con reclamo por lease, espera progresiva y clasificación de errores.
/// </summary>
public class EmisionDGIIQueueRepository : IEmisionDGIIQueueRepository
{
    private readonly POSDbContext _context;

    public EmisionDGIIQueueRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<EmisionDGIIQueue> AddAsync(EmisionDGIIQueue item, CancellationToken ct = default)
    {
        await _context.EmisionesDGIIQueue.AddAsync(item, ct);
        await _context.SaveChangesAsync(ct);
        return item;
    }

    public async Task<EmisionDGIIQueue?> GetPendientePorFacturaAsync(int facturaId, CancellationToken ct = default)
    {
        return await _context.EmisionesDGIIQueue
            .Where(q => q.FacturaId == facturaId
                && !q.EnviadoExitosamente
                && q.Estado != EstadoColaDGII.Enviado
                && q.Estado != EstadoColaDGII.Definitivo)
            .OrderByDescending(q => q.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<EmisionDGIIQueue>> ObtenerElegiblesAsync(
        int maxItems,
        DateTime ahoraUtc,
        CancellationToken ct = default)
    {
        return await _context.EmisionesDGIIQueue
            .Where(q => !q.EnviadoExitosamente
                && q.Estado != EstadoColaDGII.Enviado
                && q.Estado != EstadoColaDGII.Definitivo
                && q.Intentos < PoliticaReintentoCola.IntentosMaximos
                && (q.ProximoIntentoUtc == null || q.ProximoIntentoUtc <= ahoraUtc)
                && (q.LeaseHastaUtc == null || q.LeaseHastaUtc <= ahoraUtc))
            .OrderBy(q => q.FechaRegistro)
            .Take(maxItems)
            .ToListAsync(ct);
    }

    public async Task<bool> ReclamarAsync(
        int id,
        string leaseToken,
        DateTime ahoraUtc,
        CancellationToken ct = default)
    {
        var vencimiento = ahoraUtc.Add(PoliticaReintentoCola.DuracionLease);

        var filas = await _context.EmisionesDGIIQueue
            .Where(q => q.Id == id
                && !q.EnviadoExitosamente
                && q.Estado != EstadoColaDGII.Enviado
                && q.Estado != EstadoColaDGII.Definitivo
                && (q.LeaseHastaUtc == null || q.LeaseHastaUtc <= ahoraUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(q => q.Estado, EstadoColaDGII.EnProceso)
                .SetProperty(q => q.LeaseToken, leaseToken)
                .SetProperty(q => q.LeaseHastaUtc, vencimiento)
                .SetProperty(q => q.Intentos, q => q.Intentos + 1)
                .SetProperty(q => q.FechaUltimoIntento, ahoraUtc)
                .SetProperty(q => q.UpdatedAt, _ => ahoraUtc), ct);

        return filas == 1;
    }

    public async Task MarcarEnviadoAsync(
        int id,
        string? trackId,
        int? codigoHttp,
        CancellationToken ct = default)
    {
        var ahoraUtc = DateTime.UtcNow;

        await _context.EmisionesDGIIQueue
            .Where(q => q.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(q => q.EnviadoExitosamente, true)
                .SetProperty(q => q.Estado, EstadoColaDGII.Enviado)
                .SetProperty(q => q.TrackId, trackId)
                .SetProperty(q => q.UltimoCodigoHttp, codigoHttp)
                .SetProperty(q => q.ProximoIntentoUtc, (DateTime?)null)
                .SetProperty(q => q.LeaseToken, (string?)null)
                .SetProperty(q => q.LeaseHastaUtc, (DateTime?)null)
                .SetProperty(q => q.FechaUltimoIntento, ahoraUtc)
                .SetProperty(q => q.UpdatedAt, _ => ahoraUtc), ct);
    }

    public async Task MarcarFalloAsync(
        int id,
        string error,
        int? codigoHttp,
        bool esRecuperable,
        DateTime? proximoIntentoUtc,
        CancellationToken ct = default)
    {
        var ahoraUtc = DateTime.UtcNow;
        var detalle = error.Length > 500 ? error[..500] : error;

        await _context.EmisionesDGIIQueue
            .Where(q => q.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(q => q.UltimoError, detalle)
                .SetProperty(q => q.UltimoCodigoHttp, codigoHttp)
                .SetProperty(q => q.FechaUltimoIntento, ahoraUtc)
                // Error permanente ⇒ Definitivo. Recuperable ⇒ Pendiente, o Fallido si agotó intentos.
                .SetProperty(q => q.Estado, q => !esRecuperable
                    ? EstadoColaDGII.Definitivo
                    : q.Intentos >= PoliticaReintentoCola.IntentosMaximos
                        ? EstadoColaDGII.Fallido
                        : EstadoColaDGII.Pendiente)
                .SetProperty(q => q.ProximoIntentoUtc, proximoIntentoUtc)
                .SetProperty(q => q.LeaseToken, (string?)null)
                .SetProperty(q => q.LeaseHastaUtc, (DateTime?)null)
                .SetProperty(q => q.UpdatedAt, _ => ahoraUtc), ct);
    }

    public async Task<int> ContarPendientesAsync(CancellationToken ct = default)
    {
        return await _context.EmisionesDGIIQueue
            .CountAsync(q => !q.EnviadoExitosamente
                && q.Estado != EstadoColaDGII.Enviado
                && q.Estado != EstadoColaDGII.Definitivo, ct);
    }
}
