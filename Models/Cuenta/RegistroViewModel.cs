#nullable enable
using System.ComponentModel.DataAnnotations;

namespace AppDonnyCuevas20210074.Models.Cuenta
{
    // Formulario de registro público (crea una cuenta con el rol Visitante)
    public class RegistroViewModel
    {
        public const int LongitudMinimaContrasena = 6;

        [Required]
        [StringLength(150)]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [EmailAddress(ErrorMessage = "Escribe un correo electrónico válido.")]
        [Display(Name = "Correo electrónico")]
        public string Correo { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = LongitudMinimaContrasena,
            ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Contrasena { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(Contrasena), ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmarContrasena { get; set; } = string.Empty;
    }
}
