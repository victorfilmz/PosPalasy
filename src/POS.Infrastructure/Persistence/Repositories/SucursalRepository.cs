using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

public class SucursalRepository : ISucursalRepository
{
    private readonly POSDbContext _context;

    public SucursalRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<Sucursal?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.Sucursales.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Sucursal?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        return await _context.Sucursales.FirstOrDefaultAsync(s => s.CodigoSucursal == codigo, ct);
    }

    public async Task<Sucursal?> GetPrincipalAsync(CancellationToken ct = default)
    {
        // Return first active or branch code 001
        return await _context.Sucursales
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(s => s.EstaActiva, ct);
    }

    public async Task<IEnumerable<Sucursal>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _context.Sucursales
            .Where(s => s.EstaActiva)
            .OrderBy(s => s.Nombre)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Sucursal>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Sucursales
            .OrderBy(s => s.Nombre)
            .ToListAsync(ct);
    }

    public async Task<Sucursal> AddAsync(Sucursal sucursal, CancellationToken ct = default)
    {
        await _context.Sucursales.AddAsync(sucursal, ct);
        await _context.SaveChangesAsync(ct);
        return sucursal;
    }

    public async Task UpdateAsync(Sucursal sucursal, CancellationToken ct = default)
    {
        _context.Sucursales.Update(sucursal);
        await _context.SaveChangesAsync(ct);
    }
}
