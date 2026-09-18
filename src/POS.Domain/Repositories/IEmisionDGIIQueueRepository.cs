using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

/// <summary>
/// Contrato de la cola de emisión (outbox) hacia la DGII.
/// </summary>
/// <remarks>
/// El reclamo de un elemento es un lease atómico: un documento solo puede estar en proceso por un
/// trabajador a la vez, y si éste muere el lease vence y otro lo retoma. Sin esa garantía, dos
/// trabajadores (o un reinicio) reenviarían el mismo comprobante.
/// </remarks>
public interface IEmisionDGIIQueueRepository
{
    Task<EmisionDGIIQueue> AddAsync(EmisionDGIIQueue item, CancellationToken ct = default);

    /// <summary>Elemento de cola asociado a un comprobante que todavía no fue confirmado.</summary>
    Task<EmisionDGIIQueue?> GetPendientePorFacturaAsync(int facturaId, CancellationToken ct = default);

    /// <summary>Elementos elegibles para intento: sin enviar, no definitivos, fuera de lease y de ventana de espera.</summary>
    Task<IReadOnlyList<EmisionDGIIQueue>> ObtenerElegiblesAsync(
        int maxItems,
        DateTime ahoraUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Reclama un elemento con lease exclusivo e incrementa su contador de intentos.
    /// Devuelve false si otro trabajador lo reclamó primero.
    /// </summary>
    Task<bool> ReclamarAsync(int id, string leaseToken, DateTime ahoraUtc, CancellationToken ct = default);

    /// <summary>Marca el elemento como enviado (estado terminal de éxito).</summary>
    Task MarcarEnviadoAsync(int id, string? trackId, int? codigoHttp, CancellationToken ct = default);

    /// <summary>
    /// Registra un fallo. Si el error es recuperable y quedan intentos, el elemento vuelve a
    /// Pendiente con espera progresiva; si agotó los intentos queda Fallido; y si el error es
    /// permanente queda Definitivo (sin reintento automático).
    /// </summary>
    Task MarcarFalloAsync(
        int id,
        string error,
        int? codigoHttp,
        bool esRecuperable,
        DateTime? proximoIntentoUtc,
        CancellationToken ct = default);

    /// <summary>Cantidad de elementos pendientes de transmisión confirmada (observabilidad).</summary>
    Task<int> ContarPendientesAsync(CancellationToken ct = default);
}
