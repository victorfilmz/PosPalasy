using System;
using Microsoft.AspNetCore.Identity;
using POS.Application.Interfaces;
using POS.Domain.Entities;

namespace POS.Infrastructure.Security;

/// <summary>
/// Hashing de contraseñas basado en <see cref="PasswordHasher{TUser}"/> de ASP.NET Core Identity
/// (PBKDF2-HMAC-SHA256 con sal aleatoria y comparación en tiempo constante).
/// Se usa la implementación oficial en lugar de una propia para no introducir criptografía artesanal,
/// y se delega en el framework la decisión de "requiere rehash" para no acoplarse al formato interno
/// del hash (que cambia entre versiones).
/// </summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<Usuario> _inner = new();

    // Instancia señuelo: PasswordHasher exige un TUser aunque no use sus datos.
    private static readonly Usuario Marcador = new() { NombreUsuario = "sistema" };

    public string Hash(string passwordEnClaro)
    {
        if (string.IsNullOrEmpty(passwordEnClaro))
            throw new ArgumentException("La contraseña a hashear no puede estar vacía.", nameof(passwordEnClaro));

        return _inner.HashPassword(Marcador, passwordEnClaro);
    }

    public ResultadoVerificacionPassword Verify(string? passwordHash, string passwordEnClaro)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrEmpty(passwordEnClaro))
            return ResultadoVerificacionPassword.Fallida;

        try
        {
            return _inner.VerifyHashedPassword(Marcador, passwordHash, passwordEnClaro) switch
            {
                PasswordVerificationResult.Success => ResultadoVerificacionPassword.Correcta,
                PasswordVerificationResult.SuccessRehashNeeded => ResultadoVerificacionPassword.CorrectaRequiereRehash,
                _ => ResultadoVerificacionPassword.Fallida
            };
        }
        catch (FormatException)
        {
            // Hash almacenado con formato inválido o corrupto: se trata como credencial inválida.
            return ResultadoVerificacionPassword.Fallida;
        }
        catch (ArgumentException)
        {
            return ResultadoVerificacionPassword.Fallida;
        }
    }
}
