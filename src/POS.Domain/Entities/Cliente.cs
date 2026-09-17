using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Cliente / Comprador del sistema POS.
/// Puede ser persona moral (RNC 11 dígitos) o persona física (RNC/Cédula 9 o 11 dígitos) o consumidor final sin RNC.
/// </summary>
public class Cliente : BaseEntity
{
    public string? RNC { get; set; }
    public string? Identificacion { get; set; } // Cédula o pasaporte si no posee RNC
    public string RazonSocial { get; set; } = string.Empty;
    public string? NombreComercial { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? CodigoProvincia { get; set; }
    public string? CodigoMunicipio { get; set; }
    public bool EsExento { get; set; } = false;
    public bool EstaActivo { get; set; } = true;
}
