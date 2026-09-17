# Mapeo XSD → C# — Facturación Electrónica DGII Dominicana

**Versión:** 1.0  
**Fecha:** 16/09/2026  
**Framework:** .NET 10

---

## 1. Tipos Simples (SimpleTypes) del XSD

### 1.1 VersionType

```csharp
/// <summary>
/// Versión del formato e-CF. Siempre 1.0.
/// </summary>
public record VersionType
{
    public decimal Value { get; init; } = 1.0m;

    public static implicit operator decimal(VersionType v) => v.Value;
    public static explicit operator VersionType(decimal d) => new() { Value = d };
}
```

### 1.2 TipoeCFType

```csharp
/// <summary>
/// Tipo de comprobante fiscal electrónico.
/// CFType: 31=FacturaDeCreditoFiscal, 32=FacturaDeConsumo,
/// 33=NotaDeDebito, 34=NotaDeCredito, 41=Compras,
/// 43=GastosMenores, 44=RegimenesEspeciales, 45=Gubernamental,
/// 46=Exportaciones, 47=PagosAlExterior
/// </summary>
public enum TipoeCFType : int
{
    FacturaCreditoFiscal = 31,
    FacturaConsumo = 32,
    NotaDeDebito = 33,
    NotaDeCredito = 34,
    Compras = 41,
    GastosMenores = 43,
    RegimenesEspeciales = 44,
    Gubernamental = 45,
    Exportaciones = 46,
    PagosAlExterior = 47
}
```

### 1.3 eNCFType / eNCFValidationType

```csharp
/// <summary>
/// Número de Comprobante Fiscal Electrónico (eNCF).
/// 13 caracteres alfanuméricos: [a-z0-9A-Z]{13}
/// Ejemplo: B0000000000001
/// </summary>
public record eNCFType
{
    public string Value { get; init; }

    public eNCFType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("eNCF no puede estar vacío", nameof(value));

        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-zA-Z0-9]{13}$"))
            throw new ArgumentException(
                $"eNCF inválido: '{value}'. Debe tener exactamente 13 caracteres alfanuméricos.",
                nameof(value));

        Value = value.ToUpperInvariant();
    }

    public override string ToString() => Value;
    public static implicit operator string(eNCFType enf) => enf.Value;
    public static explicit operator eNCFType(string s) => new(s);
}
```

### 1.4 TipoIngresosType

```csharp
/// <summary>
/// Tipo de ingresos según clasificación DGII.
/// 01=Ingresos por operaciones (No financieros)
/// 02=Ingresos Financieros
/// 03=Ingresos Extraordinarios
/// 04=Ingresos por Arrendamientos
/// 05=Ingresos por Venta de Activo Depreciable
/// 06=Otros Ingresos
/// </summary>
public enum TipoIngresosType : int
{
    IngresosOperaciones = 1,       // "01"
    IngresosFinancieros = 2,       // "02"
    IngresosExtraordinarios = 3,   // "03"
    IngresosArrendamientos = 4,    // "04"
    IngresosVentaActivoDepreciable = 5, // "05"
    OtrosIngresos = 6              // "06"
}

/// <summary>
/// Representación como string de 2 dígitos para XML.
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

    public static TipoIngresosType FromString(string s) => s switch
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
```

### 1.5 TipoPagoType

```csharp
/// <summary>
/// Tipo de pago: 1=Contado, 2=Crédito, 3=Gratuito
/// </summary>
public enum TipoPagoType : int
{
    Contado = 1,
    Credito = 2,
    Gratuito = 3
}
```

### 1.6 FormaPagoType

```csharp
/// <summary>
/// Forma de pago individual. 1=Efectivo, 2=Cheque/Transferencia/Depósito,
/// 3=TarjetaDébito/Crédito, 4=Venta a Crédito, 5=Bonos/Certificados,
/// 6=Permuta, 7=Nota de crédito, 8=Otras formas de pago
/// </summary>
public enum FormaPagoType : int
{
    Efectivo = 1,
    ChequeTransferenciaDeposito = 2,
    TarjetaDebitoCredito = 3,
    VentaACredito = 4,
    BonosCertificantes = 5,
    Permuta = 6,
    NotaDeCredito = 7,
    OtrasFormas = 8
}
```

### 1.7 RNCType / RNCValidationType

```csharp
/// <summary>
/// Registro Nacional de Contribuyentes (RNC).
/// Formato: [0-9]{11} (empresa) o [0-9]{9} (persona física)
/// </summary>
public record RNC
{
    public string Value { get; init; }

    public RNC(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("RNC no puede estar vacío", nameof(value));

        var normalized = value.Trim();

        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^\d{9}$") &&
            !System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^\d{11}$"))
        {
            throw new ArgumentException(
                $"RNC inválido: '{normalized}'. Debe tener 9 o 11 dígitos.",
                nameof(value));
        }

        Value = normalized;
    }

    public bool EsEmpresa => Value.Length == 11;
    public bool EsPersonaFisica => Value.Length == 9;

    public override string ToString() => Value;
    public static implicit operator string(RNC rnc) => rnc.Value;
    public static explicit operator RNC(string s) => new(s);
}
```

### 1.8 Decimal18D1or2ValidationTypeMayorIgualCero

```csharp
/// <summary>
/// Monto decimal con 18 dígitos totales y 2 decimales, >= 0.
/// [0-9]{1,16}(?:.[0-9]{2})?
/// </summary>
public record Decimal18D2 : IComparable<Decimal18D2>
{
    public decimal Value { get; init; }

    public Decimal18D2(decimal value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "El monto debe ser >= 0");

        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public override string ToString() => Value.ToString("F2");
    public static implicit operator decimal(Decimal18D2 d) => d.Value;
    public static explicit operator Decimal18D2(decimal d) => new(d);

    public int CompareTo(Decimal18D2? other) => Value.CompareTo(other?.Value);
}
```

### 1.9 Decimal18D2MayorIgual0Type / Decimal18D1or2ValidationTypeMayorIgualCero

```csharp
/// <summary>
/// Monto decimal >= 0, 18 dígitos, 2 decimales.
/// </summary>
public record DecimalMayorIgualCero
{
    public decimal Value { get; init; }

    public DecimalMayorIgualCero(decimal value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "El monto debe ser >= 0");

        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public override string ToString() => Value.ToString("F2");
    public static implicit operator decimal(DecimalMayorIgualCero d) => d.Value;
    public static explicit operator DecimalMayorIgualCero(decimal d) => new(d);
}
```

### 1.10 FechaType / FechaValidationType

```csharp
/// <summary>
/// Fecha en formato DD-MM-AAAA.
/// Patrón: (3[01]|[12][$0-9]|0?[1-9])-(1[012]|0?[1-9])-((?:19|20)\d{2})
/// </summary>
public record FechaDominicana
{
    public DateOnly Value { get; init; }

    public FechaDominicana(DateOnly value) => Value = value;
    public FechaDominicana(DateTime date) => Value = DateOnly.FromDateTime(date);

    /// <summary>
    /// Parsea desde string DD-MM-AAAA.
    /// </summary>
    public static FechaDominicana Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Fecha no puede estar vacía", nameof(s));

        // Intentar formato DD-MM-AAAA
        var parts = s.Split('-');
        if (parts.Length != 3)
            throw new FormatException($"Formato de fecha inválido: '{s}'. Esperado: DD-MM-AAAA");

        if (!int.TryParse(parts[0], out int day) ||
            !int.TryParse(parts[1], out int month) ||
            !int.TryParse(parts[2], out int year))
        {
            throw new FormatException($"Fecha inválida: '{s}'");
        }

        var dateOnly = new DateOnly(year, month, day);
        return new FechaDominicana(dateOnly);
    }

    /// <summary>
    /// Formatea como DD-MM-AAAA para XML.
    /// </summary>
    public string ToXmlString() => Value.ToString("dd-MM-yyyy");

    public override string ToString() => ToXmlString();
    public static implicit operator DateOnly(FechaDominicana f) => f.Value;
    public static explicit operator FechaDominicana(DateOnly d) => new(d);
}
```

### 1.11 Interger6Type

```csharp
/// <summary>
/// Número entero de máximo 6 dígitos.
/// </summary>
public record Interger6Type
{
    public int Value { get; init; }

    public Interger6Type(int value)
    {
        if (value < 0 || value > 999999)
            throw new ArgumentOutOfRangeException(nameof(value), "El valor debe ser de 1 a 6 dígitos");

        Value = value;
    }

    public override string ToString() => Value.ToString();
    public static implicit operator int(Interger6Type i) => i.Value;
    public static explicit operator Interger6Type(int i) => new(i);
}
```

### 1.12 AlfaNum150RType / AlfaNum150Type

```csharp
/// <summary>
/// Cadena alfanumérica de máximo 150 caracteres.
/// </summary>
public record AlfaNum150
{
    public string Value { get; init; }

    public AlfaNum150(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El valor no puede estar vacío", nameof(value));

        if (value.Length > 150)
            throw new ArgumentException("El valor debe tener máximo 150 caracteres", nameof(value));

        Value = value.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(AlfaNum150 s) => s.Value;
    public static explicit operator AlfaNum150(string s) => new(s);
}
```

### 1.13 AlfaNum20Type

```csharp
/// <summary>
/// Cadena alfanumérica de máximo 20 caracteres.
/// </summary>
public record AlfaNum20
{
    public string Value { get; init; }

    public AlfaNum20(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El valor no puede estar vacío", nameof(value));

        if (value.Length > 20)
            throw new ArgumentException("El valor debe tener máximo 20 caracteres", nameof(value));

        Value = value.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(AlfaNum20 s) => s.Value;
    public static explicit operator AlfaNum20(string s) => new(s);
}
```

### 1.14 AlfaNum250Validation

```csharp
/// <summary>
/// Cadena alfanumérica de máximo 250 caracteres.
/// </summary>
public record AlfaNum250
{
    public string Value { get; init; }

    public AlfaNum250(string value)
    {
        if (value != null && value.Length > 250)
            throw new ArgumentException("El valor debe tener máximo 250 caracteres", nameof(value));

        Value = value?.Trim();
    }

    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum250 s) => s.Value;
    public static explicit operator AlfaNum250(string s) => new(s);
}
```

### 1.15 AlfaNum80Type / AlfaNum50Type / AlfaNum25Type / AlfaNum100Type / AlfaNum1000Type

```csharp
public record AlfaNum80
{
    public string Value { get; init; }
    public AlfaNum80(string value)
    {
        if (value != null && value.Length > 80)
            throw new ArgumentException("Máximo 80 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum80 s) => s.Value;
    public static explicit operator AlfaNum80(string s) => new(s);
}

public record AlfaNum50
{
    public string Value { get; init; }
    public AlfaNum50(string value)
    {
        if (value != null && value.Length > 50)
            throw new ArgumentException("Máximo 50 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum50 s) => s.Value;
    public static explicit operator AlfaNum50(string s) => new(s);
}

public record AlfaNum25
{
    public string Value { get; init; }
    public AlfaNum25(string value)
    {
        if (value != null && value.Length > 25)
            throw new ArgumentException("Máximo 25 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum25 s) => s.Value;
    public static explicit operator AlfaNum25(string s) => new(s);
}

public record AlfaNum100
{
    public string Value { get; init; }
    public AlfaNum100(string value)
    {
        if (value != null && value.Length > 100)
            throw new ArgumentException("Máximo 100 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum100 s) => s.Value;
    public static explicit operator AlfaNum100(string s) => new(s);
}

public record AlfaNum1000
{
    public string Value { get; init; }
    public AlfaNum1000(string value)
    {
        if (value != null && value.Length > 1000)
            throw new ArgumentException("Máximo 1000 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum1000 s) => s.Value;
    public static explicit operator AlfaNum1000(string s) => new(s);
}
```

