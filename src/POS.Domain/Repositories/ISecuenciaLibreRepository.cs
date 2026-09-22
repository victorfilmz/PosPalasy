using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

/// <summary>
/// Pool de secuencias devueltas al stock de numeración tras un rechazo corregible de la DGII
/// (secuenciaUtilizada=false, sub-fase 5.5). La asignación consume el pool antes de avanzar el
/// contador de la serie, de modo que la secuencia liberada se reutiliza exactamente una vez.
/// </summary>
public interface ISecuenciaLibreRepository
{
    /// <summary>
    /// Reserva y devuelve la secuencia libre más antigua de la serie (FIFO: las secuencias se
    /// reutilizan en el orden en que fueron liberadas), o null si el pool está vacío. La fila se
    /// marca como consumida y DEBE llamarse dentro de la transacción que usa el e-NCF: un rollback
    /// de la venta devuelve la secuencia al pool junto con todo lo demás.
    /// </summary>
    Task<SecuenciaLibre?> ConsumirAsync(string serie, CancellationToken ct = default);

    /// <summary>
    /// Registra la devolución de una secuencia al pool (auditoría incluida): la secuencia del
    /// comprobante rechazado vuelve a estar disponible. Idempotente: liberar dos veces la misma
    /// secuencia no crea dos filas.
    /// </summary>
    Task<SecuenciaLibre> LiberarAsync(
        string serie,
        long numero,
        int facturaRechazadaId,
        string motivoRechazo,
        string liberadaPor,
        CancellationToken ct = default);

    /// <summary>Secuencias libres de una serie (para diagnóstico y pantallas de numeración).</summary>
    Task<List<SecuenciaLibre>> ObtenerLibresAsync(string serie, CancellationToken ct = default);
}
