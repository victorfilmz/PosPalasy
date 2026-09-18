using Microsoft.AspNetCore.Authorization;
using POS.Domain.Enums;

namespace POS.UI.Security;

/// <summary>
/// Políticas de autorización del sistema POS. Cada política expresa una capacidad de negocio
/// ("puede vender", "puede anular un comprobante fiscal", "puede configurar el sistema") en lugar
/// de dispersar cadenas de roles por los controladores.
/// </summary>
public static class Politicas
{
    /// <summary>Operación de punto de venta y turno de caja.</summary>
    public const string OperacionPos = "OperacionPos";

    /// <summary>Facultades de supervisión: anulación fiscal, reenvío, consulta DGII y ajustes de inventario.</summary>
    public const string Supervision = "Supervision";

    /// <summary>Consulta de reportes fiscales (607, IT-1) y catálogo de comprobantes.</summary>
    public const string ReportesFiscales = "ReportesFiscales";

    /// <summary>Configuración de la empresa, del certificado digital y del formato de impresión.</summary>
    public const string Configuracion = "Configuracion";

    /// <summary>Cambio del ambiente DGII (certificación / producción) y pruebas de conectividad.</summary>
    public const string CambioAmbiente = "CambioAmbiente";

    /// <summary>Administración de cuentas de usuario del sistema.</summary>
    public const string GestionUsuarios = "GestionUsuarios";

    /// <summary>Roles autorizados por política. Fuente única para documentación y pruebas.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Matriz { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [OperacionPos] = Roles.OperacionPos,
            [Supervision] = Roles.Supervision,
            [ReportesFiscales] = Roles.ReportesFiscales,
            [Configuracion] = Roles.Configuracion,
            [CambioAmbiente] = new[] { Roles.SuperAdmin },
            [GestionUsuarios] = new[] { Roles.SuperAdmin }
        };

    /// <summary>Registra todas las políticas en el motor de autorización.</summary>
    public static void AgregarPoliticas(AuthorizationOptions opciones)
    {
        foreach (var (nombre, roles) in Matriz)
        {
            opciones.AddPolicy(nombre, politica => politica.RequireAuthenticatedUser().RequireRole(roles.ToArray()));
        }
    }
}
