using System;

namespace POS.Domain.Common;

/// <summary>
/// Desglose del cobro de una venta: cuánto entró por cada forma de pago y cuánto debe devolverse
/// en efectivo. Es el resultado de validar los pagos contra el total en el dominio, no en la UI.
/// </summary>
public sealed record DesglosePago(decimal Efectivo, decimal Tarjeta, decimal Transferencia, decimal Otros, decimal Cambio)
{
    /// <summary>Total efectivamente cobrado (incluye el cambio si el cliente entregó de más).</summary>
    public decimal TotalRecibido => Efectivo + Tarjeta + Transferencia + Otros;

    /// <summary>Efectivo neto que debe quedar en la caja del turno (descontado el cambio entregado).</summary>
    public decimal EfectivoNetoEnCaja => Efectivo - Cambio;
}
