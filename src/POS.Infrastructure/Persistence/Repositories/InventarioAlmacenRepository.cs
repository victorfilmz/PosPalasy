using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

public class InventarioAlmacenRepository : IInventarioAlmacenRepository
{
    private readonly POSDbContext _context;

    public InventarioAlmacenRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<InventarioAlmacen?> GetByProductAndSucursalAsync(int productoId, int sucursalId, CancellationToken ct = default)
    {
        return await _context.InventariosAlmacen
            .Include(i => i.Producto)
            .Include(i => i.Sucursal)
            .FirstOrDefaultAsync(i => i.ProductoId == productoId && i.SucursalId == sucursalId, ct);
    }

    public async Task<IEnumerable<InventarioAlmacen>> GetBySucursalAsync(int sucursalId, CancellationToken ct = default)
    {
        return await _context.InventariosAlmacen
            .Include(i => i.Producto)
            .Include(i => i.Sucursal)
            .Where(i => i.SucursalId == sucursalId)
            .OrderBy(i => i.Producto != null ? i.Producto.Descripcion : "")
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<InventarioAlmacen>> GetByProductoAsync(int productoId, CancellationToken ct = default)
    {
        return await _context.InventariosAlmacen
            .Include(i => i.Sucursal)
            .Where(i => i.ProductoId == productoId)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<InventarioAlmacen>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.InventariosAlmacen
            .Include(i => i.Producto)
            .Include(i => i.Sucursal)
            .OrderBy(i => i.SucursalId)
            .ThenBy(i => i.Producto != null ? i.Producto.Descripcion : "")
            .ToListAsync(ct);
    }

    public async Task<ResultadoDescuentoStock> DescontarStockAsync(
        int productoId,
        int sucursalId,
        decimal cantidad,
        bool permitirStockNegativo,
        CancellationToken ct = default)
    {
        if (cantidad <= 0)
            throw new ReglaDeNegocioException(
                "La cantidad a descontar debe ser mayor que cero.",
                "CANTIDAD_INVALIDA");

        // Una única sentencia condicional: la comprobación y el descuento son la misma operación,
        // por lo que dos ventas simultáneas de la última unidad no pueden pasar ambas el control.
        var consulta = _context.InventariosAlmacen
            .Where(i => i.ProductoId == productoId && i.SucursalId == sucursalId);

        if (!permitirStockNegativo)
            consulta = consulta.Where(i => i.StockActual >= cantidad);

        var filas = await consulta.ExecuteUpdateAsync(setters => setters
            .SetProperty(i => i.StockActual, i => i.StockActual - cantidad)
            .SetProperty(i => i.UpdatedAt, _ => DateTime.UtcNow), ct);

        if (filas == 0)
        {
            var existenciaRegistrada = await _context.InventariosAlmacen
                .AnyAsync(i => i.ProductoId == productoId && i.SucursalId == sucursalId, ct);

            if (!permitirStockNegativo)
            {
                var stockActual = existenciaRegistrada
                    ? await _context.InventariosAlmacen
                        .Where(i => i.ProductoId == productoId && i.SucursalId == sucursalId)
                        .Select(i => i.StockActual)
                        .FirstAsync(ct)
                    : 0m;

                return new ResultadoDescuentoStock(false, stockActual, existenciaRegistrada);
            }

            // Política permisiva: se admite vender sin existencias registradas; se crea la fila.
            await _context.InventariosAlmacen.AddAsync(new InventarioAlmacen
            {
                ProductoId = productoId,
                SucursalId = sucursalId,
                StockActual = -cantidad,
                StockMinimo = 5m
            }, ct);
            await _context.SaveChangesAsync(ct);

            return new ResultadoDescuentoStock(true, -cantidad, false);
        }

        var resultante = await _context.InventariosAlmacen
            .AsNoTracking()
            .Where(i => i.ProductoId == productoId && i.SucursalId == sucursalId)
            .Select(i => i.StockActual)
            .FirstAsync(ct);

        return new ResultadoDescuentoStock(true, resultante, true);
    }

    public async Task<decimal> GetStockAsync(int productoId, int sucursalId, CancellationToken ct = default)
    {
        var inv = await _context.InventariosAlmacen
            .FirstOrDefaultAsync(i => i.ProductoId == productoId && i.SucursalId == sucursalId, ct);
        return inv?.StockActual ?? 0m;
    }

    public async Task SetStockAsync(int productoId, int sucursalId, decimal nuevoStock, decimal? stockMinimo = null, CancellationToken ct = default)
    {
        var inv = await _context.InventariosAlmacen
            .FirstOrDefaultAsync(i => i.ProductoId == productoId && i.SucursalId == sucursalId, ct);

        if (inv == null)
        {
            inv = new InventarioAlmacen
            {
                ProductoId = productoId,
                SucursalId = sucursalId,
                StockActual = nuevoStock,
                StockMinimo = stockMinimo ?? 5m
            };
            await _context.InventariosAlmacen.AddAsync(inv, ct);
        }
        else
        {
            inv.StockActual = nuevoStock;
            if (stockMinimo.HasValue)
                inv.StockMinimo = stockMinimo.Value;
            _context.InventariosAlmacen.Update(inv);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateStockAsync(int productoId, int sucursalId, decimal deltaCantidad, CancellationToken ct = default)
    {
        var inv = await _context.InventariosAlmacen
            .FirstOrDefaultAsync(i => i.ProductoId == productoId && i.SucursalId == sucursalId, ct);

        if (inv == null)
        {
            inv = new InventarioAlmacen
            {
                ProductoId = productoId,
                SucursalId = sucursalId,
                StockActual = deltaCantidad,
                StockMinimo = 5m
            };
            await _context.InventariosAlmacen.AddAsync(inv, ct);
        }
        else
        {
            inv.StockActual += deltaCantidad;
            _context.InventariosAlmacen.Update(inv);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task<InventarioAlmacen> AddAsync(InventarioAlmacen inventario, CancellationToken ct = default)
    {
        await _context.InventariosAlmacen.AddAsync(inventario, ct);
        await _context.SaveChangesAsync(ct);
        return inventario;
    }

    public async Task UpdateAsync(InventarioAlmacen inventario, CancellationToken ct = default)
    {
        _context.InventariosAlmacen.Update(inventario);
        await _context.SaveChangesAsync(ct);
    }
}
