using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

public class ProveedorRepository : IProveedorRepository
{
    private readonly POSDbContext _context;

    public ProveedorRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<Proveedor?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.Proveedores.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Proveedor?> GetByRNCAsync(string rnc, CancellationToken ct = default)
    {
        return await _context.Proveedores.FirstOrDefaultAsync(p => p.RNC == rnc, ct);
    }

    public async Task<IEnumerable<Proveedor>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _context.Proveedores
            .Where(p => p.EstaActivo)
            .OrderBy(p => p.RazonSocial)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Proveedor>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Proveedores
            .OrderBy(p => p.RazonSocial)
            .ToListAsync(ct);
    }

    public async Task<Proveedor> AddAsync(Proveedor proveedor, CancellationToken ct = default)
    {
        await _context.Proveedores.AddAsync(proveedor, ct);
        await _context.SaveChangesAsync(ct);
        return proveedor;
    }

    public async Task UpdateAsync(Proveedor proveedor, CancellationToken ct = default)
    {
        _context.Proveedores.Update(proveedor);
        await _context.SaveChangesAsync(ct);
    }
}
