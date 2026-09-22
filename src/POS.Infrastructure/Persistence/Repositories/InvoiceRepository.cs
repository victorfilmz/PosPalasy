using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Domain.Types;

namespace POS.Infrastructure.Persistence.Repositories;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly POSDbContext _context;

    public InvoiceRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<ElectronicInvoice?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices
            .Include(i => i.Items)
            .Include(i => i.Devolucion)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<ElectronicInvoice?> GetByENCFAsync(string encf, CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.eNCF == encf, ct);
    }

    public async Task<ElectronicInvoice?> GetByTrackIdAsync(string trackId, CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.TrackId == trackId, ct);
    }

    public async Task<ElectronicInvoice?> GetByVentaIdAsync(int ventaId, CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.VentaId == ventaId, ct);
    }

    public async Task<ElectronicInvoice?> GetByDevolucionIdAsync(int devolucionId, CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.DevolucionId == devolucionId, ct);
    }

    public async Task<IEnumerable<ElectronicInvoice>> GetByRNCAsync(string rnc, TipoeCFType? tipo = null, CancellationToken ct = default)
    {
        var query = _context.ElectronicInvoices.Where(i => i.RNCEmisor == rnc);
        if (tipo.HasValue)
        {
            query = query.Where(i => i.TipoeCF == tipo.Value);
        }
        return await query.ToListAsync(ct);
    }

    public async Task<IEnumerable<ElectronicInvoice>> GetByEstadoAsync(EstadoFacturaElectronica estado, CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices
            .Where(i => i.Estado == estado)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<ElectronicInvoice>> GetPendingAsync(CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices
            .Where(i => i.Estado == EstadoFacturaElectronica.EnProceso || i.Estado == EstadoFacturaElectronica.PendienteReenvio)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsENCFAsync(string encf, CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices.AnyAsync(i => i.eNCF == encf, ct);
    }

    public async Task<string?> GetLastENCFAsync(string serie, CancellationToken ct = default)
    {
        return await _context.ElectronicInvoices
            .Where(i => i.eNCF.StartsWith(serie))
            .OrderByDescending(i => i.eNCF)
            .Select(i => i.eNCF)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ElectronicInvoice> AddAsync(ElectronicInvoice invoice, CancellationToken ct = default)
    {
        await _context.ElectronicInvoices.AddAsync(invoice, ct);
        await _context.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task UpdateAsync(ElectronicInvoice invoice, CancellationToken ct = default)
    {
        _context.ElectronicInvoices.Update(invoice);
        await _context.SaveChangesAsync(ct);
    }
}
