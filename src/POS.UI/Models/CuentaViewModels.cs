using System.ComponentModel.DataAnnotations;

namespace POS.UI.Models;

/// <summary>Datos del formulario de inicio de sesión.</summary>
public class LoginViewModel
{
    [Required(ErrorMessage = "El usuario es obligatorio.")]
    [Display(Name = "Usuario")]
    [StringLength(60, ErrorMessage = "El usuario no puede superar los 60 caracteres.")]
    public string Usuario { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [Display(Name = "Contraseña")]
    [DataType(DataType.Password)]
    [StringLength(128, ErrorMessage = "La contraseña no puede superar los 128 caracteres.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>URL de retorno validada contra redirecciones abiertas.</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>Datos del formulario de cambio de contraseña propia.</summary>
public class CambiarPasswordViewModel
{
    [Required(ErrorMessage = "La contraseña actual es obligatoria.")]
    [Display(Name = "Contraseña actual")]
    [DataType(DataType.Password)]
    public string PasswordActual { get; set; } = string.Empty;

    [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
    [Display(Name = "Nueva contraseña")]
    [DataType(DataType.Password)]
    public string PasswordNueva { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debe confirmar la nueva contraseña.")]
    [Display(Name = "Confirmar nueva contraseña")]
    [DataType(DataType.Password)]
    [Compare(nameof(PasswordNueva), ErrorMessage = "La confirmación no coincide con la nueva contraseña.")]
    public string ConfirmarPassword { get; set; } = string.Empty;

    /// <summary>Indica si el cambio es obligatorio (cuenta con contraseña temporal).</summary>
    public bool EsObligatorio { get; set; }
}
