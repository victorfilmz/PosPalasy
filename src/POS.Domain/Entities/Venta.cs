using System;
using System.Collections.Generic;
using System.Linq;
using POS.Domain.Common;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Domain.Entities;

/// <summary>
/// Cabecera de venta realizada en el punto de venta (POS).
/// </summary>
public class Venta : BaseEntity
{
    public string NumeroFacturaInterna { get; set; } = string.Empty;
    public int EnterpriseId { get; set; }
    public Enterprise? Enterprise { get; set; }

    public int? SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }

    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public int? CajaTurnoId { get; set; }
    public CajaTurno? CajaTurno { get; set; }

    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public TipoPago TipoPago { get; set; } = TipoPago.Contado;
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;

    // Totales calculados a partir de los items
    public decimal Subtotal { get; set; }
    public decimal TotalDescuento { get; set; }
    public decimal TotalITBIS { get; set; }
    public decimal TotalISC { get; set; }
    public decimal Total { get; set; }

    public string? Notas { get; set; }

    // Colección de renglones / items
    public ICollection<VentaItem> Items { get; set; } = new List<VentaItem>();

    // Desglose de pagos realizados (soporte de pagos mixtos)
    public ICollection<PagoFactura> Pagos { get; set; } = new List<PagoFactura>();

    // Factura electrónica generada (si aplica)
    public ElectronicInvoice? ElectronicInvoice { get; set; }

    public void AddItem(VentaItem item)
    {
        item.NumeroLinea = Items.Count + 1;
        item.Calcular();
        Items.Add(item);
        CalcularTotales();
    }

    public void CalcularTotales()
    {
        Subtotal = Items.Sum(i => i.Subtotal);
        TotalDescuento = Items.Sum(i => i.Descuento);
        TotalITBIS = Items.Sum(i => i.MontoITBIS);
        TotalISC = Items.Sum(i => i.MontoISC);
        Total = Items.Sum(i => i.Total);
    }
}

/// <summary>
/// Renglón / Item individual de una venta en el POS.
/// </summary>
public class VentaItem : BaseEntity
{
    public int VentaId { get; set; }
    public Venta? Venta { get; set; }

    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }

    public int NumeroLinea { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; } = 1;
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }

    public IndicadorFacturacionType IndicadorFacturacion { get; set; } = IndicadorFacturacionType.ITBIS1_18;
    public decimal TasaITBIS { get; set; } = 18.0m;
    public decimal MontoITBIS { get; set; }

    public string? CodigoISC { get; set; }
    public decimal MontoISC { get; set; }

    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }

    public void Calcular()
    {
        TasaITBIS = IndicadorFacturacionHelper.ObtenerTasaITBIS(IndicadorFacturacion);

        // Subtotal = (Cantidad * PrecioUnitario) - Descuento
        var bruto = Math.Round(Cantidad * PrecioUnitario, 2, MidpointRounding.AwayFromZero);
        Subtotal = Math.Max(0, bruto - Descuento);

        // ITBIS por ítem
        MontoITBIS = Math.Round(Subtotal * (TasaITBIS / 100m), 2, MidpointRounding.AwayFromZero);

        // Total línea
        Total = Subtotal + MontoITBIS + MontoISC;
    }
}