### 1.16 AlfaNum60Type / AlfaNum7Type / AlfaNum10Type / AlfaNum13Validation / AlfaNum11Validation

```csharp
public record AlfaNum60
{
    public string Value { get; init; }
    public AlfaNum60(string value)
    {
        if (value != null && value.Length > 60)
            throw new ArgumentException("Máximo 60 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum60 s) => s.Value;
    public static explicit operator AlfaNum60(string s) => new(s);
}

public record AlfaNum7
{
    public string Value { get; init; }
    public AlfaNum7(string value)
    {
        if (value != null && value.Length > 7)
            throw new ArgumentException("Máximo 7 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum7 s) => s.Value;
    public static explicit operator AlfaNum7(string s) => new(s);
}

public record AlfaNum10
{
    public string Value { get; init; }
    public AlfaNum10(string value)
    {
        if (value != null && value.Length > 10)
            throw new ArgumentException("Máximo 10 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum10 s) => s.Value;
    public static explicit operator AlfaNum10(string s) => new(s);
}

public record AlfaNum13
{
    public string Value { get; init; }
    public AlfaNum13(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El valor no puede estar vacío", nameof(value));
        if (value.Length != 13 && value.Length != 11 && value.Length != 9)
            throw new ArgumentException("El valor debe tener 9, 11 o 13 caracteres", nameof(value));
        Value = value.Trim();
    }
    public override string ToString() => Value;
    public static implicit operator string(AlfaNum13 s) => s.Value;
    public static explicit operator AlfaNum13(string s) => new(s);
}

public record AlfaNum11
{
    public string Value { get; init; }
    public AlfaNum11(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El valor no puede estar vacío", nameof(value));
        if (value.Length != 13 && value.Length != 11 && value.Length != 9)
            throw new ArgumentException("El valor debe tener 9, 11 o 13 caracteres", nameof(value));
        Value = value.Trim();
    }
    public override string ToString() => Value;
    public static implicit operator string(AlfaNum11 s) => s.Value;
    public static explicit operator AlfaNum11(string s) => new(s);
}
```

### 1.17 Num10Validation / Num2Validation

```csharp
/// <summary>
/// Número entero de 1 a 10 dígitos.
/// </summary>
public record Num10
{
    public int Value { get; init; }
    public Num10(int value)
    {
        if (value < 0 || value > 9999999999)
            throw new ArgumentOutOfRangeException(nameof(value), "El valor debe ser de 1 a 10 dígitos");
        Value = value;
    }
    public override string ToString() => Value.ToString();
    public static implicit operator int(Num10 n) => n.Value;
    public static explicit operator Num10(int n) => new(n);
}

/// <summary>
/// Número entero de 1 a 2 dígitos.
/// </summary>
public record Num2
{
    public int Value { get; init; }
    public Num2(int value)
    {
        if (value < 0 || value > 99)
            throw new ArgumentOutOfRangeException(nameof(value), "El valor debe ser de 1 a 2 dígitos");
        Value = value;
    }
    public override string ToString() => Value.ToString();
    public static implicit operator int(Num2 n) => n.Value;
    public static explicit operator Num2(int n) => new(n);
}
```

### 1.18 CodigoSeguridadeCFType

```csharp
/// <summary>
/// Código de seguridad de factura electrónica (6 caracteres).
/// Hash generado en la factura de consumo original.
/// </summary>
public record CodigoSeguridadeCF
{
    public string Value { get; init; }

    public CodigoSeguridadeCF(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El código no puede estar vacío", nameof(value));

        if (value.Length != 6)
            throw new ArgumentException("El código debe tener exactamente 6 caracteres", nameof(value));

        Value = value.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(CodigoSeguridadeCF c) => c.Value;
    public static explicit operator CodigoSeguridadeCF(string s) => new(s);
}
```

### 1.19 CodificacionTipoImpuestosType

```csharp
/// <summary>
/// Códigos de impuestos adicionales según DGII.
/// </summary>
public enum CodificacionTipoImpuestos : int
{
    PropinaLegal = 1,                       // "001"
    ContribucionTelecomunicaciones = 2,    // "002"
    ISC_ServiciosSeguros = 3,              // "003"
    ISC_ServiciosTelecomunicaciones = 4,   // "004"
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

public static class ImpuestoAdicionalFormatter
{
    public static string ToString(CodificacionTipoImpuestos tipo)
    {
        var code = (int)tipo;
        return code.ToString("000");
    }

    public static CodificacionTipoImpuestos FromString(string s)
    {
        if (!int.TryParse(s, out int code) || code < 1 || code > 39)
            throw new ArgumentException($"Código de impuesto inválido: {s}");
        return (CodificacionTipoImpuestos)code;
    }
}
```

### 1.20 TipoMonedaType

```csharp
/// <summary>
/// Tipos de moneda para facturación en moneda extranjera.
/// </summary>
public enum TipoMoneda : int
{
    BRL = 1,   // Real Brazileño
    CAD = 2,   // Dólar Canadiense
    CHF = 3,   // Franco Suizo
    CNY = 4,   // Yuan Chino
    XDR = 5,   // Derecho Especial de Giro
    DKK = 6,   // Corona Danés
    EUR = 7,   // Euro
    GBP = 8,   // Libra Esterlina
    JPY = 9,   // Yen Japonés
    NOK = 10,  // Corona Noruega
    SCP = 11,  // Libra Escocesa
    SEK = 12,  // Corona Sueca
    USD = 13,  // Dólar Estadounidense
    VEF = 14,  // Bolívar Fuerte Venezolano
    HTG = 15,  // Gourde Haitiano
    MXN = 16,  // Peso Mexicano
    COP = 17   // Peso Colombiano
}

public static class MonedaFormatter
{
    public static string ToString(TipoMoneda moneda) => moneda switch
    {
        TipoMoneda.BRL => "BRL",
        TipoMoneda.CAD => "CAD",
        TipoMoneda.CHF => "CHF",
        TipoMoneda.CNY => "CHY",
        TipoMoneda.XDR => "XDR",
        TipoMoneda.DKK => "DKK",
        TipoMoneda.EUR => "EUR",
        TipoMoneda.GBP => "GBP",
        TipoMoneda.JPY => "JPY",
        TipoMoneda.NOK => "NOK",
        TipoMoneda.SCP => "SCP",
        TipoMoneda.SEK => "SEK",
        TipoMoneda.USD => "USD",
        TipoMoneda.VEF => "VEF",
        TipoMoneda.HTG => "HTG",
        TipoMoneda.MXN => "MXN",
        TipoMoneda.COP => "COP",
        _ => throw new ArgumentOutOfRangeException(nameof(moneda))
    };

    public static TipoMoneda FromString(string s) => s switch
    {
        "BRL" => TipoMoneda.BRL,
        "CAD" => TipoMoneda.CAD,
        "CHF" => TipoMoneda.CHF,
        "CHY" => TipoMoneda.CNY,
        "XDR" => TipoMoneda.XDR,
        "DKK" => TipoMoneda.DKK,
        "EUR" => TipoMoneda.EUR,
        "GBP" => TipoMoneda.GBP,
        "JPY" => TipoMoneda.JPY,
        "NOK" => TipoMoneda.NOK,
        "SCP" => TipoMoneda.SCP,
        "SEK" => TipoMoneda.SEK,
        "USD" => TipoMoneda.USD,
        "VEF" => TipoMoneda.VEF,
        "HTG" => TipoMoneda.HTG,
        "MXN" => TipoMoneda.MXN,
        "COP" => TipoMoneda.COP,
        _ => throw new ArgumentException($"Tipo de moneda inválido: {s}")
    };
}
```

### 1.21 ProvinciaMunicipioType

```csharp
/// <summary>
/// Código de provincia y municipio de República Dominicana.
/// Formato: 6 dígitos (PP0000 para provincia, PP0M00 para municipio).
/// </summary>
public record ProvinciaMunicipio
{
    public string Value { get; init; }

    public ProvinciaMunicipio(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El código no puede estar vacío", nameof(value));

        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{6}$"))
            throw new ArgumentException(
                $"Código de provincia/municipio inválido: '{value}'. Debe tener 6 dígitos.",
                nameof(value));

        Value = value;
    }

    public string Provincia => Value[..2];
    public string Secuencia => Value[2..];

    public override string ToString() => Value;
    public static implicit operator string(ProvinciaMunicipio p) => p.Value;
    public static explicit operator ProvinciaMunicipio(string s) => new(s);
}
```

### 1.22 IndicadorFacturacionType

```csharp
/// <summary>
/// Indicador de facturación para items.
/// 0=NoFacturable (ITBIS 18% sobre margen)
/// 1=ITBIS 1 (18%)
/// 2=ITBIS 2 (16%)
/// 3=ITBIS 3 (0%)
/// 4=Exento (E)
/// </summary>
public enum IndicadorFacturacionType : int
{
    NoFacturable18 = 0,
    ITBIS1_18 = 1,
    ITBIS2_16 = 2,
    ITBIS3_0 = 3,
    Exento = 4
}
```

### 1.23 IndicadorBienoServicioType

```csharp
/// <summary>
/// Indicador de bien o servicio.
/// 1=Biens, 2=Servicio
/// </summary>
public enum IndicadorBienoServicioType : int
{
    Bien = 1,
    Servicio = 2
}
```

### 1.24 IndicadorEnvioDiferidoType

```csharp
/// <summary>
/// Indicador de envío diferido. Solo valor: 1 (autorizado).
/// </summary>
public enum IndicadorEnvioDiferidoType : int
{
    Autorizado = 1
}
```

### 1.25 IndicadorMontoGravadoType

```csharp
/// <summary>
/// Indicador de montos gravados.
/// 0=Valor 0 si los montos en las líneas no tienen ITBIS incluido.
/// 1=Valor 1 si los montos en las líneas se encuentran con ITBIS incluido.
/// </summary>
public enum IndicadorMontoGravadoType : int
{
    NoIncluyeITBIS = 0,
    IncluyeITBIS = 1
}
```

### 1.26 IndicadorServicioTodoIncluidoType

```csharp
/// <summary>
/// Indicador de servicio todo incluido. Solo valor: 1.
/// </summary>
public enum IndicadorServicioTodoIncluidoType : int
{
    TodoIncluido = 1
}
```

### 1.27 IndicadorNorma1007Type

```csharp
/// <summary>
/// Indicador norma 1007.
/// 0=No incluir, 1=Incluir
/// </summary>
public enum IndicadorNorma1007Type : int
{
    NoIncluir = 0,
    Incluir = 1
}
```

### 1.28 IndicadorAgenteRetencionoPercepcionType

```csharp
/// <summary>
/// Indicador de agente de retención o percepción.
/// 1=Retención, 2=Percepción
/// </summary>
public enum IndicadorAgenteRetencionoPercepcionType : int
{
    Retencion = 1,
    Percepcion = 2
}
```

### 1.29 TipoDescuentoRecargoType

```csharp
/// <summary>
/// Tipo de descuento o recargo.
/// $=Monto fijo, %=Porcentaje
/// </summary>
public enum TipoDescuentoRecargoType : char
{
    Monto = '$',
    Porcentaje = '%'
}
```

### 1.30 TipoAjusteType

