using System.Collections.Generic;
using POS.Domain.Common;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Domain.Entities;

/// <summary>
/// Empresa / Contribuyente emisor del sistema POS y facturación electrónica.
/// </summary>
public class Enterprise : BaseEntity
{
    public string RNC { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string? NombreComercial { get; set; }
    public string Direccion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? SitioWeb { get; set; }
    public string CodigoProvincia { get; set; } = "01";     // PP
    public string CodigoMunicipio { get; set; } = "010100"; // PPM000
    public TipoeCFType TipoComprobantePredeterminado { get; set; } = TipoeCFType.FacturaConsumo;
    public bool EstaActiva { get; set; } = true;

    // Personalización de Factura Física / Ticket Térmico
    public int AnchoPapelMm { get; set; } = 80; // 80 mm (estándar) o 58 mm (portátil)
    public bool MostrarLogoTicket { get; set; } = true;
    public string? MensajeEncabezadoExtra { get; set; }
    public string MensajePieTicket { get; set; } = "¡Gracias por su compra!";
    public string PoliticaGarantiaTicket { get; set; } = "No se aceptan devoluciones pasadas las 48 horas. Todo cambio requiere su comprobante fiscal.";
    public int CantidadCopiasTicket { get; set; } = 1;

    // Políticas Operativas de Venta

    /// <summary>
    /// Política operativa de existencias impuesta por el servidor en cada venta
    /// (Permitir / Advertir / Bloquear). Reemplaza al booleano PermitirVentaSinStock.
    /// </summary>
    public PoliticaStock PoliticaStock { get; set; } = PoliticaStock.Permitir;

    // Navegación
    public ICollection<Sucursal> Sucursales { get; set; } = new List<Sucursal>();
}
