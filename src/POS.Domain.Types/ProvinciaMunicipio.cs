using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace POS.Domain.Types;

/// <summary>
/// ValueObject inmutable para códigos de Provincia y Municipio de la República Dominicana según DGII.
/// Formato: 6 dígitos numéricos.
/// PP0000: Representa la Provincia (los últimos 4 dígitos son 0).
/// PPM000: Representa el Municipio dentro de la Provincia.
/// </summary>
public sealed class ProvinciaMunicipio : IEquatable<ProvinciaMunicipio>
{
    public string Value { get; }

    public ProvinciaMunicipio(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("El código de provincia/municipio no puede estar vacío.", nameof(value));

        var normalized = value.Trim();

        if (!Regex.IsMatch(normalized, @"^\d{6}$"))
        {
            throw new ArgumentException(
                $"Código de provincia/municipio inválido: '{normalized}'. Debe tener exactamente 6 dígitos numéricos.",
                nameof(value));
        }

        Value = normalized;
    }

    /// <summary>
    /// Código de la provincia (los primeros 2 dígitos).
    /// </summary>
    public string CodigoProvincia => Value[..2];

    /// <summary>
    /// Código de municipio (los dígitos 3 y 4).
    /// </summary>
    public string CodigoMunicipio => Value.Substring(2, 2);

    /// <summary>
    /// Indica si el código representa a nivel provincial (termina en 0000).
    /// </summary>
    public bool EsProvincia => Value.EndsWith("0000");

    /// <summary>
    /// Indica si el código representa un municipio específico.
    /// </summary>
    public bool EsMunicipio => !EsProvincia;

    public override string ToString() => Value;

    public static implicit operator string(ProvinciaMunicipio p) => p.Value;
    public static explicit operator ProvinciaMunicipio(string s) => new(s);

    public static bool IsValid(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return false;
        return Regex.IsMatch(codigo.Trim(), @"^\d{6}$");
    }

    public bool Equals(ProvinciaMunicipio? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value;
    }

    public override bool Equals(object? obj) =>
        obj is ProvinciaMunicipio other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public static bool operator ==(ProvinciaMunicipio? left, ProvinciaMunicipio? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(ProvinciaMunicipio? left, ProvinciaMunicipio? right) => !(left == right);
}

public static class ProvinciaMunicipioHelper
{
    private static readonly Dictionary<string, string> NombresProvincias = new()
    {
        { "01", "Distrito Nacional" },
        { "02", "Azua" },
        { "03", "Baoruco" },
        { "04", "Barahona" },
        { "05", "Dajabón" },
        { "06", "Duarte" },
        { "07", "Elías Piña" },
        { "08", "El Seibo" },
        { "09", "Espaillat" },
        { "10", "Independencia" },
        { "11", "La Altagracia" },
        { "12", "La Romana" },
        { "13", "La Vega" },
        { "14", "María Trinidad Sánchez" },
        { "15", "Monte Cristi" },
        { "16", "Pedernales" },
        { "17", "Peravia" },
        { "18", "Puerto Plata" },
        { "19", "Hermanas Mirabal" },
        { "20", "Samaná" },
        { "21", "San Cristóbal" },
        { "22", "San Juan" },
        { "23", "San Pedro de Macorís" },
        { "24", "Sánchez Ramírez" },
        { "25", "Santiago" },
        { "26", "Santiago Rodríguez" },
        { "27", "Valverde" },
        { "28", "Monseñor Nouel" },
        { "29", "Monte Plata" },
        { "30", "Hato Mayor" },
        { "31", "San José de Ocoa" },
        { "32", "Santo Domingo" }
    };

    public static string ObtenerNombreProvincia(ProvinciaMunicipio pm)
    {
        if (NombresProvincias.TryGetValue(pm.CodigoProvincia, out var nombre))
            return nombre;
        return $"Provincia {pm.CodigoProvincia}";
    }
}
