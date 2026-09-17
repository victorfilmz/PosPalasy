using System;
using System.Text.RegularExpressions;

namespace POS.Domain.Types;

/// <summary>
/// ValueObject inmutable para eNCF (Número de Comprobante Fiscal Electrónico).
/// 13 caracteres alfanuméricos: [A-Za-z0-9]{13} (Serie + 12 dígitos de secuencia).
/// Ejemplo: E310000000001, B000000000001
/// </summary>
public sealed class eNCF : IEquatable<eNCF>
{
    public string Value { get; }

    public eNCF(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("eNCF no puede estar vacío.", nameof(value));

        var normalized = value.Trim().ToUpperInvariant();

        if (!Regex.IsMatch(normalized, "^[A-Z0-9]{13}$"))
        {
            throw new ArgumentException(
                $"eNCF inválido: '{normalized}'. Debe tener exactamente 13 caracteres alfanuméricos.",
                nameof(value));
        }

        Value = normalized;
    }

    /// <summary>
    /// Serie del eNCF (primer caracter).
    /// Ejemplo: E310000000001 → "E"
    /// </summary>
    public string Serie => Value[..1];

    /// <summary>
    /// Secuencia del eNCF (los 12 caracteres restantes).
    /// </summary>
    public string Secuencial => Value[1..];

    /// <summary>
    /// Genera un nuevo eNCF secuencial.
    /// </summary>
    public static eNCF Generate(string serie = "E", long secuencia = 1)
    {
        if (string.IsNullOrWhiteSpace(serie))
            throw new ArgumentException("La serie no puede estar vacía.", nameof(serie));

        var secStr = secuencia.ToString("D12");
        var value = $"{serie.Trim().ToUpperInvariant()[0]}{secStr}";
        return new eNCF(value);
    }

    public override string ToString() => Value;

    public static implicit operator string(eNCF enf) => enf.Value;
    public static explicit operator eNCF(string s) => new(s);

    public static bool IsValid(string enf)
    {
        if (string.IsNullOrWhiteSpace(enf)) return false;
        return Regex.IsMatch(enf.Trim().ToUpperInvariant(), "^[A-Z0-9]{13}$");
    }

    public static bool EsValido(string enf) => IsValid(enf);

    public bool Equals(eNCF? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value;
    }

    public override bool Equals(object? obj) =>
        obj is eNCF other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public static bool operator ==(eNCF? left, eNCF? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(eNCF? left, eNCF? right) => !(left == right);
}

/// <summary>
/// Helper para generación de secuencias de eNCF.
/// </summary>
public static class eNCFSecuencia
{
    /// <summary>
    /// Incrementa la secuencia de un eNCF existente.
    /// </summary>
    public static string Incrementar(string? ultimoENCF)
    {
        var serie = "E";
        var sec = "000000000000";

        if (!string.IsNullOrEmpty(ultimoENCF) && ultimoENCF.Length >= 13)
        {
            serie = ultimoENCF[..1].ToUpperInvariant();
            sec = ultimoENCF[1..];

            if (long.TryParse(sec, out var num))
            {
                sec = (num + 1).ToString("D12");
            }
        }

        return $"{serie}{sec}";
    }
}
