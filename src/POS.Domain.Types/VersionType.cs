namespace POS.Domain.Types;

/// <summary>
/// Versión del formato e-CF. Según DGII actualmente siempre es 1.0.
/// </summary>
public record VersionType
{
    public decimal Value { get; init; } = 1.0m;

    public VersionType() { }
    public VersionType(decimal value) => Value = value;
    public VersionType(string value)
    {
        if (decimal.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var d))
            Value = d;
        else
            Value = 1.0m;
    }

    public override string ToString() => Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    public static implicit operator decimal(VersionType v) => v.Value;
    public static implicit operator string(VersionType v) => v.ToString();
    public static explicit operator VersionType(decimal d) => new(d);
    public static explicit operator VersionType(string s) => new(s);
}

/// <summary>
/// Versión RFCE (contingencia). Siempre 1.0.
/// </summary>
public record VersionRfcType
{
    public decimal Value { get; init; } = 1.0m;

    public VersionRfcType() { }
    public VersionRfcType(decimal value) => Value = value;
    public VersionRfcType(string value)
    {
        if (decimal.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var d))
            Value = d;
        else
            Value = 1.0m;
    }

    public override string ToString() => Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    public static implicit operator decimal(VersionRfcType v) => v.Value;
    public static implicit operator string(VersionRfcType v) => v.ToString();
    public static explicit operator VersionRfcType(decimal d) => new(d);
    public static explicit operator VersionRfcType(string s) => new(s);
}
