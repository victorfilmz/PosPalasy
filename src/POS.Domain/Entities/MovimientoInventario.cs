using System;
using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Tipos de movimientos que afectan el stock físico de un producto en almacén.
/// </summary>
public enum TipoMovimientoInventario
{
    EntradaCompra = 1,      // Ingreso de stock por compra a proveedor o saldo inicial
    SalidaMerma = 2,        // Reducción por rotura, daño o pérdida
    SalidaVencimiento = 3,  // Reducción por caducidad de fecha
    AjusteManual = 4,       // Corrección por conteo físico
    VentaPOS = 5,           // Descarga automática generada por venta en terminal POS
    DevolucionVenta = 6     // Reingreso de stock por anulación de comprobante fiscal
}

/// <summary>
/// Registro inmutable del Kardex de inventario para trazabilidad de existencias.
/// </summary>
public class MovimientoInventario : BaseEntity
{
    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }
    public int SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }
    public int? ProveedorId { get; set; }
    public Proveedor? Proveedor { get; set; }
    public string? NumeroLote { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public TipoMovimientoInventario Tipo { get; set; }
    public decimal Cantidad { get; set; }
    public decimal StockAnterior { get; set; }
    public decimal StockNuevo { get; set; }
    public decimal CostoUnitario { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string? ReferenciaDocumento { get; set; } // Ej: e-NCF "E320000000002"

    /// <summary>
    /// Marca del kardex: la venta salió bajo la política ADVERTIR sin existencia suficiente.
    /// Permite filtrar y revisar estas salidas sin confundirlas con ventas normales.
    /// </summary>
    public bool RequiereRevision { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public string? Usuario { get; set; }
}
