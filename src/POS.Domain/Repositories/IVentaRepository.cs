using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

/// <summary>
/// Contrato de repositorio para ventas del POS.
/// </summary>
public interface IVentaRepository
{
    Task<Venta?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Busca la venta previamente registrada con la misma clave de idempotencia. Es la barrera
    /// contra doble clic, doble POST, refresh y reintentos del cliente.
    /// </summary>
    Task<Venta?> GetByClaveIdempotenciaAsync(Guid clave, CancellationToken ct = default);
    Task<Venta?> GetWithItemsAsync(int id, CancellationToken ct = default);
    Task<IEnumerable<Venta>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IEnumerable<Venta>> GetPendingElectronicInvoicingAsync(CancellationToken ct = default);

    Task<Venta> AddAsync(Venta venta, CancellationToken ct = default);
    Task UpdateAsync(Venta venta, CancellationToken ct = default);
}
