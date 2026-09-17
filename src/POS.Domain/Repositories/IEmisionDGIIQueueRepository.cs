using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

public interface IEmisionDGIIQueueRepository
{
    Task<EmisionDGIIQueue> AddAsync(EmisionDGIIQueue item, CancellationToken ct = default);
    Task<IEnumerable<EmisionDGIIQueue>> GetPendientesAsync(int maxItems = 20, CancellationToken ct = default);
    Task MarcarEnviadoAsync(int id, string? trackId, CancellationToken ct = default);
    Task RegistrarFalloAsync(int id, string error, CancellationToken ct = default);
}
