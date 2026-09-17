namespace POS.Domain.Types;

/// <summary>
/// Tipo de ingresos según clasificación DGII.
/// 01=Ingresos por operaciones (No financieros)
/// 02=Ingresos Financieros, 03=Ingresos Extraordinarios
/// 04=Ingresos por Arrendamientos, 05=Ingresos por Venta de Activo Depreciable
/// 06=Otros Ingresos
/// </summary>
public enum TipoIngresosType : int
{
    /// <summary>Ingresos por operaciones (No financieros)</summary>
    IngresosOperaciones = 1,

    /// <summary>Ingresos Financieros</summary>
    IngresosFinancieros = 2,

    /// <summary>Ingresos Extraordinarios</summary>
    IngresosExtraordinarios = 3,

    /// <summary>Ingresos por Arrendamientos</summary>
    IngresosArrendamientos = 4,

    /// <summary>Ingresos por Venta de Activo Depreciable</summary>
    IngresosVentaActivoDepreciable = 5,

    /// <summary>Otros Ingresos</summary>
    OtrosIngresos = 6
}

/// <summary>
/// Formateador para TipoIngresosType → string de 2 dígitos.
/// </summary>
public static class TipoIngresosFormatter
{
    public static string ToString(TipoIngresosType tipo) => tipo switch
    {
        TipoIngresosType.IngresosOperaciones => "01",
        TipoIngresosType.IngresosFinancieros => "02",
        TipoIngresosType.IngresosExtraordinarios => "03",
        TipoIngresosType.IngresosArrendamientos => "04",
        TipoIngresosType.IngresosVentaActivoDepreciable => "05",
        TipoIngresosType.OtrosIngresos => "06",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo))
    };

    public static TipoIngresosType FromString(string s)
    {
        return s switch
        {
            "01" => TipoIngresosType.IngresosOperaciones,
            "02" => TipoIngresosType.IngresosFinancieros,
            "03" => TipoIngresosType.IngresosExtraordinarios,
            "04" => TipoIngresosType.IngresosArrendamientos,
            "05" => TipoIngresosType.IngresosVentaActivoDepreciable,
            "06" => TipoIngresosType.OtrosIngresos,
            _ => throw new ArgumentException($"Tipo de ingreso inválido: {s}")
        };
    }
}
