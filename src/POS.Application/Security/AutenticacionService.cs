using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using POS.Application.Interfaces;
using POS.Application.Validators;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Application.Security;

/// <summary>Motivo por el que un intento de autenticación no fue exitoso.</summary>
public enum MotivoFalloAutenticacion
{
    Ninguno = 0,
    CredencialesInvalidas = 1,
    UsuarioBloqueado = 2,
    UsuarioInactivo = 3
}

/// <summary>Resultado de un intento de inicio de sesión.</summary>
public class ResultadoAutenticacion
{
    public bool Exito { get; private init; }
    public MotivoFalloAutenticacion Motivo { get; private init; }
    public string Mensaje { get; private init; } = string.Empty;
    public Usuario? Usuario { get; private init; }

    public static ResultadoAutenticacion Ok(Usuario usuario) => new()
    {
        Exito = true,
        Motivo = MotivoFalloAutenticacion.Ninguno,
        Usuario = usuario
    };

    public static ResultadoAutenticacion Fallo(MotivoFalloAutenticacion motivo, string mensaje) => new()
    {
        Exito = false,
        Motivo = motivo,
        Mensaje = mensaje
    };
}

/// <summary>Resultado del cambio de contraseña propia.</summary>
public class ResultadoCambioPassword
{
    public bool Exito => Errores.Count == 0;
    public List<string> Errores { get; } = new();

    /// <summary>Usuario con la contraseña ya actualizada (permite renovar la sesión sin volver a consultar).</summary>
    public Usuario? Usuario { get; set; }

    public void AgregarError(string mensaje) => Errores.Add(mensaje);
}

/// <summary>
/// Caso de uso de autenticación y gestión de la contraseña propia.
/// Toda la lógica de decisión vive aquí y no en el controlador HTTP.
/// </summary>
public interface IAutenticacionService
{
    /// <summary>Valida credenciales, aplica bloqueo por intentos fallidos y registra el acceso.</summary>
    Task<ResultadoAutenticacion> AutenticarAsync(
        string? nombreUsuario,
        string? password,
        DateTime ahora,
        CancellationToken ct = default);

    /// <summary>Cambia la contraseña propia del usuario autenticado verificando la contraseña actual.</summary>
    Task<ResultadoCambioPassword> CambiarPasswordAsync(
        int usuarioId,
        string? passwordActual,
        string? passwordNueva,
        DateTime ahora,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IAutenticacionService" />
public class AutenticacionService : IAutenticacionService
{
    /// <summary>Mensaje único para credenciales inválidas: evita enumeración de usuarios.</summary>
    public const string MensajeCredencialesInvalidas = "Usuario o contraseña incorrectos.";

    private readonly IUsuarioRepository _usuarios;
    private readonly IPasswordHasher _passwordHasher;

    // Hash señuelo para igualar el coste de verificación cuando el usuario no existe (anti-enumeración por tiempo).
    private readonly Lazy<string> _hashSenuelo;

    public AutenticacionService(IUsuarioRepository usuarios, IPasswordHasher passwordHasher)
    {
        _usuarios = usuarios ?? throw new ArgumentNullException(nameof(usuarios));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _hashSenuelo = new Lazy<string>(() => _passwordHasher.Hash(Guid.NewGuid().ToString("N")));
    }

    public async Task<ResultadoAutenticacion> AutenticarAsync(
        string? nombreUsuario,
        string? password,
        DateTime ahora,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nombreUsuario) || string.IsNullOrEmpty(password))
            return ResultadoAutenticacion.Fallo(MotivoFalloAutenticacion.CredencialesInvalidas, MensajeCredencialesInvalidas);

        var usuario = await _usuarios.GetByNombreUsuarioAsync(nombreUsuario.Trim(), ct);

        if (usuario is null)
        {
            // Consume el mismo trabajo de hashing que un usuario real y falla con el mismo mensaje.
            _passwordHasher.Verify(_hashSenuelo.Value, password);
            return ResultadoAutenticacion.Fallo(MotivoFalloAutenticacion.CredencialesInvalidas, MensajeCredencialesInvalidas);
        }

        var verificacion = _passwordHasher.Verify(usuario.PasswordHash, password);

        if (!usuario.EstaActivo)
            return ResultadoAutenticacion.Fallo(MotivoFalloAutenticacion.UsuarioInactivo, "La cuenta está desactivada. Contacte al administrador.");

        if (usuario.EstaBloqueada(ahora))
        {
            return ResultadoAutenticacion.Fallo(
                MotivoFalloAutenticacion.UsuarioBloqueado,
                $"La cuenta está bloqueada temporalmente por intentos fallidos. Intente de nuevo después de las " +
                $"{usuario.BloqueadoHasta!.Value.ToLocalTime():HH:mm}.");
        }

        if (verificacion == ResultadoVerificacionPassword.Fallida)
        {
            usuario.RegistrarIntentoFallido(ahora);
            await _usuarios.UpdateAsync(usuario, ct);

            return ResultadoAutenticacion.Fallo(
                MotivoFalloAutenticacion.CredencialesInvalidas,
                usuario.EstaBloqueada(ahora)
                    ? "Se superó el número de intentos permitidos. La cuenta fue bloqueada temporalmente."
                    : MensajeCredencialesInvalidas);
        }

        usuario.RegistrarAccesoExitoso(ahora);

        // Migra el hash cuando el framework indica que quedó obsoleto (misma contraseña, sin avisar al usuario).
        if (verificacion == ResultadoVerificacionPassword.CorrectaRequiereRehash)
            usuario.PasswordHash = _passwordHasher.Hash(password);

        await _usuarios.UpdateAsync(usuario, ct);

        return ResultadoAutenticacion.Ok(usuario);
    }

    public async Task<ResultadoCambioPassword> CambiarPasswordAsync(
        int usuarioId,
        string? passwordActual,
        string? passwordNueva,
        DateTime ahora,
        CancellationToken ct = default)
    {
        var result = new ResultadoCambioPassword();

        var usuario = await _usuarios.GetByIdAsync(usuarioId, ct);
        if (usuario is null)
        {
            result.AgregarError("El usuario no existe.");
            return result;
        }

        if (!usuario.EstaActivo)
        {
            result.AgregarError("La cuenta está desactivada. Contacte al administrador.");
            return result;
        }

        if (string.IsNullOrEmpty(passwordActual) ||
            _passwordHasher.Verify(usuario.PasswordHash, passwordActual) == ResultadoVerificacionPassword.Fallida)
        {
            result.AgregarError("La contraseña actual no es correcta.");
            return result;
        }

        var politica = PasswordPolicy.Validar(passwordNueva, usuario.NombreUsuario);
        foreach (var error in politica.Errores)
            result.AgregarError(error);

        if (result.Errores.Count > 0)
            return result;

        if (_passwordHasher.Verify(usuario.PasswordHash, passwordNueva!) != ResultadoVerificacionPassword.Fallida)
            result.AgregarError("La nueva contraseña debe ser distinta de la actual.");

        if (result.Errores.Count > 0)
            return result;

        usuario.EstablecerPassword(_passwordHasher.Hash(passwordNueva!));
        await _usuarios.UpdateAsync(usuario, ct);

        result.Usuario = usuario;
        return result;
    }
}
