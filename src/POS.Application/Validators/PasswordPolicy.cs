using System;
using System.Linq;

namespace POS.Application.Validators;

/// <summary>
/// Política de contraseñas del sistema POS. Se aplica tanto al alta de usuarios como al cambio de contraseña,
/// para que ninguna cuenta pueda quedar con una contraseña débil (incluidas las cuentas sembradas).
/// </summary>
public static class PasswordPolicy
{
    /// <summary>Longitud mínima exigida a una contraseña.</summary>
    public const int LongitudMinima = 12;

    /// <summary>Longitud máxima aceptada (limita abuso de CPU en el hashing).</summary>
    public const int LongitudMaxima = 128;

    /// <summary>
    /// Valida una contraseña contra la política: longitud, variedad de caracteres,
    /// sin espacios en blanco y distinta del nombre de usuario.
    /// </summary>
    public static ValidationResult Validar(string? password, string? nombreUsuario = null)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(password))
        {
            result.AgregarError("La contraseña es obligatoria.");
            return result;
        }

        if (password.Length < LongitudMinima)
            result.AgregarError($"La contraseña debe tener al menos {LongitudMinima} caracteres.");

        if (password.Length > LongitudMaxima)
            result.AgregarError($"La contraseña no puede superar {LongitudMaxima} caracteres.");

        if (password.Any(char.IsWhiteSpace))
            result.AgregarError("La contraseña no puede contener espacios en blanco.");

        if (!password.Any(char.IsUpper))
            result.AgregarError("La contraseña debe incluir al menos una letra mayúscula.");

        if (!password.Any(char.IsLower))
            result.AgregarError("La contraseña debe incluir al menos una letra minúscula.");

        if (!password.Any(char.IsDigit))
            result.AgregarError("La contraseña debe incluir al menos un dígito.");

        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            result.AgregarError("La contraseña debe incluir al menos un carácter especial.");

        if (!string.IsNullOrWhiteSpace(nombreUsuario) &&
            password.Contains(nombreUsuario.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            result.AgregarError("La contraseña no puede contener el nombre de usuario.");
        }

        return result;
    }

    /// <summary>
    /// Genera una contraseña aleatoria criptográficamente segura que cumple la política.
    /// Se usa para la cuenta inicial cuando el operador no define una por configuración.
    /// </summary>
    public static string GenerarAleatoria()
    {
        const string mayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string minusculas = "abcdefghijkmnopqrstuvwxyz";
        const string digitos = "23456789";
        const string especiales = "!@#$%&*?-_=+";
        const string todos = mayusculas + minusculas + digitos + especiales;

        Span<char> buffer = stackalloc char[20];

        buffer[0] = Elegir(mayusculas);
        buffer[1] = Elegir(minusculas);
        buffer[2] = Elegir(digitos);
        buffer[3] = Elegir(especiales);

        for (int i = 4; i < buffer.Length; i++)
            buffer[i] = Elegir(todos);

        // Mezcla Fisher-Yates para que la posición de cada clase de carácter no sea predecible.
        for (int i = buffer.Length - 1; i > 0; i--)
        {
            var k = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
            (buffer[i], buffer[k]) = (buffer[k], buffer[i]);
        }

        return new string(buffer);
    }

    private static char Elegir(string conjunto) =>
        conjunto[System.Security.Cryptography.RandomNumberGenerator.GetInt32(conjunto.Length)];
}
