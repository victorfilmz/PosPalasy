using System;

namespace POS.Domain.Types;

/// <summary>
/// ValueObject inmutable para fechas en formato dominicano (DD-MM-AAAA).
/// </summary>
public sealed class FechaDominicana : IEquatable<FechaDominicana>
{
    public DateOnly Value { get; }

    public FechaDominicana(DateOnly value) => Value = value;
    public FechaDominicana(DateTime date) => Value = DateOnly.FromDateTime(date);

    /// <summary>
    /// Parsea desde string con formato DD-MM-AAAA.
    /// </summary>
    public static FechaDominicana Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Fecha no puede estar vacía.", nameof(s));

        var parts = s.Trim().Split('-');
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
    /// Formatea como DD-MM-AAAA para XML y representaciones DGII.
    /// </summary>
    public string ToXmlString() => Value.ToString("dd-MM-yyyy");

    public override string ToString() => ToXmlString();

    public static implicit operator DateOnly(FechaDominicana f) => f.Value;
    public static explicit operator FechaDominicana(DateOnly d) => new(d);
    public static implicit operator FechaDominicana(DateTime dt) => new(dt);
    public static explicit operator FechaDominicana(string s) => Parse(s);

    public bool Equals(FechaDominicana? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj) =>
        obj is FechaDominicana other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(FechaDominicana? left, FechaDominicana? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(FechaDominicana? left, FechaDominicana? right) => !(left == right);

    public static FechaDominicana Today => new(DateOnly.FromDateTime(DateTime.Today));
}