```csharp
/// <summary>
/// Tipo de ajuste: D=Descuento, R=Recargo
/// </summary>
public enum TipoAjusteType : char
{
    Descuento = 'D',
    Recargo = 'R'
}
```

### 1.31 IndicadorFacturacionDRType

```csharp
/// <summary>
/// Indicador de facturación para descuentos/recargos.
/// 1=ITBIS 1 (18%), 2=ITBIS 2 (16%), 3=ITBIS 3 (0%), 4=Exento (E)
/// </summary>
public enum IndicadorFacturacionDRType : int
{
    ITBIS1_18 = 1,
    ITBIS2_16 = 2,
    ITBIS3_0 = 3,
    Exento = 4
}
```

### 1.32 TipoCuentaPagoType

```csharp
/// <summary>
/// Tipo de cuenta de pago: CT=Cuenta Corriente, AH=Ahorro, OT=Otra
/// </summary>
public enum TipoCuentaPagoType : string
{
    CtaCorriente = "CT",
    Ahorro = "AH",
    Otra = "OT"
}
```

### 1.33 CodigoModificacionType

```csharp
/// <summary>
/// Código de modificación de NCF.
/// 1=Anula el NCF modificado
/// 2=Corrige texto del comprobante fiscal modificado
/// 3=Corrige montos del NCF modificado
/// 4=Reemplazo NCF emitido en contingencia
/// 5=Referencia Factura Consumo Electrónica
/// </summary>
public enum CodigoModificacionType : int
{
    AnulaNCF = 1,
    CorrigeTexto = 2,
    CorrigeMontos = 3,
    ReemplazoContingencia = 4,
    ReferenciaFacturaConsumo = 5
}
```

### 1.34 UnidadMedidaType

```csharp
/// <summary>
/// Unidades de medida según DGII (1-62).
/// </summary>
public enum UnidadMedidaType : int
{
    Barril = 1,           // BARR
    Bolsa = 2,            // BOL
    Bote = 3,             // BOT
    Bulto = 4,            // BULTO
    Botella = 5,          // BOTELLA
    Caja_Cajon = 6,       // CAJ
    Cajetilla = 7,        // CAJETILLA
    Centimetro = 8,       // CM
    Cilindro = 9,         // CIL
    Conjunto = 10,        // CONJ
    Contenedor = 11,      // CONT
    Dia = 12,             // DÍA
    Docena = 13,          // DOC
    Fardo = 14,           // FARD
    Galones = 15,         // GL
    Grado = 16,           // GRAD
    Gramo = 17,           // GR
    Granel = 18,          // GRAN
    Hora = 19,            // HOR
    Huacal = 20,          // HUAC
    Kilogramo = 21,       // KG
    KilovatioHora = 22,   // kWh
    Libra = 23,           // LB
    Litro = 24,           // LITRO
    Lote = 25,            // LOT
    Metro = 26,           // M
    Metro_Cuadrado = 27,  // M2
    Metro_Cubico = 28,    // M3
    MMBTU = 29,           // MMBTU
    Minuto = 30,          // MIN
    Paquete = 31,         // PAQ
    Par = 32,             // PAR
    Pie = 33,             // PIE
    Pieza = 34,           // PZA
    Rollo = 35,           // ROL
    Sobre = 36,           // SOBR
    Segundo = 37,         // SEG
    Tanque = 38,          // TANQUE
    Tonelada = 39,        // TONE
    Tubo = 40,            // TUB
    Yarda = 41,           // YD
    Yarda_Cuadrada = 42,  // YD2
    Unidad = 43,          // UND
    Elemento = 44,        // EA
    Millar = 45,          // MILLAR
    Saco = 46,            // SAC
    Lata = 47,            // LAT
    Display = 48,         // DIS
    Bidon = 49,           // BID
    Racion = 50,          // RAC
    Quintal = 51,         // Q
    ToneladasRegistroBruto = 52, // GRT
    Pie_Cuadrado = 53,    // P2
    Pasajero = 54,        // PAX
    Pulgadas = 55,        // PULG
    ParqueoBarcosMuelle = 56, // STAY
    Bandeja = 57,         // BDJ
    Hectarea = 58,        // HA
    Mililitro = 59,       // ML
    Miligramo = 60,       // MG
    Onzas = 61,           // OZ
    OnzasTroy = 62        // OZT
}

public static class UnidadMedidaFormatter
{
    public static string GetDescripcion(UnidadMedidaType unidad) => unidad switch
    {
        UnidadMedidaType.Barril => "BARR - Barril",
        UnidadMedidaType.Bolsa => "BOL - Bolsa",
        UnidadMedidaType.Bote => "BOT - Bote",
        UnidadMedidaType.Bulto => "BULTO - Bultos",
        UnidadMedidaType.Botella => "BOTELLA - Botella",
        UnidadMedidaType.Caja_Cajon => "CAJ - Caja/Cajón",
        UnidadMedidaType.Cajetilla => "CAJETILLA - Cajetilla",
        UnidadMedidaType.Centimetro => "CM - Centímetro",
        UnidadMedidaType.Cilindro => "CIL - Cilindro",
        UnidadMedidaType.Conjunto => "CONJ - Conjunto",
        UnidadMedidaType.Contenedor => "CONT - Contenedor",
        UnidadMedidaType.Dia => "DÍA - Día",
        UnidadMedidaType.Docena => "DOC - Docena",
        UnidadMedidaType.Fardo => "FARD - Fardo",
        UnidadMedidaType.Galones => "GL - Galones",
        UnidadMedidaType.Grado => "GRAD - Grado",
        UnidadMedidaType.Gramo => "GR - Gramo",
        UnidadMedidaType.Granel => "GRAN - Granel",
        UnidadMedidaType.Hora => "HOR - Hora",
        UnidadMedidaType.Huacal => "HUAC - Huacal",
        UnidadMedidaType.Kilogramo => "KG - Kilogramo",
        UnidadMedidaType.KilovatioHora => "kWh - Kilovatio Hora",
        UnidadMedidaType.Libra => "LB - Libra",
        UnidadMedidaType.Litro => "LITRO - Litro",
        UnidadMedidaType.Lote => "LOT - Lote",
        UnidadMedidaType.Metro => "M - Metro",
        UnidadMedidaType.Metro_Cuadrado => "M2 - Metro Cuadrado",
        UnidadMedidaType.Metro_Cubico => "M3 - Metro Cúbico",
        UnidadMedidaType.MMBTU => "MMBTU - Millones de Unidades Térmicas",
        UnidadMedidaType.Minuto => "MIN - Minuto",
        UnidadMedidaType.Paquete => "PAQ - Paquete",
        UnidadMedidaType.Par => "PAR - Par",
        UnidadMedidaType.Pie => "PIE - Pie",
        UnidadMedidaType.Pieza => "PZA - Pieza",
        UnidadMedidaType.Rollo => "ROL - Rollo",
        UnidadMedidaType.Sobre => "SOBR - Sobre",
        UnidadMedidaType.Segundo => "SEG - Segundo",
        UnidadMedidaType.Tanque => "TANQUE - Tanque",
        UnidadMedidaType.Tonelada => "TONE - Tonelada",
        UnidadMedidaType.Tubo => "TUB - Tubo",
        UnidadMedidaType.Yarda => "YD - Yarda",
        UnidadMedidaType.Yarda_Cuadrada => "YD2 - Yarda cuadrada",
        UnidadMedidaType.Unidad => "UND - Unidad",
        UnidadMedidaType.Elemento => "EA - Elemento",
        UnidadMedidaType.Millar => "MILLAR - Millar",
        UnidadMedidaType.Saco => "SAC - Saco",
        UnidadMedidaType.Lata => "LAT - Lata",
        UnidadMedidaType.Display => "DIS - Display",
        UnidadMedidaType.Bidon => "BID - Bidón",
        UnidadMedidaType.Racion => "RAC - Ración",
        UnidadMedidaType.Quintal => "Q - Quintal",
        UnidadMedidaType.ToneladasRegistroBruto => "GRT - Toneladas de registro bruto",
        UnidadMedidaType.Pie_Cuadrado => "P2 - Pie Cuadrado",
        UnidadMedidaType.Pasajero => "PAX - Pasajero",
        UnidadMedidaType.Pulgadas => "PULG - Pulgadas",
        UnidadMedidaType.ParqueoBarcosMuelle => "STAY - Parqueo Barcos en Muelle",
        UnidadMedidaType.Bandeja => "BDJ - Bandeja",
        UnidadMedidaType.Hectarea => "HA - Hectárea",
        UnidadMedidaType.Mililitro => "ML - Mililitro",
        UnidadMedidaType.Miligramo => "MG - Miligramo",
        UnidadMedidaType.Onzas => "OZ - Onzas",
        UnidadMedidaType.OnzasTroy => "OZT - Onzas Troy",
        _ => throw new ArgumentOutOfRangeException(nameof(unidad))
    };

    public static string GetAbreviatura(UnidadMedidaType unidad) => unidad switch
    {
        UnidadMedidaType.Barril => "BARR",
        UnidadMedidaType.Bolsa => "BOL",
        UnidadMedidaType.Bote => "BOT",
        UnidadMedidaType.Bulto => "BULTO",
        UnidadMedidaType.Botella => "BOTELLA",
        UnidadMedidaType.Caja_Cajon => "CAJ",
        UnidadMedidaType.Cajetilla => "CAJETILLA",
        UnidadMedidaType.Centimetro => "CM",
        UnidadMedidaType.Cilindro => "CIL",
        UnidadMedidaType.Conjunto => "CONJ",
        UnidadMedidaType.Contenedor => "CONT",
        UnidadMedidaType.Dia => "DÍA",
        UnidadMedidaType.Docena => "DOC",
        UnidadMedidaType.Fardo => "FARD",
        UnidadMedidaType.Galones => "GL",
        UnidadMedidaType.Grado => "GRAD",
        UnidadMedidaType.Gramo => "GR",
        UnidadMedidaType.Granel => "GRAN",
        UnidadMedidaType.Hora => "HOR",
        UnidadMedidaType.Huacal => "HUAC",
        UnidadMedidaType.Kilogramo => "KG",
        UnidadMedidaType.KilovatioHora => "kWh",
        UnidadMedidaType.Libra => "LB",
        UnidadMedidaType.Litro => "LITRO",
        UnidadMedidaType.Lote => "LOT",
        UnidadMedidaType.Metro => "M",
        UnidadMedidaType.Metro_Cuadrado => "M2",
        UnidadMedidaType.Metro_Cubico => "M3",
        UnidadMedidaType.MMBTU => "MMBTU",
        UnidadMedidaType.Minuto => "MIN",
        UnidadMedidaType.Paquete => "PAQ",
        UnidadMedidaType.Par => "PAR",
        UnidadMedidaType.Pie => "PIE",
        UnidadMedidaType.Pieza => "PZA",
        UnidadMedidaType.Rollo => "ROL",
        UnidadMedidaType.Sobre => "SOBR",
        UnidadMedidaType.Segundo => "SEG",
        UnidadMedidaType.Tanque => "TANQUE",
        UnidadMedidaType.Tonelada => "TONE",
        UnidadMedidaType.Tubo => "TUB",
        UnidadMedidaType.Yarda => "YD",
        UnidadMedidaType.Yarda_Cuadrada => "YD2",
        UnidadMedidaType.Unidad => "UND",
        UnidadMedidaType.Elemento => "EA",
        UnidadMedidaType.Millar => "MILLAR",
        UnidadMedidaType.Saco => "SAC",
        UnidadMedidaType.Lata => "LAT",
        UnidadMedidaType.Display => "DIS",
        UnidadMedidaType.Bidon => "BID",
        UnidadMedidaType.Racion => "RAC",
        UnidadMedidaType.Quintal => "Q",
        UnidadMedidaType.ToneladasRegistroBruto => "GRT",
        UnidadMedidaType.Pie_Cuadrado => "P2",
        UnidadMedidaType.Pasajero => "PAX",
        UnidadMedidaType.Pulgadas => "PULG",
        UnidadMedidaType.ParqueoBarcosMuelle => "STAY",
        UnidadMedidaType.Bandeja => "BDJ",
        UnidadMedidaType.Hectarea => "HA",
        UnidadMedidaType.Mililitro => "ML",
        UnidadMedidaType.Miligramo => "MG",
        UnidadMedidaType.Onzas => "OZ",
        UnidadMedidaType.OnzasTroy => "OZT",
        _ => throw new ArgumentOutOfRangeException(nameof(unidad))
    };

    public static UnidadMedidaType FromCode(int code) => code switch
    {
        1 => UnidadMedidaType.Barril,
        2 => UnidadMedidaType.Bolsa,
        3 => UnidadMedidaType.Bote,
        4 => UnidadMedidaType.Bulto,
        5 => UnidadMedidaType.Botella,
        6 => UnidadMedidaType.Caja_Cajon,
        7 => UnidadMedidaType.Cajetilla,
        8 => UnidadMedidaType.Centimetro,
        9 => UnidadMedidaType.Cilindro,
        10 => UnidadMedidaType.Conjunto,
        11 => UnidadMedidaType.Contenedor,
        12 => UnidadMedidaType.Dia,
        13 => UnidadMedidaType.Docena,
        14 => UnidadMedidaType.Fardo,
        15 => UnidadMedidaType.Galones,
        16 => UnidadMedidaType.Grado,
        17 => UnidadMedidaType.Gramo,
        18 => UnidadMedidaType.Granel,
        19 => UnidadMedidaType.Hora,
        20 => UnidadMedidaType.Huacal,
        21 => UnidadMedidaType.Kilogramo,
        22 => UnidadMedidaType.KilovatioHora,
        23 => UnidadMedidaType.Libra,
        24 => UnidadMedidaType.Litro,
        25 => UnidadMedidaType.Lote,
        26 => UnidadMedidaType.Metro,
        27 => UnidadMedidaType.Metro_Cuadrado,
        28 => UnidadMedidaType.Metro_Cubico,
        29 => UnidadMedidaType.MMBTU,
        30 => UnidadMedidaType.Minuto,
        31 => UnidadMedidaType.Paquete,
        32 => UnidadMedidaType.Par,
        33 => UnidadMedidaType.Pie,
        34 => UnidadMedidaType.Pieza,
        35 => UnidadMedidaType.Rrollo,
        36 => UnidadMedidaType.Sobre,
        37 => UnidadMedidaType.Segundo,
        38 => UnidadMedidaType.Tanque,
        39 => UnidadMedidaType.Tonelada,
        40 => UnidadMedidaType.Tubo,
        41 => UnidadMedidaType.Yarda,
        42 => UnidadMedidaType.Yarda_Cuadrada,
        43 => UnidadMedidaType.Unidad,
        44 => UnidadMedidaType.Elemento,
        45 => UnidadMedidaType.Millar,
        46 => UnidadMedidaType.Saco,
        47 => UnidadMedidaType.Lata,
        48 => UnidadMedidaType.Display,
        49 => UnidadMedidaType.Bidon,
        50 => UnidadMedidaType.Racion,
        51 => UnidadMedidaType.Quintal,
        52 => UnidadMedidaType.ToneladasRegistroBruto,
        53 => UnidadMedidaType.Pie_Cuadrado,
        54 => UnidadMedidaType.Pasajero,
        55 => UnidadMedidaType.Pulgadas,
        56 => UnidadMedidaType.ParqueoBarcosMuelle,
        57 => UnidadMedidaType.Bandeja,
        58 => UnidadMedidaType.Hectarea,
        59 => UnidadMedidaType.Mililitro,
        60 => UnidadMedidaType.Miligramo,
        61 => UnidadMedidaType.Onzas,
        62 => UnidadMedidaType.OnzasTroy,
        _ => throw new ArgumentOutOfRangeException(nameof(code), $"Código de unidad de medida inválido: {code}")
    };
}
```

