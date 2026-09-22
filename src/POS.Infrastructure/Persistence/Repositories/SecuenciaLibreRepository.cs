using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Pool persistente de secuencias devueltas al stock de numeración tras un rechazo corregible
/// (secuenciaUtilizada=false, sub-fase 5.5).
/// </summary>
/// <remarks>
/// El consumo es una marca dentro de la transacción ambiente del emisor: bajo SQL Server, la
/// transacción de la venta bloquea la fila consumida hasta el commit y el siguiente emisor pasa a la
/// siguiente secuencia (o al contador); en SQLite la serialización es la del propio archivo. El
/// respaldo final es el índice único filtrado de <c>ElectronicInvoices.eNCF</c>: dos comprobantes
/// vigentes jamás comparten número, pase lo que pase con el pool.
/// </remarks>
public class SecuenciaLibreRepository : ISecuenciaLibreRepository
{
    private readonly POSDbContext _context;

    public SecuenciaLibreRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<SecuenciaLibre?> ConsumirAsync(string serie, CancellationToken ct = default)
    {
        // FIFO: las secuencias se reutilizan en el orden en que fueron liberadas. La marca ocurre
        // dentro de la transacción del emisor: un rollback la devuelve al pool.
        var libre = await _context.SecuenciasLibres
            .Where(s => s.Serie == serie && !s.Consumida)
            .OrderBy(s => s.FechaLiberacionUtc)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (libre is null)
            return null;

        libre.Consumida = true;
        libre.FechaConsumoUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return libre;
    }

    public async Task<SecuenciaLibre> LiberarAsync(
        string serie,
        long numero,
        int facturaRechazadaId,
        string motivoRechazo,
        string liberadaPor,
        CancellationToken ct = default)
    {
        // Idempotencia: liberar dos veces la misma secuencia no crea dos filas (p. ej. rechazo
        // consolidado dos veces, o rechazo + consulta tardía que confirma lo mismo).
        var existente = await _context.SecuenciasLibres
            .FirstOrDefaultAsync(s => s.Serie == serie && s.Numero == numero && !s.Consumida, ct);

        if (existente is not null)
            return existente;

        var libre = new SecuenciaLibre
        {
            Serie = serie,
            Numero = numero,
            ENCF = SecuenciaLibre.Formatear(serie, numero),
            FacturaRechazadaId = facturaRechazadaId,
            MotivoRechazo = motivoRechazo,
            LiberadaPor = liberadaPor,
            FechaLiberacionUtc = DateTime.UtcNow,
            Consumida = false
        };

        await _context.SecuenciasLibres.AddAsync(libre, ct);
        await _context.SaveChangesAsync(ct);

        return libre;
    }

    public Task<List<SecuenciaLibre>> ObtenerLibresAsync(string serie, CancellationToken ct = default) =>
        _context.SecuenciasLibres
            .AsNoTracking()
            .Where(s => s.Serie == serie && !s.Consumida)
            .OrderBy(s => s.FechaLiberacionUtc)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);
}
