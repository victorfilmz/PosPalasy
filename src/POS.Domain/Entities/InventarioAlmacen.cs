using POS.Domain.Common;

namespace POS.Domain.Entities;

public class InventarioAlmacen : BaseEntity
{
    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }
    public int SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }
    public decimal StockActual { get; set; } = 0.00m;
    public decimal StockMinimo { get; set; } = 5.00m;
}
