using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Registro de auditoría en la tabla AuditoriaCambios. Se escribe dentro de la misma transacción del
/// cambio que documenta: un cambio sin traza (o una traza sin cambio) es un defecto, no un detalle.
/// </summary>
public class AuditoriaRepository : IAuditoriaRepository
{
    private readonly POSDbContext _context;

    public AuditoriaRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task RegistrarAsync(AuditoriaCambio cambio, CancellationToken ct = default)
    {
        await _context.AuditoriaCambios.AddAsync(cambio, ct);
        await _context.SaveChangesAsync(ct);
    }
}
