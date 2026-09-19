using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

public class DevolucionRepository : IDevolucionRepository
{
    private readonly POSDbContext _context;

    public DevolucionRepository(POSDbContext context)
    {
        _context = context;
    }

    public Task<Devolucion?> GetByClaveIdempotenciaAsync(Guid clave, CancellationToken ct = default)
    {
        if (clave == Guid.Empty)
            return Task.FromResult<Devolucion?>(null);

        return _context.Devoluciones
            .Include(d => d.Items)
            .Include(d => d.Venta)
                .ThenInclude(v => v!.ElectronicInvoice)
            .FirstOrDefaultAsync(d => d.ClaveIdempotencia == clave, ct);
    }

    public async Task<List<Devolucion>> GetByVentaIdAsync(int ventaId, CancellationToken ct = default)
    {
        var devoluciones = await _context.Devoluciones
            .Include(d => d.Items)
            .Where(d => d.VentaId == ventaId)
            .ToListAsync(ct);

        return devoluciones;
    }

    public async Task<Devolucion> AddAsync(Devolucion devolucion, CancellationToken ct = default)
    {
        await _context.Devoluciones.AddAsync(devolucion, ct);
        await _context.SaveChangesAsync(ct);
        return devolucion;
    }

    /// <summary>
    /// Escritura nula sobre la fila de la venta (<c>Fecha = Fecha</c>: sin cambio de datos ni de la
    /// fecha fiscal). En SQL Server toma el bloqueo exclusivo de la fila hasta el fin de la
    /// transacción; en SQLite la primera escritura ocupa el archivo. Es el mismo patrón de
    /// serialización que la numeración eNCF (Fase 3), aplicado al tope de reembolso por venta.
    /// </summary>
    public Task AnclarVentaParaDevolucionAsync(int ventaId, CancellationToken ct = default) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Ventas] SET [Fecha] = [Fecha] WHERE [Id] = {ventaId}", ct);
}