---

## 2. Tipos Complejos (ComplexTypes) del XSD

### 2.1 Encabezado (del e-CF 32)

```csharp
/// <summary>
/// Encabezado del comprobante e-CF tipo 32 (Factura de Consumo Electrónica).
/// Ver XSD: e-CF 32 v.1.0.xsd, líneas 6-209
/// </summary>
public record EncabezadoECF32
{
    public VersionType Version { get; init; } = new(1.0m);
    public IdDocECF32 IdDoc { get; init; } = new();
    public EmisorECF32 Emisor { get; init; } = new();
    public CompradorECF32 Comprador { get; init; } = new();
    public InformacionesAdicionalesECF32? InformacionesAdicionales { get; init; }
    public TransporteECF32? Transporte { get; init; }
    public TotalesECF32 Totales { get; init; } = new();
    public OtraMonedaECF32? OtraMoneda { get; init; }
}

public record IdDocECF32
{
    public TipoeCFType TipoeCF { get; init; } = TipoeCFType.FacturaConsumo;
    public eNCFType eNCF { get; init; } = new("B0000000000001");
    public IndicadorEnvioDiferidoType? IndicadorEnvioDiferido { get; init; }
    public IndicadorMontoGravadoType? IndicadorMontoGravado { get; init; }
    public IndicadorServicioTodoIncluidoType? IndicadorServicioTodoIncluido { get; init; }
    public TipoIngresosType TipoIngresos { get; init; } = TipoIngresosType.IngresosOperaciones;
    public TipoPagoType TipoPago { get; init; } = TipoPagoType.Contado;
    public FechaDominicana? FechaLimitePago { get; init; }
    public string? TerminoPago { get; init; }
    public List<FormaDePagoECF32>? TablaFormasPago { get; init; }
    public TipoCuentaPagoType? TipoCuentaPago { get; init; }
    public string? NumeroCuentaPago { get; init; }       // maxLength 28
    public string? BancoPago { get; init; }              // maxLength 75
    public FechaDominicana? FechaDesde { get; init; }
    public FechaDominicana? FechaHasta { get; init; }
    public Interger6Type? TotalPaginas { get; init; }
}

public record FormaDePagoECF32
{
    public FormaPagoType FormaPago { get; init; }
    public DecimalMayorIgualCero? MontoPago { get; init; }
}

public record EmisorECF32
{
    public RNC RNCEmisor { get; init; } = new("00000000000");
    public AlfaNum150 RazonSocialEmisor { get; init; } = new("EMPRESA EMISORA S.A.");
    public AlfaNum150? NombreComercial { get; init; }
    public AlfaNum20? Sucursal { get; init; }
    public AlfaNum100 DireccionEmisor { get; init; } = new("Dirección de la empresa");
    public ProvinciaMunicipio? Municipio { get; init; }
    public ProvinciaMunicipio? Provincia { get; init; }
    public List<string>? Telefonos { get; init; }        // TelefonoValidationType: XXX-XXX-XXXX
    public string? CorreoEmisor { get; init; }           // CorreoValidationType: email
    public AlfaNum50? WebSite { get; init; }
    public AlfaNum100? ActividadEconomica { get; init; }
    public AlfaNum60? CodigoVendedor { get; init; }
    public AlfaNum20? NumeroFacturaInterna { get; init; }
    public Interger6Type? NumeroPedidoInterno { get; init; }
    public AlfaNum20? ZonaVenta { get; init; }
    public AlfaNum20? RutaVenta { get; init; }
    public AlfaNum250? InformacionAdicionalEmisor { get; init; }
    public FechaDominicana FechaEmision { get; init; } = new(DateOnly.Parse("2026-09-15"));
}

public record CompradorECF32
{
    public RNC? RNCComprador { get; init; }
    public AlfaNum20? IdentificadorExtranjero { get; init; }
    public AlfaNum150? RazonSocialComprador { get; init; }
    public AlfaNum80? ContactoComprador { get; init; }
    public string? CorreoComprador { get; init; }        // CorreoValidationType
    public AlfaNum100? DireccionComprador { get; init; }
    public ProvinciaMunicipio? MunicipioComprador { get; init; }
    public ProvinciaMunicipio? ProvinciaComprador { get; init; }
    public FechaDominicana? FechaEntrega { get; init; }
    public AlfaNum100? ContactoEntrega { get; init; }
    public AlfaNum100? DireccionEntrega { get; init; }
    public string? TelefonoAdicional { get; init; }      // TelefonoValidationType
    public FechaDominicana? FechaOrdenCompra { get; init; }
    public AlfaNum20? NumeroOrdenCompra { get; init; }
    public AlfaNum20? CodigoInternoComprador { get; init; }
    public Alfa20? ResponsablePago { get; init; }        // Alfa20Type
    public AlfaNum150? InformacionAdicionalComprador { get; init; }
}

/// <summary>
/// Alfa20Type - cadena de máximo 20 caracteres alfabéticos.
/// </summary>
public record Alfa20
{
    public string Value { get; init; }
    public Alfa20(string value)
    {
        if (value != null && value.Length > 20)
            throw new ArgumentException("Máximo 20 caracteres", nameof(value));
        if (value != null && !System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-zA-Z ]+$"))
            throw new ArgumentException("Solo caracteres alfabéticos y espacios permitidos", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(Alfa20 a) => a.Value;
    public static explicit operator Alfa20(string s) => new(s);
}

public record InformacionesAdicionalesECF32
{
    public FechaDominicana? FechaEmbarque { get; init; }
    public AlfaNum25? NumeroEmbarque { get; init; }
    public AlfaNum100? NumeroContenedor { get; init; }
    public Interger6Type? NumeroReferencia { get; init; }
    public DecimalMayorIgualCero? PesoBruto { get; init; }
    public DecimalMayorIgualCero? PesoNeto { get; init; }
    public UnidadMedidaType? UnidadPesoBruto { get; init; }
    public UnidadMedidaType? UnidadPesoNeto { get; init; }
    public DecimalMayorIgualCero? CantidadBulto { get; init; }
    public UnidadMedidaType? UnidadBulto { get; init; }
    public DecimalMayorCero? VolumenBulto { get; init; }
    public UnidadMedidaType? UnidadVolumen { get; init; }
}

public record DecimalMayorCero
{
    public decimal Value { get; init; }
    public DecimalMayorCero(decimal value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "El valor debe ser > 0");
        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
    public override string ToString() => Value.ToString("F2");
    public static implicit operator decimal(DecimalMayorCero d) => d.Value;
    public static explicit operator DecimalMayorCero(decimal d) => new(d);
}

public record TransporteECF32
{
    public AlfaNum20? Conductor { get; init; }
    public Interger6Type? DocumentoTransporte { get; init; }
    public AlfaNum10? Ficha { get; init; }
    public AlfaNum7? Placa { get; init; }
    public AlfaNum20? RutaTransporte { get; init; }
    public AlfaNum20? ZonaTransporte { get; init; }
    public AlfaNum20? NumeroAlbaran { get; init; }
}

public record TotalesECF32
{
    public DecimalMayorIgualCero? MontoGravadoTotal { get; init; }
    public DecimalMayorIgualCero? MontoGravadoI1 { get; init; }
    public DecimalMayorIgualCero? MontoGravadoI2 { get; init; }
    public DecimalMayorIgualCero? MontoGravadoI3 { get; init; }
    public DecimalMayorIgualCero? MontoExento { get; init; }
    public Num2? ITBIS1 { get; init; }
    public Num2? ITBIS2 { get; init; }
    public Num2? ITBIS3 { get; init; }
    public DecimalMayorIgualCero? TotalITBIS { get; init; }
    public DecimalMayorIgualCero? TotalITBIS1 { get; init; }
    public DecimalMayorIgualCero? TotalITBIS2 { get; init; }
    public DecimalMayorIgualCero? TotalITBIS3 { get; init; }
    public DecimalMayorCero? MontoImpuestoAdicional { get; init; }
    public List<ImpuestoAdicionalECF32>? ImpuestosAdicionales { get; init; }
    public DecimalMayorIgualCero MontoTotal { get; init; } = new(0);
    public DecimalMontoNegativoPositivo? MontoNoFacturable { get; init; }
    public DecimalMontoNegativoPositivo? MontoPeriodo { get; init; }
    public DecimalMontoNegativoPositivo? SaldoAnterior { get; init; }
    public DecimalMayorIgualCero? MontoAvancePago { get; init; }
    public DecimalMontoNegativoPositivo? ValorPagar { get; init; }
}

public record DecimalMontoNegativoPositivo
{
    public decimal Value { get; init; }
    public DecimalMontoNegativoPositivo(decimal value)
    {
        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
    public override string ToString() => Value.ToString("F2");
    public static implicit operator decimal(DecimalMontoNegativoPositivo d) => d.Value;
    public static explicit operator DecimalMontoNegativoPositivo(decimal d) => new(d);
}

public record ImpuestoAdicionalECF32
{
    public CodificacionTipoImpuestos TipoImpuesto { get; init; }
    public Decimal5D1or2MayorCero TasaImpuestoAdicional { get; init; } = new(0);
    public DecimalMayorCero? MontoImpuestoSelectivoConsumoEspecifico { get; init; }
    public DecimalMayorCero? MontoImpuestoSelectivoConsumoAdvalorem { get; init; }
    public DecimalMayorCero? OtrosImpuestosAdicionales { get; init; }
}

public record Decimal5D1or2MayorCero
{
    public decimal Value { get; init; }
    public Decimal5D1or2MayorCero(decimal value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "La tasa debe ser > 0");
        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
    public override string ToString() => Value.ToString("F2");
    public static implicit operator decimal(Decimal5D1or2MayorCero d) => d.Value;
    public static explicit operator Decimal5D1or2MayorCero(decimal d) => new(d);
}

public record OtraMonedaECF32
{
    public TipoMoneda? TipoMoneda { get; init; }
    public Decimal7D1or4MayorCero? TipoCambio { get; init; }
    public DecimalMayorIgualCero? MontoGravadoTotalOtraMoneda { get; init; }
    public DecimalMayorIgualCero? MontoGravado1OtraMoneda { get; init; }
    public DecimalMayorIgualCero? MontoGravado2OtraMoneda { get; init; }
    public DecimalMayorIgualCero? MontoGravado3OtraMoneda { get; init; }
    public DecimalMayorIgualCero? MontoExentoOtraMoneda { get; init; }
    public DecimalMayorIgualCero? TotalITBISOtraMoneda { get; init; }
    public DecimalMayorIgualCero? TotalITBIS1OtraMoneda { get; init; }
    public DecimalMayorIgualCero? TotalITBIS2OtraMoneda { get; init; }
    public DecimalMayorIgualCero? TotalITBIS3OtraMoneda { get; init; }
    public DecimalMayorCero? MontoImpuestoAdicionalOtraMoneda { get; init; }
    public List<ImpuestoAdicionalOtraMonedaECF32>? ImpuestosAdicionalesOtraMoneda { get; init; }
    public DecimalMayorIgualCero? MontoTotalOtraMoneda { get; init; }
}

public record Decimal7D1or4MayorCero
{
    public decimal Value { get; init; }
    public Decimal7D1or4MayorCero(decimal value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "El tipo de cambio debe ser > 0");
        Value = Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }
    public override string ToString() => Value.ToString("F4");
    public static implicit operator decimal(Decimal7D1or4MayorCero d) => d.Value;
    public static explicit operator Decimal7D1or4MayorCero(decimal d) => new(d);
}

public record ImpuestoAdicionalOtraMonedaECF32
{
    public CodificacionTipoImpuestos TipoImpuestoOtraMoneda { get; init; }
    public Decimal5D1or2MayorCero TasaImpuestoAdicionalOtraMoneda { get; init; } = new(0);
    public DecimalMayorCero? MontoImpuestoSelectivoConsumoEspecificoOtraMoneda { get; init; }
    public DecimalMayorCero? MontoImpuestoSelectivoConsumoAdvaloremOtraMoneda { get; init; }
    public DecimalMayorCero? OtrosImpuestosAdicionalesOtraMoneda { get; init; }
}
```

