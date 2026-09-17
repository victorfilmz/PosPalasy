using System;
using System.Globalization;
using System.Web;

namespace POS.Application.Services;

/// <summary>
/// Generador del enlace oficial de validación y consulta de comprobantes fiscales electrónicos (e-CF)
/// para la representación impresa según la Norma General No. 01-2020 de la DGII.
/// </summary>
public static class DgiiQrHelper
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Construye la URL oficial que debe codificarse dentro del Código QR en el ticket o factura impresa.
    /// </summary>
    public static string GenerarUrlConsultaDgii(
        string rncEmisor,
        string? rncComprador,
        string encf,
        string fechaEmision, // DD-MM-AAAA
        decimal montoTotal,
        decimal totalItbis,
        string codigoSeguridad) // 6 caracteres
    {
        var baseUrl = "https://fc.dgii.gov.do/ecf/consultatimbre";

        var totalStr = montoTotal.ToString("F2", Inv);
        var itbisStr = totalItbis.ToString("F2", Inv);
        var comprador = string.IsNullOrWhiteSpace(rncComprador) ? "" : rncComprador.Trim();

        var query = $"RncEmisor={Uri.EscapeDataString(rncEmisor.Trim())}" +
                    $"&RncComprador={Uri.EscapeDataString(comprador)}" +
                    $"&ENCF={Uri.EscapeDataString(encf.Trim())}" +
                    $"&FechaEmision={Uri.EscapeDataString(fechaEmision.Trim())}" +
                    $"&MontoTotal={Uri.EscapeDataString(totalStr)}" +
                    $"&TotalITBIS={Uri.EscapeDataString(itbisStr)}" +
                    $"&CodigoSeguridad={Uri.EscapeDataString(codigoSeguridad.Trim())}";

        return $"{baseUrl}?{query}";
    }
}
