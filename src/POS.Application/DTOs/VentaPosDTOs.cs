using System.Collections.Generic;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Application.DTOs;

public class PagoFacturaDto
{
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
}

public class VentaPosItemRequest
{
    public int ProductoId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; } = 1;
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public IndicadorFacturacionType IndicadorFacturacion { get; set; } = IndicadorFacturacionType.ITBIS1_18;
    public UnidadMedidaType UnidadMedida { get; set; } = UnidadMedidaType.Unidad;
}

public class VentaPosRequest
{
    public int? SucursalId { get; set; }
    public int? ClienteId { get; set; }
    public string? RNCComprador { get; set; }
    public string? RazonSocialComprador { get; set; }
    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;
    public TipoPago TipoPago { get; set; } = TipoPago.Contado;
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public decimal? MontoRecibido { get; set; }
    public List<PagoFacturaDto>? Pagos { get; set; }

    public List<VentaPosItemRequest> Items { get; set; } = new();
}

public class VentaPosResponse
{
    public bool Exitoso { get; set; }
    public int VentaId { get; set; }
    public int? ElectronicInvoiceId { get; set; }
    public string eNCF { get; set; } = string.Empty;
    public string? TrackId { get; set; }
    public decimal Total { get; set; }
    public decimal Cambio { get; set; }
    public EstadoFacturaElectronica Estado { get; set; }
    public bool EsOfflineDGII { get; set; }
    public string? Mensaje { get; set; }
}
