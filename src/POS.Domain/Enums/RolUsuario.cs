using System;
using System.Collections.Generic;
using System.Linq;

namespace POS.Domain.Enums;

/// <summary>
/// Roles operativos del sistema POS. El rol determina qué operaciones sensibles
/// (configuración, anulación fiscal, ajustes de inventario, usuarios) puede ejecutar un usuario.
/// </summary>
public enum RolUsuario : int
{
    /// <summary>Administrador técnico del sistema. Único rol autorizado a cambiar el ambiente DGII y gestionar usuarios.</summary>
    SuperAdmin = 1,

    /// <summary>Administrador del negocio: empresa, certificado, catálogo, inventario y reportes.</summary>
    Administrador = 2,

    /// <summary>Supervisor de tienda: anulaciones, ajustes de inventario, reportes y operación de caja.</summary>
    Supervisor = 3,

    /// <summary>Cajero: punto de venta y turno de caja. No puede anular comprobantes ni configurar el sistema.</summary>
    Cajero = 4,

    /// <summary>Contador: lectura de reportes fiscales (607, IT-1) y de comprobantes.</summary>
    Contador = 5
}

/// <summary>
/// Nombres canónicos de los roles para claims, políticas y atributos de autorización.
/// Fuente única de verdad: evita cadenas literales dispersas en atributos [Authorize(Roles = "...")].
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Administrador = "Administrador";
    public const string Supervisor = "Supervisor";
    public const string Cajero = "Cajero";
    public const string Contador = "Contador";

    /// <summary>Todos los roles del sistema.</summary>
    public static IReadOnlyList<string> Todos { get; } = new[]
    {
        SuperAdmin, Administrador, Supervisor, Cajero, Contador
    };

    /// <summary>Rol operativo del POS: cajeros, supervisores y administradores pueden vender.</summary>
    public static IReadOnlyList<string> OperacionPos { get; } = new[]
    {
        SuperAdmin, Administrador, Supervisor, Cajero
    };

    /// <summary>Roles con facultades de supervisión sobre la operación (no configuración global).</summary>
    public static IReadOnlyList<string> Supervision { get; } = new[]
    {
        SuperAdmin, Administrador, Supervisor
    };

    /// <summary>Roles que pueden ver información fiscal consolidada.</summary>
    public static IReadOnlyList<string> ReportesFiscales { get; } = new[]
    {
        SuperAdmin, Administrador, Supervisor, Contador
    };

    /// <summary>Roles con acceso a configuración del sistema (empresa, certificado, impresión).</summary>
    public static IReadOnlyList<string> Configuracion { get; } = new[]
    {
        SuperAdmin, Administrador
    };

    /// <summary>Convierte un rol a su nombre canónico.</summary>
    public static string NombreDe(RolUsuario rol) => rol switch
    {
        RolUsuario.SuperAdmin => SuperAdmin,
        RolUsuario.Administrador => Administrador,
        RolUsuario.Supervisor => Supervisor,
        RolUsuario.Cajero => Cajero,
        RolUsuario.Contador => Contador,
        _ => throw new ArgumentOutOfRangeException(nameof(rol), rol, "Rol de usuario no reconocido.")
    };

    /// <summary>Intenta resolver un nombre canónico a un rol del dominio.</summary>
    public static bool TryParse(string? nombre, out RolUsuario rol)
    {
        rol = default;
        if (string.IsNullOrWhiteSpace(nombre)) return false;

        var encontrado = Enum.GetValues<RolUsuario>()
            .FirstOrDefault(r => string.Equals(NombreDe(r), nombre.Trim(), StringComparison.OrdinalIgnoreCase));

        if (encontrado == default) return false;

        rol = encontrado;
        return true;
    }
}
