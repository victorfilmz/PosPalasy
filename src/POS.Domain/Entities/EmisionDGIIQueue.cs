using System;
using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Cola de emisión asíncrona y contingencia offline ante la DGII (patrón outbox persistente).
/// La fila se crea dentro de la misma transacción que la venta: si el proceso muere antes de
/// transmitir, el documento no se pierde.
/// </summary>
public class EmisionDGIIQueue : BaseEntity
{
    public int FacturaId { get; set; }
    public string eNCF { get; set; } = string.Empty;
    public string XmlFirmado { get; set; } = string.Empty;
    public int Intentos { get; set; } = 0;
    public string? UltimoError { get; set; }
    public bool EnviadoExitosamente { get; set; } = false;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    public DateTime? FechaUltimoIntento { get; set; }

    /// <summary>Estado del elemento dentro de la cola (pendiente, en proceso, enviado, fallido, definitivo).</summary>
    public Enums.EstadoColaDGII Estado { get; set; } = Enums.EstadoColaDGII.Pendiente;

    /// <summary>
    /// Momento a partir del cual el elemento es elegible para reintento (espera progresiva con jitter).
    /// Evita el reintento en bucle cada 30 segundos sin importar el tipo de error.
    /// </summary>
    public DateTime? ProximoIntentoUtc { get; set; }

    /// <summary>Identificador del trabajador que reclamó el elemento (lease).</summary>
    public string? LeaseToken { get; set; }

    /// <summary>Vencimiento del lease: si el trabajador muere, otro lo retoma al expirar.</summary>
    public DateTime? LeaseHastaUtc { get; set; }

    /// <summary>TrackId devuelto por la DGII una vez confirmada la recepción.</summary>
    public string? TrackId { get; set; }

    /// <summary>Último código HTTP recibido (diagnóstico de la causa del fallo).</summary>
    public int? UltimoCodigoHttp { get; set; }

    /// <summary>Indica si el elemento está disponible para ser reclamado por un trabajador.</summary>
    public bool EsElegible(DateTime ahoraUtc) =>
        !EnviadoExitosamente
        && Estado != Enums.EstadoColaDGII.Enviado
        && Estado != Enums.EstadoColaDGII.Definitivo
        && (ProximoIntentoUtc == null || ProximoIntentoUtc <= ahoraUtc)
        && (LeaseHastaUtc == null || LeaseHastaUtc <= ahoraUtc);
}
