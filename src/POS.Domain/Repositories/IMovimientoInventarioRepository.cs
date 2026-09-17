using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

/// <summary>
/// Contrato de repositorio para trazabilidad y movimientos de inventario (Kardex).
/// </summary>
public interface IMovimientoInventarioRepository
{
    Task<MovimientoInventario> AddAsync(MovimientoInventario movimiento, CancellationToken ct = default);
    Task<IEnumerable<MovimientoInventario>> GetByProductoIdAsync(int productoId, CancellationToken ct = default);
    Task<IEnumerable<MovimientoInventario>> GetBySucursalIdAsync(int sucursalId, int take = 100, CancellationToken ct = default);
    Task<IEnumerable<MovimientoInventario>> GetUltimosMovimientosAsync(int take = 100, CancellationToken ct = default);
}
