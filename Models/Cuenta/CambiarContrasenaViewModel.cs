#nullable enable
using System.ComponentModel.DataAnnotations;
using AppDonnyCuevas20210074.Helpers;

namespace AppDonnyCuevas20210074.Models.Cuenta
{
    // Formulario de cambio de contraseña (obligatorio en el primer acceso del administrador
    // y cuando la contraseña actual ya no cumple la política)
    public class CambiarContrasenaViewModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña actual")]
        public string ContrasenaActual { get; set; } = string.Empty;

        [Required]
        [StringLength(PoliticaContrasena.LongitudMaxima, MinimumLength = PoliticaContrasena.LongitudMinima,
            ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string NuevaContrasena { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(NuevaContrasena), ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar nueva contraseña")]
        public string ConfirmarContrasena { get; set; } = string.Empty;
    }
}
