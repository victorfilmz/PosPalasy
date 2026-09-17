using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

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

    public async Task<IEnumerable<EmisionDGIIQueue>> GetPendientesAsync(int maxItems = 20, CancellationToken ct = default)
    {
        return await _context.EmisionesDGIIQueue
            .Where(q => !q.EnviadoExitosamente && q.Intentos < 10)
            .OrderBy(q => q.FechaRegistro)
            .Take(maxItems)
            .ToListAsync(ct);
    }

    public async Task MarcarEnviadoAsync(int id, string? trackId, CancellationToken ct = default)
    {
        var item = await _context.EmisionesDGIIQueue.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (item != null)
        {
            item.EnviadoExitosamente = true;
            item.FechaUltimoIntento = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(trackId))
            {
                item.UltimoError = null;
            }
            _context.EmisionesDGIIQueue.Update(item);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task RegistrarFalloAsync(int id, string error, CancellationToken ct = default)
    {
        var item = await _context.EmisionesDGIIQueue.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (item != null)
        {
            item.Intentos++;
            item.UltimoError = error;
            item.FechaUltimoIntento = DateTime.UtcNow;
            _context.EmisionesDGIIQueue.Update(item);
            await _context.SaveChangesAsync(ct);
        }
    }
}