### 2.2 DetallesItems (Items)

```csharp
/// <summary>
/// Contenedor de items del e-CF 32.
/// Máximo 1000 items por factura.
/// </summary>
public record DetallesItemsECF32
{
    public List<ItemECF32> Items { get; init; } = new();
}

/// <summary>
/// Item individual en la factura e-CF 32.
/// Max 1000 items.
/// </summary>
public record ItemECF32
{
    public Num2 NumeroLinea { get; init; } = new(1);
    public List<CodigosItemECF32>? TablaCodigosItem { get; init; }  // hasta 5
    public IndicadorFacturacionType IndicadorFacturacion { get; init; }
    public IndicadorBienoServicioType IndicadorBienoServicio { get; init; }
    public AlfaNum80 NombreItem { get; init; } = new("");
    public AlfaNum1000? DescripcionItem { get; init; }
    public DecimalMayorCero CantidadItem { get; init; } = new(0);
    public UnidadMedidaType? UnidadMedida { get; init; }
    public DecimalMayorIgualCero? CantidadReferencia { get; init; }
    public UnidadMedidaType? UnidadReferencia { get; init; }
    public List<SubcantidadItemECF32>? TablaSubcantidad { get; init; }  // hasta 5
    public DecimalMayorCero? GradosAlcohol { get; init; }
    public FechaDominicana? FechaElaboracion { get; init; }
    public FechaDominicana? FechaVencimientoItem { get; init; }
    public MineriaItemECF32? Mineria { get; init; }
    public Decimal20D1or4MayorIgualCero PrecioUnitarioItem { get; init; } = new(0);
    public DecimalMayorIgualCero? DescuentoMonto { get; init; }
    public List<SubDescuentoItemECF32>? TablaSubDescuento { get; init; }  // hasta 12
    public DecimalMayorIgualCero? RecargoMonto { get; init; }
    public List<SubRecargoItemECF32>? TablaSubRecargo { get; init; }  // hasta 12
    public List<ImpuestoAdicionalItemECF32>? TablaImpuestoAdicional { get; init; }  // hasta 2
    public OtraMonedaDetalleItemECF32? OtraMonedaDetalle { get; init; }
    public DecimalMayorIgualCero MontoItem { get; init; } = new(0);
}

public record CodigosItemECF32
{
    public AlfaNum14 TipoCodigo { get; init; } = new("");
    public AlfaNum35 CodigoItem { get; init; } = new("");
}

public record AlfaNum14
{
    public string Value { get; init; }
    public AlfaNum14(string value)
    {
        if (value != null && value.Length > 14)
            throw new ArgumentException("Máximo 14 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum14 a) => a.Value;
    public static explicit operator AlfaNum14(string s) => new(s);
}

public record AlfaNum35
{
    public string Value { get; init; }
    public AlfaNum35(string value)
    {
        if (value != null && value.Length > 35)
            throw new ArgumentException("Máximo 35 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum35 a) => a.Value;
    public static explicit operator AlfaNum35(string s) => new(s);
}

public record SubcantidadItemECF32
{
    public Decimal19D1or3MayorIgualCero? Subcantidad { get; init; }
    public UnidadMedidaType? CodigoSubcantidad { get; init; }
}

public record Decimal19D1or3MayorIgualCero
{
    public decimal Value { get; init; }
    public Decimal19D1or3MayorIgualCero(decimal value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "La subcantidad debe ser >= 0");
        Value = Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }
    public override string ToString() => Value.ToString("F3");
    public static implicit operator decimal(Decimal19D1or3MayorIgualCero d) => d.Value;
    public static explicit operator Decimal19D1or3MayorIgualCero(decimal d) => new(d);
}

public record MineriaItemECF32
{
    public Decimal19D1or3MayorIgualCero? PesoNetoKilogramo { get; init; }
    public Decimal19D1or3MayorIgualCero? PesoNetoMineria { get; init; }
    public TipoAfiliacionType? TipoAfiliacion { get; init; }
    public LiquidacionType? Liquidacion { get; init; }
}

public enum TipoAfiliacionType : int
{
    Afiliada = 1,
    NoAfiliada = 2
}

public enum LiquidacionType : int
{
    Provisional = 1,
    Final = 2
}

public record Decimal20D1or4MayorIgualCero
{
    public decimal Value { get; init; }
    public Decimal20D1or4MayorIgualCero(decimal value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "El precio unitario debe ser >= 0");
        Value = Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }
    public override string ToString() => Value.ToString("F4");
    public static implicit operator decimal(Decimal20D1or4MayorIgualCero d) => d.Value;
    public static explicit operator Decimal20D1or4MayorIgualCero(decimal d) => new(d);
}

public record SubDescuentoItemECF32
{
    public TipoDescuentoRecargoType TipoSubDescuento { get; init; }
    public Decimal5D1or2MayorCero? SubDescuentoPorcentaje { get; init; }
    public DecimalMayorIgualCero? MontoSubDescuento { get; init; }
}

public record SubRecargoItemECF32
{
    public TipoDescuentoRecargoType TipoSubRecargo { get; init; }
    public Decimal5D1or2MayorCero? SubRecargoPorcentaje { get; init; }
    public DecimalMayorIgualCero? MontoSubRecargo { get; init; }
}

public record ImpuestoAdicionalItemECF32
{
    public CodificacionTipoImpuestos TipoImpuesto { get; init; }
}

public record OtraMonedaDetalleItemECF32
{
    public Decimal20D1or4MayorIgualCero? PrecioOtraMoneda { get; init; }
    public DecimalMayorIgualCero? DescuentoOtraMoneda { get; init; }
    public DecimalMayorIgualCero? RecargoOtraMoneda { get; init; }
    public DecimalMayorIgualCero? MontoItemOtraMoneda { get; init; }
}
```

### 2.3 Subtotales

```csharp
/// <summary>
/// Subtotales de la factura e-CF 32.
/// Máximo 20 subtotales.
/// </summary>
public record SubtotalesECF32
{
    public List<SubtotalECF32> Subtotals { get; init; } = new();
}

public record SubtotalECF32
{
    public Num2? NumeroSubTotal { get; init; }
    public AlfaNum40? DescripcionSubtotal { get; init; }
    public Num2? Orden { get; init; }
    public DecimalMayorIgualCero? SubTotalMontoGravadoTotal { get; init; }
    public DecimalMayorIgualCero? SubTotalMontoGravadoI1 { get; init; }
    public DecimalMayorIgualCero? SubTotalMontoGravadoI2 { get; init; }
    public DecimalMayorIgualCero? SubTotalMontoGravadoI3 { get; init; }
    public DecimalMayorIgualCero? SubTotaITBIS { get; init; }
    public DecimalMayorIgualCero? SubTotaITBIS1 { get; init; }
    public DecimalMayorIgualCero? SubTotaITBIS2 { get; init; }
    public DecimalMayorIgualCero? SubTotaITBIS3 { get; init; }
    public DecimalMayorIgualCero? SubTotalImpuestoAdicional { get; init; }
    public DecimalMayorIgualCero? SubTotalExento { get; init; }
    public DecimalMayorIgualCero? MontoSubTotal { get; init; }
    public Num2? Lineas { get; init; }
}

public record AlfaNum40
{
    public string Value { get; init; }
    public AlfaNum40(string value)
    {
        if (value != null && value.Length > 40)
            throw new ArgumentException("Máximo 40 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum40 a) => a.Value;
    public static explicit operator AlfaNum40(string s) => new(s);
}
```

