using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

/// <summary>
/// Resultado de un descuento de existencias ejecutado de forma atómica en la base de datos.
/// </summary>
/// <param name="Aplicado">True si el stock fue modificado.</param>
/// <param name="StockResultante">Stock después de la operación (0 si no se aplicó).</param>
/// <param name="ExistenciaRegistrada">False cuando no existía fila de inventario para el producto/sucursal.</param>
public sealed record ResultadoDescuentoStock(bool Aplicado, decimal StockResultante, bool ExistenciaRegistrada);

public interface IInventarioAlmacenRepository
{
    Task<InventarioAlmacen?> GetByProductAndSucursalAsync(int productoId, int sucursalId, CancellationToken ct = default);

    /// <summary>
    /// Descuenta existencias con una única sentencia condicional en la base de datos, de modo que dos
    /// ventas simultáneas de la última unidad nunca produzcan stock negativo cuando la política es
    /// estricta (<paramref name="permitirStockNegativo"/> = false).
    /// </summary>
    Task<ResultadoDescuentoStock> DescontarStockAsync(
        int productoId,
        int sucursalId,
        decimal cantidad,
        bool permitirStockNegativo,
        CancellationToken ct = default);
    Task<IEnumerable<InventarioAlmacen>> GetBySucursalAsync(int sucursalId, CancellationToken ct = default);
    Task<IEnumerable<InventarioAlmacen>> GetByProductoAsync(int productoId, CancellationToken ct = default);
    Task<IEnumerable<InventarioAlmacen>> GetAllAsync(CancellationToken ct = default);
    Task<decimal> GetStockAsync(int productoId, int sucursalId, CancellationToken ct = default);
    Task SetStockAsync(int productoId, int sucursalId, decimal nuevoStock, decimal? stockMinimo = null, CancellationToken ct = default);
    Task UpdateStockAsync(int productoId, int sucursalId, decimal deltaCantidad, CancellationToken ct = default);
    Task<InventarioAlmacen> AddAsync(InventarioAlmacen inventario, CancellationToken ct = default);
    Task UpdateAsync(InventarioAlmacen inventario, CancellationToken ct = default);
}
