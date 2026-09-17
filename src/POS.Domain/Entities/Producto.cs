using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using POS.Domain.Common;
using POS.Domain.Types;

namespace POS.Domain.Entities;

/// <summary>
/// Producto o servicio comercializado en el POS, con atributos fiscales DGII y existencias multi-sucursal.
/// </summary>
public class Producto : BaseEntity
{
    public string Codigo { get; set; } = string.Empty; // SKU o código de barra
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = "General"; // Categoría para filtros POS y reportes
    public decimal PrecioUnitario { get; set; }
    public decimal CostoUnitario { get; set; } = 0.00m; // Costo de compra para calcular margen
    public bool TieneLotes { get; set; } = false; // Requiere rastreo por lote y fecha caducidad
    public IndicadorFacturacionType IndicadorFacturacion { get; set; } = IndicadorFacturacionType.ITBIS1_18;
    public IndicadorBienoServicioType IndicadorBienoServicio { get; set; } = IndicadorBienoServicioType.Bien;
    public UnidadMedidaType UnidadMedida { get; set; } = UnidadMedidaType.Unidad;
    public string? CodigoISC { get; set; } // Código "001" a "039" si aplica
    public decimal? TasaISC { get; set; }
    public bool EstaActivo { get; set; } = true;

    // Colección de existencias por sucursal / almacén
    public ICollection<InventarioAlmacen> Inventarios { get; set; } = new List<InventarioAlmacen>();

    public decimal StockConsolidado => Inventarios?.Sum(i => i.StockActual) ?? 0m;

    // Atributos de stock contextual para la sucursal activa en sesión (no mapeados en tabla Productos)
    [NotMapped]
    public decimal StockActual { get; set; }

    [NotMapped]
    public decimal StockMinimo { get; set; } = 5m;
}
