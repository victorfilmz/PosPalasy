using System;
using System.Collections.Generic;

namespace POS.Domain.Types;

/// <summary>
/// Tipo de cálculo de Impuesto Selectivo al Consumo (ISC).
/// </summary>
public enum TipoCalculoISC
{
    AdValorem,  // Porcentual sobre la base imponible
    Especifico  // Monto fijo por unidad física / volumen
}

/// <summary>
/// Catálogo de los 39 Códigos de Impuestos Adicionales / ISC según la DGII.
/// </summary>
public enum CodificacionTipoImpuestos : int
{
    PropinaLegal = 1,                       // "001" - 10%
    ContribucionTelecomunicaciones = 2,    // "002" - 2% (CDT)
    ISC_ServiciosSeguros = 3,              // "003" - 16%
    ISC_ServiciosTelecomunicaciones = 4,   // "004" - 10%
    ISC_ExpedicionPrimeraPlaca = 5,        // "005"
    ISC_Cerveza_Especifico = 6,            // "006"
    ISC_VinosUva_Especifico = 7,           // "007"
    ISC_VermutVinosEspecifico = 8,         // "008"
    ISC_BebidasFermentadas_Especifico = 9, // "009"
    ISC_AlcoholAltaGraduacion_Especifico = 10,  // "010"
    ISC_AlcoholBajaGraduacion_Especifico = 11,  // "011"
    ISC_AguardientesUva_Especifico = 12,   // "012"
    ISC_Whisky_Especifico = 13,            // "013"
    ISC_RonAguardientes_Especifico = 14,   // "014"
    ISC_GinGinebra_Especifico = 15,        // "015"
    ISC_Vodka_Especifico = 16,             // "016"
    ISC_Licores_Especifico = 17,           // "017"
    ISC_DemasBebidasAlcohol_Especifico = 18, // "018"
    ISC_Cigarrillos20Especifico = 19,      // "019"
    ISC_Demascigarrillos20Especifico = 20, // "020"
    ISC_Cigarrillos10_Especifico = 21,    // "021"
    ISC_Demascigarrillos10_Especifico = 22, // "022"
    ISC_Cerveza_AdValorem = 23,            // "023"
    ISC_VinosUva_AdValorem = 24,           // "024"
    ISC_VermutVinosAdValorem = 25,         // "025"
    ISC_BebidasFermentadas_AdValorem = 26, // "026"
    ISC_AlcoholAltaGraduacion_AdValorem = 27, // "027"
    ISC_AlcoholBajaGraduacion_AdValorem = 28, // "028"
    ISC_AguardientesUva_AdValorem = 29,   // "029"
    ISC_Whisky_AdValorem = 30,             // "030"
    ISC_RonAguardientes_AdValorem = 31,   // "031"
    ISC_GinGinebra_AdValorem = 32,         // "032"
    ISC_Vodka_AdValorem = 33,              // "033"
    ISC_Licores_AdValorem = 34,            // "034"
    ISC_DemasBebidasAlcohol_AdValorem = 35, // "035"
    ISC_Cigarrillos20_AdValorem = 36,     // "036"
    ISC_Demascigarrillos20_AdValorem = 37, // "037"
    ISC_Cigarrillos10_AdValorem = 38,     // "038"
    ISC_Demascigarrillos10_AdValorem = 39  // "039"
}

public record DetalleISC(string Codigo, string Descripcion, TipoCalculoISC Tipo);

