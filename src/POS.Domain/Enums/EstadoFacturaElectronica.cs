namespace POS.Domain.Enums;

/// <summary>
/// Estados de ciclo de vida de un comprobante fiscal electrónico (e-CF) según DGII y contingencia local.
/// </summary>
public enum EstadoFacturaElectronica : int
{
    /// <summary>
    /// Factura enviada a DGII y en procesamiento (polling requerido mediante TrackId).
    /// </summary>
    EnProceso = 0,

    /// <summary>
    /// Factura aceptada formalmente por DGII (vigente).
    /// </summary>
    Aceptado = 1,

    /// <summary>
    /// Factura rechazada por DGII (con motivo documentado).
    /// </summary>
    Rechazado = 2,

    /// <summary>
    /// Factura anulada (por el emisor o por disposición de DGII).
    /// </summary>
    Anulado = 3,

    /// <summary>
    /// Factura emitida localmente en modo contingencia (RFCE 32) pendiente de reenvío por falla/timeout en DGII.
    /// </summary>
    PendienteReenvio = 4
}
