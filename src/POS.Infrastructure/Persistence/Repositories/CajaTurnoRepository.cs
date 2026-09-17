using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

public class CajaTurnoRepository : ICajaTurnoRepository
{
    private readonly POSDbContext _context;

    public CajaTurnoRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<CajaTurno?> GetTurnoActivoAsync(string? cajero = null, CancellationToken ct = default)
    {
        var query = _context.CajaTurnos
            .Include(c => c.Movimientos)
            .Include(c => c.Ventas)
            .Where(c => c.Estado == TurnoCajaEstado.Abierto);

        if (!string.IsNullOrWhiteSpace(cajero))
        {
            query = query.Where(c => c.Cajero == cajero);
        }

        return await query.OrderByDescending(c => c.FechaApertura).FirstOrDefaultAsync(ct);
    }

    public async Task<CajaTurno?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.CajaTurnos
            .Include(c => c.Movimientos)
            .Include(c => c.Ventas)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IEnumerable<CajaTurno>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.CajaTurnos
            .OrderByDescending(c => c.FechaApertura)
            .ToListAsync(ct);
    }

    public async Task<CajaTurno> AddAsync(CajaTurno turno, CancellationToken ct = default)
    {
        await _context.CajaTurnos.AddAsync(turno, ct);
        await _context.SaveChangesAsync(ct);
        return turno;
    }

    public async Task UpdateAsync(CajaTurno turno, CancellationToken ct = default)
    {
        _context.CajaTurnos.Update(turno);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddMovimientoAsync(MovimientoCaja movimiento, CancellationToken ct = default)
    {
        await _context.MovimientosCaja.AddAsync(movimiento, ct);
        await _context.SaveChangesAsync(ct);
    }
}
