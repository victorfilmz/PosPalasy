using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

/// <summary>
/// Contrato del registro de devoluciones de venta. La implementación es responsable de la barrera
/// de idempotencia: una misma clave no puede producir dos reembolsos.
/// </summary>
public interface IDevolucionRepository
{
    /// <summary>Devuelve la devolución ya registrada con la clave indicada (o null).</summary>
    Task<Devolucion?> GetByClaveIdempotenciaAsync(Guid clave, CancellationToken ct = default);

    /// <summary>
    /// Devolución por identificador, con sus renglones, la venta de origen y el comprobante de esa
    /// venta (todo lo necesario para construir la nota de crédito sin consultas adicionales).
    /// </summary>
    Task<Devolucion?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Devoluciones registradas sobre la venta indicada (para computar lo ya devuelto y evitar
    /// devolver más de lo vendido entre intentos concurrentes o sucesivos).
    /// </summary>
    Task<List<Devolucion>> GetByVentaIdAsync(int ventaId, CancellationToken ct = default);

    /// <summary>Registra la devolución dentro de la transacción del caso de uso.</summary>
    Task<Devolucion> AddAsync(Devolucion devolucion, CancellationToken ct = default);

    /// <summary>
    /// Marca el efecto fiscal de la devolución (comprobante emitido y vinculado). Debe llamarse
    /// dentro de la transacción del caso de uso que emite la nota de crédito.
    /// </summary>
    Task UpdateAsync(Devolucion devolucion, CancellationToken ct = default);

    /// <summary>Ancla la serialización de devoluciones sobre la fila de la venta. Debe invocarse
    /// dentro de la transacción, ANTES de leer lo ya devuelto: dos peticiones concurrentes sobre la
    /// misma venta quedan en fila en este punto y la segunda ve el tope consumido por la primera, de
    /// modo que el reembolso nunca supera lo pagado (la lectura optimista sola permite duplicarlo).
    /// </summary>
    Task AnclarVentaParaDevolucionAsync(int ventaId, CancellationToken ct = default);

    /// <summary>
    /// Serialización para la emisión de la nota de crédito: dos emisiones concurrentes de nota
    /// sobre la MISMA devolución quedan en fila aquí y la segunda ve la nota de la primera (una
    /// devolución tiene UNA nota). Debe invocarse dentro de la transacción de emisión.
    /// </summary>
    Task AnclarParaNotaCreditoAsync(int devolucionId, CancellationToken ct = default);
}
