namespace POS.Domain.Enums;

/// <summary>
/// Política operativa de existencias de la empresa emisora, impuesta por el servidor en cada venta.
/// </summary>
/// <remarks>
/// <para>
/// Precedencia deliberada: UN solo nivel (empresa). No se modela política por sucursal ni por
/// producto porque no hay requisito operativo que lo exija y agregaría superficie de decisión sin
/// necesidad; si el negocio lo pide, la política de empresa es el punto de extensión natural.
/// </para>
/// <para>
/// El valor no es solo visual: <see cref="POS.Domain.Entities.InventarioAlmacen"/> se descuenta y se
/// valida en el caso de uso de venta dentro de la transacción, y cualquier cambio de la política
/// queda auditado (usuario, fecha, valor anterior, valor nuevo, motivo).
/// </para>
/// </remarks>
public enum PoliticaStock : int
{
    /// <summary>
    /// Se puede vender aunque la existencia no alcance y la fila de existencia no esté registrada.
    /// El stock queda negativo de forma explícita y el kardex lo documenta. Práctica habitual de
    /// minoristas con reposición inmediata (venta por encargo).
    /// </summary>
    Permitir = 0,

    /// <summary>
    /// Se vende aunque no alcance, pero cada venta sobre-existente queda marcada en el kardex y en la
    /// venta (campo de observación) para revisión posterior; sigue siendo decisión del cajero.
    /// </summary>
    Advertir = 1,

    /// <summary>
    /// No se vende por encima de la existencia registrada: la venta se rechaza con
    /// STOCK_INSUFICIENTE antes de modificar inventario, caja, venta, eNCF u outbox.
    /// </summary>
    Bloquear = 2
}
