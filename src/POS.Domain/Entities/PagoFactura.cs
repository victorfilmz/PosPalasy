using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Desglose de pagos mixtos o individuales aplicados a una venta / factura.
/// </summary>
public class PagoFactura : BaseEntity
{
    public int VentaId { get; set; }
    public Venta? Venta { get; set; }

    public int? FacturaId { get; set; } // ElectronicInvoice Id si fue emitida
    public string MetodoPago { get; set; } = string.Empty; // "Efectivo", "Tarjeta", "Transferencia"
    public decimal Monto { get; set; }
    public string? Referencia { get; set; } // No. de autorización voucher, cheque o transferencia
}
