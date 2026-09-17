using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

public interface IInventarioAlmacenRepository
{
    Task<InventarioAlmacen?> GetByProductAndSucursalAsync(int productoId, int sucursalId, CancellationToken ct = default);
    Task<IEnumerable<InventarioAlmacen>> GetBySucursalAsync(int sucursalId, CancellationToken ct = default);
    Task<IEnumerable<InventarioAlmacen>> GetByProductoAsync(int productoId, CancellationToken ct = default);
    Task<IEnumerable<InventarioAlmacen>> GetAllAsync(CancellationToken ct = default);
    Task<decimal> GetStockAsync(int productoId, int sucursalId, CancellationToken ct = default);
    Task SetStockAsync(int productoId, int sucursalId, decimal nuevoStock, decimal? stockMinimo = null, CancellationToken ct = default);
    Task UpdateStockAsync(int productoId, int sucursalId, decimal deltaCantidad, CancellationToken ct = default);
    Task<InventarioAlmacen> AddAsync(InventarioAlmacen inventario, CancellationToken ct = default);
    Task UpdateAsync(InventarioAlmacen inventario, CancellationToken ct = default);
}
