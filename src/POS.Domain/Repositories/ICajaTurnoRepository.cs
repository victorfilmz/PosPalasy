using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

public interface ICajaTurnoRepository
{
    Task<CajaTurno?> GetTurnoActivoAsync(string? cajero = null, CancellationToken ct = default);
    Task<CajaTurno?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IEnumerable<CajaTurno>> GetAllAsync(CancellationToken ct = default);
    Task<CajaTurno> AddAsync(CajaTurno turno, CancellationToken ct = default);
    Task UpdateAsync(CajaTurno turno, CancellationToken ct = default);
    Task AddMovimientoAsync(MovimientoCaja movimiento, CancellationToken ct = default);
}