### 2.4 DescuentosORecargos

```csharp
/// <summary>
/// Descuentos o recargos de la factura e-CF 32.
/// Máximo 20 descuentos/recargos.
/// </summary>
public record DescuentosORecargosECF32
{
    public List<DescuentoORecargoECF32> Descuentos { get; init; } = new();
}

public record DescuentoORecargoECF32
{
    public Num2 NumeroLinea { get; init; } = new(1);
    public TipoAjusteType TipoAjuste { get; init; }
    public IndicadorNorma1007Type? IndicadorNorma1007 { get; init; }
    public AlfaNum45? DescripcionDescuentooRecargo { get; init; }
    public TipoDescuentoRecargoType? TipoValor { get; init; }
    public Decimal5D1or2MayorCero? ValorDescuentooRecargo { get; init; }
    public DecimalMayorIgualCero? MontoDescuentooRecargo { get; init; }
    public DecimalMayorIgualCero? MontoDescuentooRecargoOtraMoneda { get; init; }
    public IndicadorFacturacionDRType? IndicadorFacturacionDescuentooRecargo { get; init; }
}

public record AlfaNum45
{
    public string Value { get; init; }
    public AlfaNum45(string value)
    {
        if (value != null && value.Length > 45)
            throw new ArgumentException("Máximo 45 caracteres", nameof(value));
        Value = value?.Trim();
    }
    public override string ToString() => Value ?? "";
    public static implicit operator string(AlfaNum45 a) => a.Value;
    public static explicit operator AlfaNum45(string s) => new(s);
}
```

### 2.5 Paginacion

```csharp
/// <summary>
/// Paginación de la factura e-CF 32.
/// Máximo 1000 páginas.
/// </summary>
public record PaginacionECF32
{
    public List<PaginaECF32> Paginas { get; init; } = new();
}

public record PaginaECF32
{
    public Num4? PaginaNo { get; init; }
    public Num4? NoLineaDesde { get; init; }
    public Num4? NoLineaHasta { get; init; }
    public DecimalMayorIgualCero? SubtotalMontoGravadoPagina { get; init; }
    public DecimalMayorIgualCero? SubtotalMontoGravado1Pagina { get; init; }
    public DecimalMayorIgualCero? SubtotalMontoGravado2Pagina { get; init; }
    public DecimalMayorIgualCero? SubtotalMontoGravado3Pagina { get; init; }
    public DecimalMayorIgualCero? SubtotalExentoPagina { get; init; }
    public DecimalMayorIgualCero? SubtotalItbisPagina { get; init; }
    public DecimalMayorIgualCero? SubtotalItbis1Pagina { get; init; }
    public DecimalMayorIgualCero? SubtotalItbis2Pagina { get; init; }
    public DecimalMayorIgualCero? SubtotalItbis3Pagina { get; init; }
    public DecimalMayorCero? SubtotalImpuestoAdicionalPagina { get; init; }
    public ImpuestoAdicionalPaginaECF32? SubtotalImpuestoSelectivoConsumoEspecifico { get; init; }
    public ImpuestoAdicionalPaginaECF32? SubtotalOtrosImpuesto { get; init; }
    public DecimalMayorIgualCero? MontoSubtotalPagina { get; init; }
    public DecimalMayorIgualCero? SubtotalMontoNoFacturablePagina { get; init; }
}

public record Num4
{
    public int Value { get; init; }
    public Num4(int value)
    {
        if (value < 0 || value > 9999)
            throw new ArgumentOutOfRangeException(nameof(value), "El valor debe ser de 1 a 4 dígitos");
        Value = value;
    }
    public override string ToString() => Value.ToString();
    public static implicit operator int(Num4 n) => n.Value;
    public static explicit operator Num4(int n) => new(n);
}

public record ImpuestoAdicionalPaginaECF32
{
    public DecimalMayorCero? SubtotalImpuestoSelectivoConsumoEspecificoPagina { get; init; }
    public DecimalMayorCero? SubtotalOtrosImpuesto { get; init; }
}

public record DecimalMayorCeroPagina
{
    public decimal Value { get; init; }
    public DecimalMayorCeroPagina(decimal value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "El valor debe ser > 0");
        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
    public override string ToString() => Value.ToString("F2");
    public static implicit operator decimal(DecimalMayorCeroPagina d) => d.Value;
    public static explicit operator DecimalMayorCeroPagina(decimal d) => new(d);
}
```

### 2.6 InformacionReferencia

```csharp
/// <summary>
/// Información de referencia para modificación de NCF.
/// </summary>
public record InformacionReferenciaECF32
{
    public AlfaNum11a19? NCFModificado { get; init; }
    public RNC? RNCOtroContribuyente { get; init; }
    public FechaDominicana? FechaNCFModificado { get; init; }
    public CodigoModificacionType? CodigoModificacion { get; init; }
}

public record AlfaNum11a19
{
    public string Value { get; init; }
    public AlfaNum11a19(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El valor no puede estar vacío", nameof(value));
        if (value.Length < 11 || value.Length > 19)
            throw new ArgumentException("El valor debe tener entre 11 y 19 caracteres", nameof(value));
        Value = value.Trim();
    }
    public override string ToString() => Value;
    public static implicit operator string(AlfaNum11a19 s) => s.Value;
    public static explicit operator AlfaNum11a19(string s) => new(s);
}
```

---

## 3. Tipo Complejo ANECF (Anulación)

```csharp
/// <summary>
/// Comprobante de anulación de NCF (ANECF).
/// Ver XSD: ANECF v.1.0.xsd
/// </summary>
public record ANECF
{
    public EncabezadoAnulacion Encabezado { get; init; } = new();
    public DetalleAnulacion DetalleAnulacion { get; init; } = new();
    public DateTime FechaHoraFirma { get; init; } = DateTime.Now;
}

public record EncabezadoAnulacion
{
    public VersionType Version { get; init; } = new(1.0m);
    public RNC RncEmisor { get; init; } = new("00000000000");
    public Num10 CantidadeNCFAnulados { get; init; } = new(0);
    public DateTime FechaHoraAnulacion;  // DateAndTimeValidation: dd-MM-yyyy HH:mm:ss
}

public record DetalleAnulacion
{
    public List<RangoAnulacion> Anulaciones { get; init; } = new();
}

public record RangoAnulacion
{
    public Num2 NoLinea { get; init; } = new(1);
    public CFType TipoeCF { get; init; } = CFType.FacturaConsumo;
    public TablaRangoSecuenciasAnuladas TablaRangoSecuenciasAnuladaseNCF { get; init; } = new();
    public Num10 CantidadeNCFAnulados { get; init; } = new(0);
}

public enum CFType : int
{
    FacturaCreditoFiscal = 31,
    FacturaConsumo = 32,
    NotaDeDebito = 33,
    NotaDeCredito = 34,
    Compras = 41,
    GastosMenores = 43,
    RegimenesEspeciales = 44,
    Gubernamental = 45,
    Exportaciones = 46,
    PagosAlExterior = 47
}

public record TablaRangoSecuenciasAnuladas
{
    public List<SecuenciaRango> Secuencias { get; init; } = new();
}

public record SecuenciaRango
{
    public AlfaNum13 SecuenciaeNCFDesde { get; init; } = new("");
    public AlfaNum13 SecuenciaeNCFHasta { get; init; } = new("");
}
```

---

## 4. Tipo Complejo ACECF (Aprobación Comercial)

```csharp
/// <summary>
/// Comprobante de aprobación comercial (ACECF).
/// Ver XSD: ACECF v.1.0.xsd
/// </summary>
public record ACECF
{
    public DetalleAprobacionComercial DetalleAprobacionComercial { get; init; } = new();
    public DateTime FechaHoraFirma { get; init; } = DateTime.Now;
}

public record DetalleAprobacionComercial
{
    public VersionType Version { get; init; } = new(1.0m);
    public RNC RNCEmisor { get; init; } = new("00000000000");
    public AlfaNum11 eNCF { get; init; } = new("");
    public FechaDominicana FechaEmision { get; init; } = new(DateOnly.Parse("2026-09-15"));
    public DecimalMayorIgualCero MontoTotal { get; init; } = new(0);
    public RNC RNCComprador { get; init; } = new("00000000000");
    public EstadoType Estado { get; init; } = EstadoType.Aceptado;
    public AlfaNum250? DetalleMotivoRechazo { get; init; }
    public DateTime FechaHoraAprobacionComercial;  // DateAndTimeValidation
}

public enum EstadoType : int
{
    Aceptado = 1,
    Rechazado = 2
}
```

---

## 5. Tipo Complejo ARECF (Acuse de Recibo)

```csharp
/// <summary>
/// Comprobante de acuse de recibo (ARECF).
/// Ver XSD: ARECF v1.0.xsd
/// </summary>
public record ARECF
{
    public DetalleAcuseDeRecibo DetalleAcusedeRecibo { get; init; } = new();
    public DateTime FechaHoraFirma { get; init; } = DateTime.Now;
}

public record DetalleAcuseDeRecibo
{
    public VersionType Version { get; init; } = new(1.0m);
    public RNC RNCEmisor { get; init; } = new("00000000000");
    public RNC RNCComprador { get; init; } = new("00000000000");
    public eNCFType eNCF { get; init; } = new("B0000000000001");
    public EstadoAcuseType Estado { get; init; } = EstadoAcuseType.Recibido;
    public CodigoMotivoNoRecibidoType? CodigoMotivoNoRecibido { get; init; }
    public DateTime FechaHoraAcuseRecibo;  // DateAndTimeType: dd-MM-yyyy HH:mm:ss
}

public enum EstadoAcuseType : int
{
    Recibido = 0,
    NoRecibido = 1
}

public enum CodigoMotivoNoRecibidoType : int
{
    ErrorEspecificacion = 1,
    ErrorFirmaDigital = 2,
    EnvioDuplicado = 3,
    RNCCompradornoCorresponde = 4
}
```

---

## 6. Interfaces para los Servicios de Facturación Electrónica

### 6.1 IElectronicInvoiceService

