using System;
using POS.Domain.Entities;
using POS.Domain.Types;

namespace POS.Application.DTOs;

public class ProductoInventarioDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = "General";
    public decimal PrecioUnitario { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public int? SucursalId { get; set; }
    public string? NombreSucursal { get; set; }
    public bool TieneLotes { get; set; }
    public IndicadorFacturacionType IndicadorFacturacion { get; set; }
    public bool EstaActivo { get; set; }
    public bool TieneVentasHistoricas { get; set; }

    public decimal MargenPorcentaje => CostoUnitario > 0 
        ? Math.Round(((PrecioUnitario - CostoUnitario) / CostoUnitario) * 100, 1) 
        : 100m;

    public string EstadoStock => StockActual <= 0 
        ? "Agotado" 
        : (StockActual <= StockMinimo ? "Bajo" : "Normal");
}

public class CrearProductoDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = "General";
    public decimal PrecioUnitario { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal StockInicial { get; set; }
    public decimal StockMinimo { get; set; } = 5m;
    public int? SucursalId { get; set; }
    public bool TieneLotes { get; set; }
    public IndicadorFacturacionType IndicadorFacturacion { get; set; } = IndicadorFacturacionType.ITBIS1_18;
}

public class EditarProductoDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = "General";
    public decimal PrecioUnitario { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal StockMinimo { get; set; } = 5m;
    public int? SucursalId { get; set; }
    public bool TieneLotes { get; set; }
    public IndicadorFacturacionType IndicadorFacturacion { get; set; } = IndicadorFacturacionType.ITBIS1_18;
    public bool EstaActivo { get; set; } = true;
}

public class AjusteStockDto
{
    public int ProductoId { get; set; }
    public int? SucursalId { get; set; }
    public int? ProveedorId { get; set; }
    public string? NumeroLote { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public decimal Cantidad { get; set; }
    public TipoMovimientoInventario TipoMovimiento { get; set; } = TipoMovimientoInventario.AjusteManual;
    public string Concepto { get; set; } = string.Empty;
}

public class KardexItemDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public int ProductoId { get; set; }
    public string CodigoProducto { get; set; } = string.Empty;
    public string DescripcionProducto { get; set; } = string.Empty;
    public int? SucursalId { get; set; }
    public string? NombreSucursal { get; set; }
    public string? ProveedorNombre { get; set; }
    public string? NumeroLote { get; set; }
    public TipoMovimientoInventario TipoMovimiento { get; set; }
    public decimal Cantidad { get; set; }
    public decimal StockAnterior { get; set; }
    public decimal StockResultante { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string? ReferenciaDocumento { get; set; }
}
