using System;
using POS.Domain.Common;
using POS.Domain.Enums;

namespace POS.Domain.Entities;

/// <summary>
/// Usuario del sistema POS. El hash de contraseña se genera con PBKDF2 (formato ASP.NET Core Identity V3)
/// y nunca se almacena ni se registra la contraseña en claro.
/// </summary>
public class Usuario : BaseEntity
{
    /// <summary>Máximo de intentos fallidos consecutivos antes de bloquear la cuenta.</summary>
    public const int MaxIntentosFallidos = 5;

    /// <summary>Duración del bloqueo temporal tras superar el máximo de intentos fallidos.</summary>
    public static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(15);

    public string NombreUsuario { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; } = RolUsuario.Cajero;
    public bool EstaActivo { get; set; } = true;

    /// <summary>Obliga al usuario a definir una contraseña propia antes de usar el sistema (cuentas sembradas).</summary>
    public bool DebeCambiarPassword { get; set; }

    public int IntentosFallidos { get; set; }
    public DateTime? BloqueadoHasta { get; set; }
    public DateTime? UltimoAcceso { get; set; }

    /// <summary>
    /// Indica si la cuenta puede iniciar sesión en el instante indicado.
    /// Un bloqueo vencido se considera expirado y no impide el acceso.
    /// </summary>
    public bool PuedeIniciarSesion(DateTime ahora)
    {
        if (!EstaActivo) return false;
        if (string.IsNullOrWhiteSpace(PasswordHash)) return false;
        return BloqueadoHasta is null || BloqueadoHasta.Value <= ahora;
    }

    /// <summary>Indica si la cuenta está bloqueada temporalmente en ese instante.</summary>
    public bool EstaBloqueada(DateTime ahora) =>
        EstaActivo && BloqueadoHasta is not null && BloqueadoHasta.Value > ahora;

    /// <summary>
    /// Registra un intento de acceso fallido. Al alcanzar el máximo, bloquea la cuenta
    /// durante <see cref="DuracionBloqueo"/> y reinicia el contador.
    /// </summary>
    public void RegistrarIntentoFallido(DateTime ahora)
    {
        // Un bloqueo vencido se limpia antes de contar el nuevo intento.
        if (BloqueadoHasta is not null && BloqueadoHasta.Value <= ahora)
        {
            BloqueadoHasta = null;
            IntentosFallidos = 0;
        }

        IntentosFallidos++;

        if (IntentosFallidos >= MaxIntentosFallidos)
        {
            BloqueadoHasta = ahora.Add(DuracionBloqueo);
            IntentosFallidos = 0;
        }
    }

    /// <summary>Registra un acceso exitoso y limpia el estado de bloqueo.</summary>
    public void RegistrarAccesoExitoso(DateTime ahora)
    {
        IntentosFallidos = 0;
        BloqueadoHasta = null;
        UltimoAcceso = ahora;
    }

    /// <summary>Establece una nueva contraseña ya hasheada y libera la obligación de cambio.</summary>
    public void EstablecerPassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("El hash de contraseña no puede estar vacío.", nameof(passwordHash));

        PasswordHash = passwordHash;
        DebeCambiarPassword = false;
        IntentosFallidos = 0;
        BloqueadoHasta = null;
    }
}
