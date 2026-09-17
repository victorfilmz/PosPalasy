using POS.Domain.Types;

namespace POS.Application.DTOs;

public class ConfiguracionEmpresaDto
{
    public string RNC { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string? NombreComercial { get; set; }
    public string Direccion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? SitioWeb { get; set; }
    public string CodigoProvincia { get; set; } = "01";
    public string CodigoMunicipio { get; set; } = "010100";
    public TipoeCFType TipoComprobantePredeterminado { get; set; } = TipoeCFType.FacturaConsumo;
    public bool PermitirVentaSinStock { get; set; } = true;
}

public class ConfiguracionFacturaFisicaDto
{
    public int AnchoPapelMm { get; set; } = 80;
    public bool MostrarLogoTicket { get; set; } = true;
    public string? MensajeEncabezadoExtra { get; set; }
    public string MensajePieTicket { get; set; } = "¡Gracias por su compra!";
    public string PoliticaGarantiaTicket { get; set; } = "No se aceptan devoluciones pasadas las 48 horas. Todo cambio requiere su comprobante fiscal.";
    public int CantidadCopiasTicket { get; set; } = 1;
}
