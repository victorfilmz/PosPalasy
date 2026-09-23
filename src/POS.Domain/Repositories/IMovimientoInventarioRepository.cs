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

    /// <summary>
    /// Movimientos de un tipo dentro de un rango de fechas (inclusive), con Producto/Proveedor
    /// cargados. Soporta los reportes fiscales por período (p. ej. libro de compras 606).
    /// </summary>
    Task<IEnumerable<MovimientoInventario>> GetByTipoYFechaAsync(
        TipoMovimientoInventario tipo, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default);
}
