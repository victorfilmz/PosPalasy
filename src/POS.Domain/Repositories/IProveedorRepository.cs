using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

public interface IProveedorRepository
{
    Task<Proveedor?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Proveedor?> GetByRNCAsync(string rnc, CancellationToken ct = default);
    Task<IEnumerable<Proveedor>> GetAllActiveAsync(CancellationToken ct = default);
    Task<IEnumerable<Proveedor>> GetAllAsync(CancellationToken ct = default);
    Task<Proveedor> AddAsync(Proveedor proveedor, CancellationToken ct = default);
    Task UpdateAsync(Proveedor proveedor, CancellationToken ct = default);
}