```csharp
public interface IElectronicInvoiceService
{
    /// <summary>
    /// Emite una factura electrónica y la envía a DGII.
    /// </summary>
    Task<ElectronicInvoiceResult> SubmitAsync(
        ElectronicInvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anula uno o más NCFs en un rango especificado.
    /// </summary>
    Task<AnulacionResult> AnularAsync(
        AnulacionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta el estado de una factura electrónica por su eNCF.
    /// </summary>
    Task<ElectronicInvoiceStatus> ConsultarEstadoAsync(
        string eNCF,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera el XML de una factura sin enviar (para contingencia o local).
    /// </summary>
    Task<string> GenerateXmlAsync(
        ElectronicInvoiceRequest request,
        CancellationToken cancellationToken = default);
}

public record ElectronicInvoiceRequest
{
    public int SaleId { get; init; }
    public TipoeCFType TipoeCF { get; init; } = TipoeCFType.FacturaConsumo;
    public string? eNCF { get; init; }
    public EmisorRequest Emisor { get; init; } = new();
    public CompradorRequest Comprador { get; init; } = new();
    public TipoPagoType TipoPago { get; init; } = TipoPagoType.Contado;
    public TipoIngresosType TipoIngresos { get; init; } = TipoIngresosType.IngresosOperaciones;
    public DateTime? FechaLimitePago { get; init; }
    public string? TerminoPago { get; init; }
    public TipoCuentaPagoType? TipoCuentaPago { get; init; }
    public string? NumeroCuentaPago { get; init; }
    public string? BancoPago { get; init; }
    public List<FormaDePagoRequest>? FormasDePago { get; init; }
    public IndicadorMontoGravadoType IndicadorMontoGravado { get; init; } = IndicadorMontoGravadoType.NoIncluyeITBIS;
    public IndicadorEnvioDiferidoType? IndicadorEnvioDiferido { get; init; }
    public IndicadorServicioTodoIncluidoType? IndicadorServicioTodoIncluido { get; init; }
    public List<InvoiceLineRequest> Lineas { get; init; } = new();
    public List<InvoiceSubtotalRequest> Subtotales { get; init; } = new();
    public List<InvoiceDiscountRequest> DescuentosORecargos { get; init; } = new();
    public List<InvoicePageRequest> Paginas { get; init; } = new();
    public InvoiceReferenceRequest? InformacionReferencia { get; init; }
    public InvoiceOtherCurrencyRequest? OtraMoneda { get; init; }
}

public record EmisorRequest
{
    public string RNCEmisor { get; init; } = "";
    public string RazonSocial { get; init; } = "";
    public string? NombreComercial { get; init; }
    public string? Sucursal { get; init; }
    public string Direccion { get; init; } = "";
    public string? Municipio { get; init; }
    public string? Provincia { get; init; }
    public List<string>? Telefonos { get; init; }
    public string? Correo { get; init; }
    public string? WebSite { get; init; }
    public string? ActividadEconomica { get; init; }
    public string? CodigoVendedor { get; init; }
    public string? NumeroFacturaInterna { get; init; }
    public int? NumeroPedidoInterno { get; init; }
    public string? ZonaVenta { get; init; }
    public string? RutaVenta { get; init; }
    public string? InformacionAdicional { get; init; }
    public DateTime FechaEmision { get; init; } = DateTime.Today;
}

public record CompradorRequest
{
    public string? RNC { get; init; }
    public string? IdentificadorExtranjero { get; init; }
    public string? RazonSocial { get; init; }
    public string? Contacto { get; init; }
    public string? Correo { get; init; }
    public string? Direccion { get; init; }
    public string? Municipio { get; init; }
    public string? Provincia { get; init; }
    public DateTime? FechaEntrega { get; init; }
    public string? ContactoEntrega { get; init; }
    public string? DireccionEntrega { get; init; }
    public string? TelefonoAdicional { get; init; }
    public DateTime? FechaOrdenCompra { get; init; }
    public string? NumeroOrdenCompra { get; init; }
    public string? CodigoInterno { get; init; }
    public string? ResponsablePago { get; init; }
    public string? InformacionAdicional { get; init; }
}

public record FormaDePagoRequest
{
    public FormaPagoType Tipo { get; init; }
    public decimal Monto { get; init; }
}

public record InvoiceLineRequest
{
    public int NumeroLinea { get; init; }
    public string Nombre { get; init; } = "";
    public string? Descripcion { get; init; }
    public IndicadorFacturacionType IndicadorFacturacion { get; init; }
    public IndicadorBienoServicioType IndicadorBienoServicio { get; init; }
    public decimal Cantidad { get; init; }
    public int? UnidadMedida { get; init; }
    public decimal? CantidadReferencia { get; init; }
    public int? UnidadReferencia { get; init; }
    public decimal PrecioUnitario { get; init; }
    public decimal? DescuentoMonto { get; init; }
    public decimal? RecargoMonto { get; init; }
    public List<InvoiceCodeRequest> Codigos { get; init; } = new();
    public List<InvoiceSubDiscountRequest> SubDescuentos { get; init; } = new();
    public List<InvoiceSubRecargoRequest> SubRecargos { get; init; } = new();
    public decimal Monto { get; init; }
}

public record InvoiceCodeRequest
{
    public string TipoCodigo { get; init; } = "";
    public string Codigo { get; init; } = "";
}

public record InvoiceSubDiscountRequest
{
    public TipoDescuentoRecargoType Tipo { get; init; }
    public decimal? Porcentaje { get; init; }
    public decimal? Monto { get; init; }
}

public record InvoiceSubRecargoRequest
{
    public TipoDescuentoRecargoType Tipo { get; init; }
    public decimal? Porcentaje { get; init; }
    public decimal? Monto { get; init; }
}

public record InvoiceSubtotalRequest
{
    public int? NumeroSubTotal { get; init; }
    public string? Descripcion { get; init; }
    public int? Orden { get; init; }
    public decimal? MontoGravadoTotal { get; init; }
    public decimal? MontoGravadoI1 { get; init; }
    public decimal? MontoGravadoI2 { get; init; }
    public decimal? MontoGravadoI3 { get; init; }
    public decimal? ITBIS { get; init; }
    public decimal? ITBIS1 { get; init; }
    public decimal? ITBIS2 { get; init; }
    public decimal? ITBIS3 { get; init; }
    public decimal? ImpuestoAdicional { get; init; }
    public decimal? Exento { get; init; }
    public decimal? Monto { get; init; }
    public int? Lineas { get; init; }
}

public record InvoiceDiscountRequest
{
    public int NumeroLinea { get; init; }
    public TipoAjusteType TipoAjuste { get; init; }
    public IndicadorNorma1007Type? IndicadorNorma1007 { get; init; }
    public string? Descripcion { get; init; }
    public TipoDescuentoRecargoType? TipoValor { get; init; }
    public decimal? Valor { get; init; }
    public decimal? Monto { get; init; }
    public decimal? MontoOtraMoneda { get; init; }
    public IndicadorFacturacionDRType? IndicadorFacturacion { get; init; }
}

public record InvoicePageRequest
{
    public int? PaginaNo { get; init; }
    public int? NoLineaDesde { get; init; }
    public int? NoLineaHasta { get; init; }
    public decimal? MontoGravado { get; init; }
    public decimal? MontoGravado1 { get; init; }
    public decimal? MontoGravado2 { get; init; }
    public decimal? MontoGravado3 { get; init; }
    public decimal? Exento { get; init; }
    public decimal? ITBIS { get; init; }
    public decimal? ITBIS1 { get; init; }
    public decimal? ITBIS2 { get; init; }
    public decimal? ITBIS3 { get; init; }
    public decimal? ImpuestoAdicional { get; init; }
    public decimal? MontoSubtotal { get; init; }
    public decimal? MontoNoFacturable { get; init; }
}

public record InvoiceReferenceRequest
{
    public string? NCFModificado { get; init; }
    public string? RNCOtroContribuyente { get; init; }
    public DateTime? FechaNCFModificado { get; init; }
    public CodigoModificacionType? CodigoModificacion { get; init; }
}

public record InvoiceOtherCurrencyRequest
{
    public TipoMoneda? TipoMoneda { get; init; }
    public decimal? TipoCambio { get; init; }
    public decimal? MontoGravadoTotal { get; init; }
    public decimal? MontoGravado1 { get; init; }
    public decimal? MontoGravado2 { get; init; }
    public decimal? MontoGravado3 { get; init; }
    public decimal? MontoExento { get; init; }
    public decimal? TotalITBIS { get; init; }
    public decimal? TotalITBIS1 { get; init; }
    public decimal? TotalITBIS2 { get; init; }
    public decimal? TotalITBIS3 { get; init; }
    public decimal? MontoImpuestoAdicional { get; init; }
    public List<InvoiceOtherCurrencyTaxRequest> ImpuestosAdicionales { get; init; } = new();
    public decimal? MontoTotal { get; init; }
}

public record InvoiceOtherCurrencyTaxRequest
{
    public CodificacionTipoImpuestos TipoImpuesto { get; init; }
    public decimal Tasa { get; init; }
    public decimal? MontoSpecifico { get; init; }
    public decimal? MontoAdValorem { get; init; }
    public decimal? OtrosImpuestos { get; init; }
}

public record ElectronicInvoiceResult
{
    public bool Success { get; init; }
    public string eNCF { get; init; } = "";
    public string XML { get; init; } = "";
    public string XMLHash { get; init; } = "";
    public ElectronicInvoiceStatus Estado { get; init; } = ElectronicInvoiceStatus.Creado;
    public string? MotivoRechazo { get; init; }
    public DateTime? FechaAprobacion { get; init; }
    public DateTime FechaEnvio { get; init; } = DateTime.Now;
    public string? ARECFXML { get; init; }
    public string? ACECFXML { get; init; }
    public string? Error { get; init; }
}

public enum ElectronicInvoiceStatus
{
    Creado = 0,
    Enviado = 1,
    Aprobado = 2,
    Rechazado = 3,
    Anulado = 4,
    NoRecibido = 5
}

public record AnulacionRequest
{
    public string RNCEmisor { get; init; } = "";
    public DateTime FechaHoraAnulacion { get; init; } = DateTime.Now;
    public List<RangoAnulacionRequest> Anulaciones { get; init; } = new();
}

public record RangoAnulacionRequest
{
    public int NoLinea { get; init; }
    public CFType TipoeCF { get; init; }
    public List<RangoSecuenciaRequest> Secuencias { get; init; } = new();
    public int CantidadNCFAnulados { get; init; }
}

public record RangoSecuenciaRequest
{
    public string Desde { get; init; } = "";
    public string Hasta { get; init; } = "";
}

public record AnulacionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<string> eNCFsAnulados { get; init; } = new();
}

public record ElectronicInvoiceStatus
{
    public string eNCF { get; init; } = "";
    public ElectronicInvoiceStatus Estado { get; init; }
    public DateTime? FechaEmision { get; init; }
    public DateTime? FechaEnvio { get; init; }
    public DateTime? FechaAprobacion { get; init; }
    public string? MotivoRechazo { get; init; }
}
```

### 6.2 IInvoiceRepository

```csharp
public interface IInvoiceRepository
{
    Task<ElectronicInvoiceEntity?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ElectronicInvoiceEntity?> GetByENCFAsync(string enf, CancellationToken ct = default);
    Task<ElectronicInvoiceEntity> CreateAsync(ElectronicInvoiceEntity invoice, CancellationToken ct = default);
    Task<IEnumerable<ElectronicInvoiceEntity>> GetByRNCAsync(string rnc, int? tipoeCF = null, CancellationToken ct = default);
    Task<IEnumerable<ElectronicInvoiceEntity>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IEnumerable<ElectronicInvoiceEntity>> GetPendingAsync(CancellationToken ct = default);
    Task UpdateEstadoAsync(int id, ElectronicInvoiceStatus nuevoEstado, CancellationToken ct = default);
    Task<bool> ExistsENCFAsync(string enf, CancellationToken ct = default);
}
```

### 6.3 IXmlValidator

```csharp
public interface IXmlValidator
{
    Task<XmlValidationResult> ValidateAsync(string xml, string xsdPath, CancellationToken ct = default);
}

public record XmlValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}
```

### 6.4 IHashGenerator

