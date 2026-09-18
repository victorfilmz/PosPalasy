namespace POS.Infrastructure.Persistence;

/// <summary>
/// Credenciales de la cuenta administradora inicial. Se enlazan desde la sección
/// <c>Seguridad:AdminInicial</c> de configuración (variables de entorno o user-secrets),
/// nunca desde un archivo versionado con valor real.
/// </summary>
public sealed class OpcionesAdminInicial
{
    /// <summary>Nombre de usuario de la cuenta inicial.</summary>
    public string Usuario { get; set; } = "admin";

    /// <summary>
    /// Contraseña de la cuenta inicial. Si es nula, vacía o no cumple la política,
    /// el sistema genera una temporal y obliga a cambiarla en el primer acceso.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>Nombre completo mostrado en la interfaz.</summary>
    public string NombreCompleto { get; set; } = "Administrador del Sistema";
}
