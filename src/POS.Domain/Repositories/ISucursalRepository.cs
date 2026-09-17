using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

public interface ISucursalRepository
{
    Task<Sucursal?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Sucursal?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
    Task<Sucursal?> GetPrincipalAsync(CancellationToken ct = default);
    Task<IEnumerable<Sucursal>> GetAllActiveAsync(CancellationToken ct = default);
    Task<IEnumerable<Sucursal>> GetAllAsync(CancellationToken ct = default);
    Task<Sucursal> AddAsync(Sucursal sucursal, CancellationToken ct = default);
    Task UpdateAsync(Sucursal sucursal, CancellationToken ct = default);
}
