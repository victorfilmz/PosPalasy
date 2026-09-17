using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Proveedor / Suplidor de mercancías para compras y entradas de inventario.
/// </summary>
public class Proveedor : BaseEntity
{
    public string RNC { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Direccion { get; set; }
    public bool EstaActivo { get; set; } = true;
}
