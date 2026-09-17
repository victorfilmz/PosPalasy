namespace POS.Application.DTOs;

public class EmisorRequest
{
    public string RNC { get; set; } = string.Empty;           // 9 o 11 dígitos
    public string RazonSocial { get; set; } = string.Empty;  // Nombre legal
    public string? NombreComercial { get; set; }
    public string Direccion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? SitioWeb { get; set; }
    public string CodigoProvincia { get; set; } = "01";       // PP0000 o PP
    public string CodigoMunicipio { get; set; } = "010100";   // PPM000
    public string? Sucursal { get; set; }
}

public class CompradorRequest
{
    public string? RNC { get; set; }                         // Nullable si consumidor final sin RNC
    public string? Identificacion { get; set; }              // Cédula o pasaporte
    public string RazonSocial { get; set; } = string.Empty;  // Nombre o Razón Social
    public string? NombreComercial { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? CodigoProvincia { get; set; }
    public string? CodigoMunicipio { get; set; }
    public bool Exento { get; set; } = false;
}
