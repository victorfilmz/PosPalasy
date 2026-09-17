using System.Collections.Generic;
using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Sucursal, tienda o almacén físico perteneciente a una empresa emisora.
/// </summary>
public class Sucursal : BaseEntity
{
    public int EnterpriseId { get; set; }
    public Enterprise? Enterprise { get; set; }

    public string CodigoSucursal { get; set; } = "001";
    public string Nombre { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string CodigoProvincia { get; set; } = "01";
    public string CodigoMunicipio { get; set; } = "010100";
    public bool EstaActiva { get; set; } = true;

    // Colecciones de navegación
    public ICollection<InventarioAlmacen> Inventarios { get; set; } = new List<InventarioAlmacen>();
    public ICollection<CajaTurno> Turnos { get; set; } = new List<CajaTurno>();
    public ICollection<Venta> Ventas { get; set; } = new List<Venta>();
}
