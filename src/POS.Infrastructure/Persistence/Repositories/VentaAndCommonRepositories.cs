using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

public class VentaRepository : IVentaRepository
{
    private readonly POSDbContext _context;

    public VentaRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<Venta?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Include(v => v.Pagos)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<Venta?> GetWithItemsAsync(int id, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Include(v => v.Pagos)
            .Include(v => v.Items)
            .Include(v => v.Cliente)
            .Include(v => v.ElectronicInvoice)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<IEnumerable<Venta>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _context.Ventas
            .Include(v => v.Pagos)
            .Include(v => v.Items)
            .Where(v => v.Fecha >= from && v.Fecha <= to)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Venta>> GetPendingElectronicInvoicingAsync(CancellationToken ct = default)
    {
        return await _context.Ventas
            .Include(v => v.Pagos)
            .Include(v => v.Items)
            .Include(v => v.Cliente)
            .Where(v => v.ElectronicInvoice == null)
            .ToListAsync(ct);
    }

    public async Task<Venta?> GetByClaveIdempotenciaAsync(Guid clave, CancellationToken ct = default)
    {
        if (clave == Guid.Empty)
            return null;

        return await _context.Ventas
            .Include(v => v.Pagos)
            .Include(v => v.Items)
            .Include(v => v.ElectronicInvoice)
            .FirstOrDefaultAsync(v => v.ClaveIdempotencia == clave, ct);
    }

    public async Task<Venta> AddAsync(Venta venta, CancellationToken ct = default)
    {
        await _context.Ventas.AddAsync(venta, ct);
        await _context.SaveChangesAsync(ct);
        return venta;
    }

    public async Task UpdateAsync(Venta venta, CancellationToken ct = default)
    {
        _context.Ventas.Update(venta);
        await _context.SaveChangesAsync(ct);
    }
}

public class CommonRepositories : IProductoRepository, IClienteRepository, IEnterpriseRepository, IAnulacionRepository, IMovimientoInventarioRepository
{
    private readonly POSDbContext _context;

    public CommonRepositories(POSDbContext context)
    {
        _context = context;
    }

    // Producto
    async Task<Producto?> IProductoRepository.GetByIdAsync(int id, CancellationToken ct) =>
        await _context.Productos.FirstOrDefaultAsync(p => p.Id == id, ct);

    async Task<Producto?> IProductoRepository.GetByCodigoAsync(string codigo, CancellationToken ct) =>
        await _context.Productos.FirstOrDefaultAsync(p => p.Codigo == codigo, ct);

    async Task<IEnumerable<Producto>> IProductoRepository.GetAllActiveAsync(CancellationToken ct) =>
        await _context.Productos.Where(p => p.EstaActivo).OrderBy(p => p.Descripcion).ToListAsync(ct);

    async Task<IEnumerable<Producto>> IProductoRepository.GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await _context.Productos.Where(p => lista.Contains(p.Id)).ToListAsync(ct);
    }

    async Task<IEnumerable<Producto>> IProductoRepository.GetAllAsync(CancellationToken ct) =>
        await _context.Productos.OrderBy(p => p.Descripcion).ToListAsync(ct);

    async Task<Producto> IProductoRepository.AddAsync(Producto producto, CancellationToken ct)
    {
        await _context.Productos.AddAsync(producto, ct);
        await _context.SaveChangesAsync(ct);
        return producto;
    }

    async Task IProductoRepository.UpdateAsync(Producto producto, CancellationToken ct)
    {
        _context.Productos.Update(producto);
        await _context.SaveChangesAsync(ct);
    }

    async Task IProductoRepository.DeleteAsync(int id, CancellationToken ct)
    {
        var prod = await _context.Productos.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (prod != null)
        {
            var hasVentas = await _context.VentaItems.AnyAsync(vi => vi.ProductoId == id, ct);
            if (hasVentas)
            {
                prod.EstaActivo = false;
                _context.Productos.Update(prod);
            }
            else
            {
                var movs = await _context.MovimientosInventario.Where(m => m.ProductoId == id).ToListAsync(ct);
                if (movs.Any())
                {
                    _context.MovimientosInventario.RemoveRange(movs);
                }
                _context.Productos.Remove(prod);
            }
            await _context.SaveChangesAsync(ct);
        }
    }

    async Task<bool> IProductoRepository.HasVentasAsync(int productoId, CancellationToken ct) =>
        await _context.VentaItems.AnyAsync(vi => vi.ProductoId == productoId, ct);

    // MovimientoInventario (Kardex)
    async Task<MovimientoInventario> IMovimientoInventarioRepository.AddAsync(MovimientoInventario movimiento, CancellationToken ct)
    {
        await _context.MovimientosInventario.AddAsync(movimiento, ct);
        await _context.SaveChangesAsync(ct);
        return movimiento;
    }

