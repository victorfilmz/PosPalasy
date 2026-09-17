using System;
using System.Text.RegularExpressions;

namespace POS.Domain.Types;

/// <summary>
/// ValueObject inmutable para RNC (Registro Nacional de Contribuyentes) de República Dominicana.
/// Formato: 9 dígitos (persona física) o 11 dígitos (empresa/persona jurídica).
/// </summary>
public sealed class RNC : IEquatable<RNC>
{
    public string Value { get; }

    public RNC(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("RNC no puede estar vacío.", nameof(value));

        var normalized = value.Trim();

        if (!Regex.IsMatch(normalized, "^[0-9]{9}$") &&
            !Regex.IsMatch(normalized, "^[0-9]{11}$"))
        {
            throw new ArgumentException(
                $"RNC inválido: '{normalized}'. Debe tener 9 o 11 dígitos numéricos.",
                nameof(value));
        }

        Value = normalized;
    }

    public bool IsEmpresa => Value.Length == 11;
    public bool IsPersonaFisica => Value.Length == 9;
    public bool EsEmpresa => IsEmpresa;
    public bool EsPersonaFisica => IsPersonaFisica;

    public override string ToString() => Value;

    public static implicit operator string(RNC rnc) => rnc.Value;
    public static explicit operator RNC(string s) => new(s);

    public static bool IsValid(string rnc)
    {
        if (string.IsNullOrWhiteSpace(rnc)) return false;
        var trimmed = rnc.Trim();
        return Regex.IsMatch(trimmed, "^[0-9]{9}$") || Regex.IsMatch(trimmed, "^[0-9]{11}$");
    }

    public bool Equals(RNC? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value;
    }

    public override bool Equals(object? obj) =>
        obj is RNC other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public static bool operator ==(RNC? left, RNC? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(RNC? left, RNC? right) => !(left == right);
}
