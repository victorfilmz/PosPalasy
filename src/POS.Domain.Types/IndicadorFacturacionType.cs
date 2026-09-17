using System;

namespace POS.Domain.Types;

/// <summary>
/// Indicador de facturación para items según la normativa DGII.
/// Determina el tratamiento del ITBIS para cada línea del comprobante.
/// 0 = No facturable (ITBIS 18% sobre margen)
/// 1 = Gravado ITBIS Tasa 1 (18% general)
/// 2 = Gravado ITBIS Tasa 2 (16% tasa reducida / primera necesidad)
/// 3 = Gravado ITBIS Tasa 3 (0%)
/// 4 = Exento (E)
/// </summary>
public enum IndicadorFacturacionType : int
{
    NoFacturable18 = 0,
    ITBIS1_18 = 1,
    ITBIS2_16 = 2,
    ITBIS3_0 = 3,
    Exento = 4
}

/// <summary>
/// Indicador de facturación para Descuentos o Recargos globales.
/// Solo admite tasas 1 (18%), 2 (16%) y 3 (0%).
/// </summary>
public enum IndicadorFacturacionDRType : int
{
    ITBIS1_18 = 1,
    ITBIS2_16 = 2,
    ITBIS3_0 = 3
}

/// <summary>
/// Indicador de Bien o Servicio (1 = Bien, 2 = Servicio).
/// </summary>
public enum IndicadorBienoServicioType : int
{
    Bien = 1,
    Servicio = 2
}

/// <summary>
/// Indicador de si los montos incluyen ITBIS o no.
/// 0 = No incluye ITBIS en el precio de las líneas.
/// 1 = Montos expresados con ITBIS incluido.
/// </summary>
public enum IndicadorMontoGravadoType : int
{
    NoIncluyeITBIS = 0,
    IncluyeITBIS = 1
}

/// <summary>
/// Indicador de envío diferido. Solo valor 1 (autorizado).
/// </summary>
public enum IndicadorEnvioDiferidoType : int
{
    Autorizado = 1
}

/// <summary>
/// Helper para cálculos y descripciones de ITBIS según el IndicadorFacturacion.
/// </summary>
public static class IndicadorFacturacionHelper
{
    /// <summary>
    /// Devuelve el porcentaje de ITBIS aplicable según el indicador.
    /// </summary>
    public static decimal ObtenerTasaITBIS(IndicadorFacturacionType indicador) => indicador switch
    {
        IndicadorFacturacionType.NoFacturable18 => 18.0m,
        IndicadorFacturacionType.ITBIS1_18 => 18.0m,
        IndicadorFacturacionType.ITBIS2_16 => 16.0m,
        IndicadorFacturacionType.ITBIS3_0 => 0.0m,
        IndicadorFacturacionType.Exento => 0.0m,
        _ => throw new ArgumentOutOfRangeException(nameof(indicador))
    };

    public static string ObtenerDescripcion(IndicadorFacturacionType indicador) => indicador switch
    {
        IndicadorFacturacionType.NoFacturable18 => "No facturable (18% sobre margen)",
        IndicadorFacturacionType.ITBIS1_18 => "Gravado 18% (General)",
        IndicadorFacturacionType.ITBIS2_16 => "Gravado 16% (Primera necesidad)",
        IndicadorFacturacionType.ITBIS3_0 => "Gravado 0%",
        IndicadorFacturacionType.Exento => "Exento de ITBIS",
        _ => throw new ArgumentOutOfRangeException(nameof(indicador))
    };
}
