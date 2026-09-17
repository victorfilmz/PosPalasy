using System;

namespace POS.Domain.Types;

/// <summary>
/// Monedas soportadas según tabla de divisas de DGII / ISO 4217.
/// DOP es la moneda oficial por defecto. Las demás se usan en OtraMoneda.
/// </summary>
public enum TipoMonedaType : int
{
    DOP = 1,
    USD = 2,
    EUR = 3,
    BRL = 4,
    CAD = 5,
    CHF = 6,
    CNY = 7,
    XDR = 8,
    DKK = 9,
    GBP = 10,
    JPY = 11,
    NOK = 12,
    SCP = 13,
    SEK = 14,
    VEF = 15,
    HTG = 16,
    MXN = 17,
    COP = 18
}

/// <summary>
/// Alias para mantener compatibilidad con referencias a TipoMoneda.
/// </summary>
public enum TipoMoneda : int
{
    DOP = 1,
    USD = 2,
    EUR = 3,
    BRL = 4,
    CAD = 5,
    CHF = 6,
    CNY = 7,
    XDR = 8,
    DKK = 9,
    GBP = 10,
    JPY = 11,
    NOK = 12,
    SCP = 13,
    SEK = 14,
    VEF = 15,
    HTG = 16,
    MXN = 17,
    COP = 18
}

public static class TipoMonedaFormatter
{
    public static string ToString(TipoMonedaType moneda) => moneda switch
    {
        TipoMonedaType.DOP => "DOP",
        TipoMonedaType.USD => "USD",
        TipoMonedaType.EUR => "EUR",
        TipoMonedaType.BRL => "BRL",
        TipoMonedaType.CAD => "CAD",
        TipoMonedaType.CHF => "CHF",
        TipoMonedaType.CNY => "CHY", // DGII XSD especifica "CHY" para Yuan
        TipoMonedaType.XDR => "XDR",
        TipoMonedaType.DKK => "DKK",
        TipoMonedaType.GBP => "GBP",
        TipoMonedaType.JPY => "JPY",
        TipoMonedaType.NOK => "NOK",
        TipoMonedaType.SCP => "SCP",
        TipoMonedaType.SEK => "SEK",
        TipoMonedaType.VEF => "VEF",
        TipoMonedaType.HTG => "HTG",
        TipoMonedaType.MXN => "MXN",
        TipoMonedaType.COP => "COP",
        _ => throw new ArgumentOutOfRangeException(nameof(moneda), $"Moneda no soportada: {moneda}")
    };

    public static string ToString(TipoMoneda moneda) => ToString((TipoMonedaType)moneda);

    public static TipoMonedaType FromString(string s) => s?.Trim().ToUpperInvariant() switch
    {
        "DOP" => TipoMonedaType.DOP,
        "USD" => TipoMonedaType.USD,
        "EUR" => TipoMonedaType.EUR,
        "BRL" => TipoMonedaType.BRL,
        "CAD" => TipoMonedaType.CAD,
        "CHF" => TipoMonedaType.CHF,
        "CNY" or "CHY" => TipoMonedaType.CNY,
        "XDR" => TipoMonedaType.XDR,
        "DKK" => TipoMonedaType.DKK,
        "GBP" => TipoMonedaType.GBP,
        "JPY" => TipoMonedaType.JPY,
        "NOK" => TipoMonedaType.NOK,
        "SCP" => TipoMonedaType.SCP,
        "SEK" => TipoMonedaType.SEK,
        "VEF" => TipoMonedaType.VEF,
        "HTG" => TipoMonedaType.HTG,
        "MXN" => TipoMonedaType.MXN,
        "COP" => TipoMonedaType.COP,
        _ => throw new ArgumentException($"Código de moneda inválido según DGII: '{s}'", nameof(s))
    };
}