    async Task<IEnumerable<MovimientoInventario>> IMovimientoInventarioRepository.GetByProductoIdAsync(int productoId, CancellationToken ct) =>
        await _context.MovimientosInventario
            .Include(m => m.Producto)
            .Include(m => m.Sucursal)
            .Include(m => m.Proveedor)
            .Where(m => m.ProductoId == productoId)
            .OrderByDescending(m => m.Fecha)
            .ToListAsync(ct);

    async Task<IEnumerable<MovimientoInventario>> IMovimientoInventarioRepository.GetBySucursalIdAsync(int sucursalId, int take, CancellationToken ct) =>
        await _context.MovimientosInventario
            .Include(m => m.Producto)
            .Include(m => m.Sucursal)
            .Include(m => m.Proveedor)
            .Where(m => m.SucursalId == sucursalId)
            .OrderByDescending(m => m.Fecha)
            .Take(take)
            .ToListAsync(ct);

    async Task<IEnumerable<MovimientoInventario>> IMovimientoInventarioRepository.GetUltimosMovimientosAsync(int take, CancellationToken ct) =>
        await _context.MovimientosInventario
            .Include(m => m.Producto)
            .Include(m => m.Sucursal)
            .Include(m => m.Proveedor)
            .OrderByDescending(m => m.Fecha)
            .Take(take)
            .ToListAsync(ct);

    // Cliente
    async Task<Cliente?> IClienteRepository.GetByIdAsync(int id, CancellationToken ct) =>
        await _context.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct);

    async Task<Cliente?> IClienteRepository.GetByRNCAsync(string rnc, CancellationToken ct) =>
        await _context.Clientes.FirstOrDefaultAsync(c => c.RNC == rnc, ct);

    async Task<IEnumerable<Cliente>> IClienteRepository.GetAllAsync(CancellationToken ct) =>
        await _context.Clientes.ToListAsync(ct);

    async Task<Cliente> IClienteRepository.AddAsync(Cliente cliente, CancellationToken ct)
    {
        await _context.Clientes.AddAsync(cliente, ct);
        await _context.SaveChangesAsync(ct);
        return cliente;
    }

    async Task IClienteRepository.UpdateAsync(Cliente cliente, CancellationToken ct)
    {
        _context.Clientes.Update(cliente);
        await _context.SaveChangesAsync(ct);
    }

    // Enterprise
    async Task<Enterprise?> IEnterpriseRepository.GetDefaultAsync(CancellationToken ct) =>
        await _context.Enterprises.Include(e => e.Sucursales).FirstOrDefaultAsync(e => e.EstaActiva, ct);

    async Task<Enterprise?> IEnterpriseRepository.GetByRNCAsync(string rnc, CancellationToken ct) =>
        await _context.Enterprises.Include(e => e.Sucursales).FirstOrDefaultAsync(e => e.RNC == rnc, ct);

    async Task<Enterprise> IEnterpriseRepository.AddAsync(Enterprise enterprise, CancellationToken ct)
    {
        await _context.Enterprises.AddAsync(enterprise, ct);
        await _context.SaveChangesAsync(ct);
        return enterprise;
    }

    async Task IEnterpriseRepository.UpdateAsync(Enterprise enterprise, CancellationToken ct)
    {
        _context.Enterprises.Update(enterprise);
        await _context.SaveChangesAsync(ct);
    }

    // Anulacion
    async Task<Anulacion?> IAnulacionRepository.GetByIdAsync(int id, CancellationToken ct) =>
        await _context.Anulaciones.FirstOrDefaultAsync(a => a.Id == id, ct);

    async Task<Anulacion?> IAnulacionRepository.GetByTrackIdAsync(string trackId, CancellationToken ct) =>
        await _context.Anulaciones.FirstOrDefaultAsync(a => a.TrackId == trackId, ct);

    async Task<IEnumerable<Anulacion>> IAnulacionRepository.GetAllAsync(CancellationToken ct) =>
        await _context.Anulaciones.OrderByDescending(a => a.FechaSolicitud).ToListAsync(ct);

    async Task<Anulacion> IAnulacionRepository.AddAsync(Anulacion anulacion, CancellationToken ct)
    {
        await _context.Anulaciones.AddAsync(anulacion, ct);
        await _context.SaveChangesAsync(ct);
        return anulacion;
    }

    async Task IAnulacionRepository.UpdateAsync(Anulacion anulacion, CancellationToken ct)
    {
        _context.Anulaciones.Update(anulacion);
        await _context.SaveChangesAsync(ct);
    }
}