```csharp
public interface IHashGenerator
{
    string Generate(string xmlContent);
}
```

### 6.5 IXmlSerializer

```csharp
public interface IXmlSerializer
{
    Task<string> SerializeAsync(ElectronicInvoiceRequest request, string xsdType, CancellationToken ct = default);
    Task<string> SerializeAnulacionAsync(AnulacionRequest request, CancellationToken ct = default);
    Task<string> SerializeAcuseReciboAsync(ARECF acuse, CancellationToken ct = default);
    Task<string> SerializeAprobacionComercialAsync(ACECF aprobacion, CancellationToken ct = default);
}
```

---

## 7. Enumeraciones de Referencia Rápida

### 7.1 Tipos de Comprobante (e-CF)

| Valor | Descripción |
|-------|-------------|
| 31 | Factura de Crédito Fiscal Electrónica |
| 32 | Factura de Consumo Electrónica |
| 33 | Nota de Débito Electrónica |
| 34 | Nota de Crédito Electrónica |
| 41 | Compras Electrónico |
| 43 | Gastos Menores Electrónico |
| 44 | Regímenes Especiales Electrónico |
| 45 | Gubernamental Electrónico |
| 46 | Comprobante de Exportaciones Electrónico |
| 47 | Comprobante para Pagos al Exterior Electrónico |

### 7.2 Indicadores de Facturación (Items)

| Valor | Descripción | ITBIS |
|-------|-------------|-------|
| 0 | No Facturable (18%) | 18% sobre margen |
| 1 | ITBIS 1 (18%) | 18% |
| 2 | ITBIS 2 (16%) | 16% |
| 3 | ITBIS 3 (0%) | 0% (gravado) |
| 4 | Exento (E) | 0% |

### 7.3 Tipos de Pago

| Valor | Descripción |
|-------|-------------|
| 1 | Contado |
| 2 | Crédito |
| 3 | Gratuito |

### 7.4 Formas de Pago

| Valor | Descripción |
|-------|-------------|
| 1 | Efectivo |
| 2 | Cheque/Transferencia/Depósito |
| 3 | Tarjeta de Débito/Crédito |
| 4 | Venta a Crédito |
| 5 | Bonos o Certificados de regalo |
| 6 | Permuta |
| 7 | Nota de crédito |
| 8 | Otras Formas de pago |

### 7.5 Tipos de Ingresos

| Valor | Descripción |
|-------|-------------|
| 01 | Ingresos por operaciones (No financieros) |
| 02 | Ingresos Financieros |
| 03 | Ingresos Extraordinarios |
| 04 | Ingresos por Arrendamientos |
| 05 | Ingresos por Venta de Activo Depreciable |
| 06 | Otros Ingresos |

### 7.6 Impuestos Adicionales (ISC)

| Código | Descripción |
|---------|-------------|
| 001 | Propina Legal |
| 002 | Contribución al Desarrollo de las Telecomunicaciones |
| 003 | ISC: Servicios Seguros en general |
| 004 | ISC: Servicios de Telecomunicaciones |
| 005 | ISC: Expedición de la primera placa |
| 006-010 | ISC (Especifico): Bebidas alcohólicas |
| 011-018 | ISC (Especifico): Licores y aguardientes |
| 019-022 | ISC (Especifico): Cigarrillos |
| 023-039 | ISC (AdValorem): Bebidas y cigarillos |

### 7.7 Unidades de Medida (Resumen)

| Código | Descripción | Código | Descripción |
|---------|-------------|---------|-------------|
| 6 | Caja/Cajón (CAJ) | 34 | Pieza (PZA) |
| 7 | Cajetilla (CAJET) | 43 | Unidad (UND) |
| 8 | Centímetro (CM) | 45 | Millar (ML) |
| 12 | Día (DÍA) | 55 | Pulgadas (PULG) |
| 13 | Docena (DOC) | 21 | Kilogramo (KG) |
| 17 | Gramo (GR) | 24 | Litro (LT) |
| 19 | Hora (HOR) | 26 | Metro (M) |
| 23 | Libra (LB) | 27 | Metro cuadrado (M2) |
| 28 | Metro cúbico (M3) | 30 | Minuto (MIN) |

---

## 8. Validaciones

### 8.1 RNC

- Formato: `^\d{9}$` o `^\d{11}$`
- 9 dígitos: Persona física
- 11 dígitos: Empresa

### 8.2 eNCF

- Formato: `^[a-zA-Z0-9]{13}$`
- 13 caracteres alfanuméricos
- Ejemplo: `B0000000000001`

### 8.3 Fecha

- Formato: `dd-MM-yyyy`
- Patrón: días 01-31, meses 01-12, año 1900-2099

### 8.4 Montos

- Decimal con 2 decimales
- Máximo 18 dígitos totales
- Positivo o cero (≥ 0)
- Algunos montos pueden ser negativos (ej: MontoNoFacturable, MontoPeriodo)

### 8.5 Teléfonos

- Formato: `^\d{3}-\d{3}-\d{4}$`
- Ejemplo: `809-555-1234`

### 8.6 Correos Electrónicos

- Formato: `\w+([-.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*`
- Máximo 80 caracteres

### 8.7 Tipos de Moneda

- BRL, CAD, CHF, CHY, XDR, DKK, EUR, GBP, JPY, NOK, SCP, SEK, USD, VEF, HTG, MXN, COP

---

## 9. Endpoint DGII (Pendiente de confirmar)

La integración con DGII se realiza mediante servicios web SOAP. Los endpoints típicos son:

- **Facturación electrónica:** `https://www.dgii.gov.do/serviciosonline/facturacion/Paginas/facturacion.aspx` o servicio SOAP
- **Consulta de estado:** POST al endpoint de validación
- **Anulación:** POST al endpoint de anulación

**Nota:** Los endpoints exactos y la estructura SOAP deben confirmarse con la documentación oficial de la DGII.

---

## 10. Ejemplo de XML Generado (e-CF 32)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ECF xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Encabezado>
    <Version>1.0</Version>
    <IdDoc>
      <TipoeCF>32</TipoeCF>
      <eNCF>B0000000000001</eNCF>
      <IndicadorEnvioDiferido>0</IndicadorEnvioDiferido>
      <IndicadorMontoGravado>0</IndicadorMontoGravado>
      <IndicadorServicioTodoIncluido>0</IndicadorServicioTodoIncluido>
      <TipoIngresos>01</TipoIngresos>
      <TipoPago>1</TipoPago>
      <FechaLimitePago/>
      <TablaFormasPago>
        <FormaDePago>
          <FormaPago>1</FormaPago>
          <MontoPago>15000.00</MontoPago>
        </FormaDePago>
      </TablaFormasPago>
      <FechaDesde/>
      <FechaHasta/>
      <TotalPaginas/>
    </IdDoc>
    <Emisor>
      <RNCEmisor>00000000000</RNCEmisor>
      <RazonSocialEmisor>EMPRESA EMISORA S.A.</RazonSocialEmisor>
      <NombreComercial/>
      <Sucursal/>
      <DireccionEmisor>Calle Principal #123</DireccionEmisor>
      <Municipio>010100</Municipio>
      <Provincia>010000</Provincia>
      <TablaTelefonoEmisor/>
      <CorreoEmisor/>
      <WebSite/>
      <ActividadEconomica/>
      <CodigoVendedor/>
      <NumeroFacturaInterna/>
      <NumeroPedidoInterno/>
      <ZonaVenta/>
      <RutaVenta/>
      <InformacionAdicionalEmisor/>
      <FechaEmision>15-09-2026</FechaEmision>
    </Emisor>
    <Comprador>
      <RNCComprador/>
      <RazonSocialComprador>Juan Pérez</RazonSocialComprador>
      <ContactoComprador/>
      <CorreoComprador/>
      <DireccionComprador/>
      <MunicipioComprador/>
      <ProvinciaComprador/>
      <FechaEntrega/>
      <ContactoEntrega/>
      <DireccionEntrega/>
      <TelefonoAdicional/>
      <FechaOrdenCompra/>
      <NumeroOrdenCompra/>
      <CodigoInternoComprador/>
      <ResponsablePago/>
      <InformacionAdicionalComprador/>
    </Comprador>
    <InformacionesAdicionales/>
    <Transporte/>
    <Totales>
      <MontoGravadoTotal>15000.00</MontoGravadoTotal>
      <MontoGravadoI1>15000.00</MontoGravadoI1>
      <MontoGravadoI2/>
      <MontoGravadoI3/>
      <MontoExento/>
      <ITBIS1>18</ITBIS1>
      <ITBIS2/>
      <ITBIS3/>
      <TotalITBIS>2700.00</TotalITBIS>
      <TotalITBIS1>2700.00</TotalITBIS1>
      <TotalITBIS2/>
      <TotalITBIS3/>
      <MontoImpuestoAdicional/>
      <ImpuestosAdicionales/>
      <MontoTotal>17700.00</MontoTotal>
    </Totales>
    <OtraMoneda/>
  </Encabezado>
  <DetallesItems>
    <Item>
      <NumeroLinea>1</NumeroLinea>
      <TablaCodigosItem/>
      <IndicadorFacturacion>1</IndicadorFacturacion>
      <IndicadorBienoServicio>1</IndicadorBienoServicio>
      <NombreItem>Producto de Prueba</NombreItem>
      <DescripcionItem/>
      <CantidadItem>2.00</CantidadItem>
      <UnidadMedida>34</UnidadMedida>
      <CantidadReferencia/>
      <UnidadReferencia/>
      <TablaSubcantidad/>
      <GradosAlcohol/>
      <FechaElaboracion/>
      <FechaVencimientoItem/>
      <Mineria/>
      <PrecioUnitarioItem>7500.0000</PrecioUnitarioItem>
      <DescuentoMonto/>
      <TablaSubDescuento/>
      <RecargoMonto/>
      <TablaSubRecargo/>
      <TablaImpuestoAdicional/>
      <OtraMonedaDetalle/>
      <MontoItem>15000.00</MontoItem>
    </Item>
  </DetallesItems>
  <Subtotales/>
  <DescuentosORecargos/>
  <Paginacion/>
  <InformacionReferencia/>
  <FechaHoraFirma>15-09-2026 14:30:00</FechaHoraFirma>
</ECF>
```

---

## 11. Flujos de Trabajo

### 11.1 Flujo de Emisión de Factura (Normal)

```
1. Usuario finaliza venta en POS
2. System calcula totales e ITBIS
3. Se crea ElectronicInvoiceRequest
4. Se valida contra reglas de negocio
5. Se serializa a XML con e-CF 32 XSD
6. Se valida XML contra XSD
7. Se calcula hash de seguridad (6 chars)
8. SE firma digitalmente (pendiente certificado)
9. SE envía a DGII vía REST API JSON
10. SE recibe TrackId de DGII
11. SE polling para consultar resultado
12. SE recibe ARECF (acuse inmediato)
13. SE persiste en BD
14. SE imprime factura
13. SE imprime factura
```

### 11.2 Flujo de Contingencia

```
1. DGII no disponible
2. SE genera XML con RFCE 32 XSD
3. SE guarda XML localmente
4. SE marca factura como "Pendiente"
5. SE reintenta envío automático cada N minutos
6. Al éxito, se marca como "Enviado"
```

### 11.3 Flujo de Anulación

```
1. Usuario solicita anular factura
2. Se crea ANECF con rango de eNCFs
3. SE serializa XML de anulación
4. SE valida contra XSD ANECF
5. SE envía a DGII
6. SE recibe confirmación
7. SE marca factura(s) como "Anulado"
```
