using POS.Domain.Types;

namespace POS.Application.DTOs;

public class InvoiceItemRequest
{
    public int Indice { get; set; }
    public string Codigo { get; set; } = string.Empty;              // Código SKU o barra
    public string Descripcion { get; set; } = string.Empty;         // Descripción del ítem
    public decimal Cantidad { get; set; } = 1;
    public UnidadMedidaType UnidadMedida { get; set; } = UnidadMedidaType.Unidad;
    public decimal PrecioUnitario { get; set; }
    
    // Tratamiento de ITBIS
    public IndicadorFacturacionType IndicadorFacturacion { get; set; } = IndicadorFacturacionType.ITBIS1_18;
    public decimal TasaITBIS { get; set; } = 18.0m;
    public decimal ITBIS { get; set; }

    // ISC si aplica
    public string? ISC { get; set; } // Código "001" a "039"
    public decimal? ISCValue { get; set; }

    // Descuentos y Recargos
    public decimal Descuento { get; set; }
    public decimal Recargo { get; set; }

    public decimal Subtotal { get; set; } // (Cantidad * PrecioUnitario) - Descuento + Recargo
    public decimal Total { get; set; }    // Subtotal + ITBIS + ISCValue
}

public class TotalesRequest
{
    public decimal SubTotal { get; set; }
    public decimal TotalDescuentos { get; set; }
    public decimal TotalRecargos { get; set; }

    // Bases imponibles
    public decimal MontoGravadoTotal { get; set; }
    public decimal MontoGravadoI1 { get; set; } // Base 18%
    public decimal MontoGravadoI2 { get; set; } // Base 16%
    public decimal MontoGravadoI3 { get; set; } // Base 0%
    public decimal MontoExento { get; set; }

    // ITBIS
    public decimal TotalITBIS { get; set; }
    public decimal TotalITBIS1 { get; set; } // Monto 18%
    public decimal TotalITBIS2 { get; set; } // Monto 16%
    public decimal TotalITBIS3 { get; set; } // Monto 0%

    // ISC
    public decimal TotalISC { get; set; }

    // Monto Total Factura
    public decimal Total { get; set; }
}
