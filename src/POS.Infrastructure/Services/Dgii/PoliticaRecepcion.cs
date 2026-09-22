using POS.Domain.Entities;
using POS.Domain.Types;
using POS.Infrastructure.DGII;

namespace POS.Infrastructure.Services.Dgii;

/// <summary>
/// Regla de la DGII que decide por qué vía se transmite y consulta un comprobante: la recepción
/// RFCE (facturas de consumo &lt; RD$250,000) entrega el resultado fiscal definitivo en la misma
/// llamada y no emite TrackId; la recepción e-CF completa queda En proceso y se consulta por
/// TrackId. También parsea la secuencia numérica de un e-NCF (uso compartido por el liberador).
/// </summary>
internal static class PoliticaRecepcion
{
    /// <summary>Umbral oficial entre resumen (RFCE) y comprobante completo (e-CF).</summary>
    public const decimal UmbralRFCE = 250_000m;

    /// <summary>
    /// Indica si el comprobante se transmitió como resumen RFCE (consumo &lt; RD$250,000).
    /// </summary>
    public static bool EsRFCE(ElectronicInvoice invoice) =>
        invoice.TipoeCF == TipoeCFType.FacturaConsumo && invoice.MontoTotal < UmbralRFCE;

    /// <summary>Extrae el número de secuencia de un e-NCF; null si el formato no corresponde a la serie.</summary>
    public static long? ParsearSecuencia(string serie, string eNCF)
    {
        if (string.IsNullOrWhiteSpace(eNCF) || eNCF.Length <= serie.Length ||
            !eNCF.StartsWith(serie, StringComparison.OrdinalIgnoreCase))
            return null;

        return long.TryParse(eNCF[serie.Length..], out var numero) ? numero : null;
    }
}