/// <summary>
/// Clase estática con metadatos y métodos auxiliares para los 39 códigos ISC de la DGII.
/// </summary>
public static class ISC
{
    private static readonly Dictionary<int, DetalleISC> Catalogo = new()
    {
        { 1, new("001", "Propina Legal", TipoCalculoISC.AdValorem) },
        { 2, new("002", "Contribución al Desarrollo de Telecomunicaciones (CDT)", TipoCalculoISC.AdValorem) },
        { 3, new("003", "ISC Servicios de Seguros", TipoCalculoISC.AdValorem) },
        { 4, new("004", "ISC Servicios de Telecomunicaciones", TipoCalculoISC.AdValorem) },
        { 5, new("005", "ISC Expedición de Primera Placa", TipoCalculoISC.AdValorem) },
        { 6, new("006", "ISC Cerveza (Específico)", TipoCalculoISC.Especifico) },
        { 7, new("007", "ISC Vinos de Uva (Específico)", TipoCalculoISC.Especifico) },
        { 8, new("008", "ISC Vermut y Vinos (Específico)", TipoCalculoISC.Especifico) },
        { 9, new("009", "ISC Bebidas Fermentadas (Específico)", TipoCalculoISC.Especifico) },
        { 10, new("010", "ISC Alcohol Alta Graduación (Específico)", TipoCalculoISC.Especifico) },
        { 11, new("011", "ISC Alcohol Baja Graduación (Específico)", TipoCalculoISC.Especifico) },
        { 12, new("012", "ISC Aguardientes de Uva (Específico)", TipoCalculoISC.Especifico) },
        { 13, new("013", "ISC Whisky (Específico)", TipoCalculoISC.Especifico) },
        { 14, new("014", "ISC Ron y Aguardientes (Específico)", TipoCalculoISC.Especifico) },
        { 15, new("015", "ISC Gin y Ginebra (Específico)", TipoCalculoISC.Especifico) },
        { 16, new("016", "ISC Vodka (Específico)", TipoCalculoISC.Especifico) },
        { 17, new("017", "ISC Licores (Específico)", TipoCalculoISC.Especifico) },
        { 18, new("018", "ISC Demás Bebidas con Alcohol (Específico)", TipoCalculoISC.Especifico) },
        { 19, new("019", "ISC Cigarrillos 20 unidades (Específico)", TipoCalculoISC.Especifico) },
        { 20, new("020", "ISC Demás Cigarrillos 20 unidades (Específico)", TipoCalculoISC.Especifico) },
        { 21, new("021", "ISC Cigarrillos 10 unidades (Específico)", TipoCalculoISC.Especifico) },
        { 22, new("022", "ISC Demás Cigarrillos 10 unidades (Específico)", TipoCalculoISC.Especifico) },
        { 23, new("023", "ISC Cerveza (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 24, new("024", "ISC Vinos de Uva (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 25, new("025", "ISC Vermut y Vinos (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 26, new("026", "ISC Bebidas Fermentadas (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 27, new("027", "ISC Alcohol Alta Graduación (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 28, new("028", "ISC Alcohol Baja Graduación (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 29, new("029", "ISC Aguardientes de Uva (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 30, new("030", "ISC Whisky (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 31, new("031", "ISC Ron y Aguardientes (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 32, new("032", "ISC Gin y Ginebra (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 33, new("033", "ISC Vodka (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 34, new("034", "ISC Licores (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 35, new("035", "ISC Demás Bebidas con Alcohol (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 36, new("036", "ISC Cigarrillos 20 unidades (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 37, new("037", "ISC Demás Cigarrillos 20 unidades (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 38, new("038", "ISC Cigarrillos 10 unidades (Ad-Valorem)", TipoCalculoISC.AdValorem) },
        { 39, new("039", "ISC Demás Cigarrillos 10 unidades (Ad-Valorem)", TipoCalculoISC.AdValorem) }
    };

    public static string ToCodigoString(CodificacionTipoImpuestos tipo) => ((int)tipo).ToString("000");

    public static CodificacionTipoImpuestos FromCodigoString(string codigo)
    {
        if (!int.TryParse(codigo, out int num) || num < 1 || num > 39)
            throw new ArgumentException($"Código de ISC / Impuesto inválido: '{codigo}'. Debe ser entre 001 y 039.", nameof(codigo));

        return (CodificacionTipoImpuestos)num;
    }

    public static DetalleISC ObtenerDetalle(CodificacionTipoImpuestos tipo) => Catalogo[(int)tipo];

    public static bool EsValido(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return false;
        return int.TryParse(codigo, out int num) && num >= 1 && num <= 39;
    }
}
