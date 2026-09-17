using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

public interface IProductoRepository
{
    Task<Producto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Producto?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IEnumerable<Producto>> GetAllActiveAsync(CancellationToken ct = default);
    Task<IEnumerable<Producto>> GetAllAsync(CancellationToken ct = default);
    Task<Producto> AddAsync(Producto producto, CancellationToken ct = default);
    Task UpdateAsync(Producto producto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<bool> HasVentasAsync(int productoId, CancellationToken ct = default);
}

public interface IClienteRepository
{
    Task<Cliente?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Cliente?> GetByRNCAsync(string rnc, CancellationToken ct = default);
    Task<IEnumerable<Cliente>> GetAllAsync(CancellationToken ct = default);
    Task<Cliente> AddAsync(Cliente cliente, CancellationToken ct = default);
    Task UpdateAsync(Cliente cliente, CancellationToken ct = default);
}

public interface IEnterpriseRepository
{
    Task<Enterprise?> GetDefaultAsync(CancellationToken ct = default);
    Task<Enterprise?> GetByRNCAsync(string rnc, CancellationToken ct = default);
    Task<Enterprise> AddAsync(Enterprise enterprise, CancellationToken ct = default);
    Task UpdateAsync(Enterprise enterprise, CancellationToken ct = default);
}

public interface IAnulacionRepository
{
    Task<Anulacion?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Anulacion?> GetByTrackIdAsync(string trackId, CancellationToken ct = default);
    Task<IEnumerable<Anulacion>> GetAllAsync(CancellationToken ct = default);
    Task<Anulacion> AddAsync(Anulacion anulacion, CancellationToken ct = default);
    Task UpdateAsync(Anulacion anulacion, CancellationToken ct = default);
}
