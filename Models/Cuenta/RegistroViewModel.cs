#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AppDonnyCuevas20210074.Helpers;

namespace AppDonnyCuevas20210074.Models.Cuenta
{
    // Formulario de registro público (crea una cuenta con el rol Visitante)
    public class RegistroViewModel : IValidatableObject
    {
        // S-01: 15 caracteres como mínimo (antes 6). La política completa está en Helpers/PoliticaContrasena.cs
        public const int LongitudMinimaContrasena = PoliticaContrasena.LongitudMinima;

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
        [Display(Name = "Tipo de cuenta")]
        public string TipoCuenta { get; set; } = DocumentoIdentidad.CuentaPersona;

        [StringLength(150)]
        [Display(Name = "Nombre de la empresa")]
        public string? NombreEmpresa { get; set; }

        [Required]
        [Display(Name = "Tipo de documento")]
        public string TipoDocumento { get; set; } = DocumentoIdentidad.Cedula;

        [Required]
        [StringLength(30)]
        [Display(Name = "Número de documento")]
        public string NumeroDocumento { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        [Phone(ErrorMessage = "Escribe un teléfono válido.")]
        [Display(Name = "Teléfono")]
        public string Telefono { get; set; } = string.Empty;

        [DataType(DataType.Date)]
        [Display(Name = "Fecha de nacimiento")]
        public DateTime? FechaNacimiento { get; set; }

        [StringLength(80)]
        [Display(Name = "Nacionalidad")]
        public string? Nacionalidad { get; set; }

        [StringLength(80)]
        [Display(Name = "País")]
        public string? Pais { get; set; }

        [StringLength(100)]
        [Display(Name = "Ciudad")]
        public string? Ciudad { get; set; }

        [StringLength(200)]
        [Display(Name = "Dirección")]
        public string? Direccion { get; set; }

        [Required]
        [StringLength(PoliticaContrasena.LongitudMaxima, MinimumLength = LongitudMinimaContrasena,
            ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Contrasena { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(Contrasena), ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmarContrasena { get; set; } = string.Empty;

        // Reglas que dependen del tipo de cuenta: persona con cédula o pasaporte, empresa con RNC
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var esEmpresa = TipoCuenta == DocumentoIdentidad.CuentaEmpresa;

            if (!esEmpresa && TipoCuenta != DocumentoIdentidad.CuentaPersona)
                yield return new ValidationResult("Selecciona un tipo de cuenta válido.", new[] { nameof(TipoCuenta) });

            if (esEmpresa && TipoDocumento != DocumentoIdentidad.Rnc)
                yield return new ValidationResult("Una empresa debe registrarse con su RNC.", new[] { nameof(TipoDocumento) });
            if (!esEmpresa && TipoDocumento == DocumentoIdentidad.Rnc)
                yield return new ValidationResult("Una persona debe registrarse con cédula o pasaporte.", new[] { nameof(TipoDocumento) });

            if (DocumentoIdentidad.Validar(TipoDocumento, DocumentoIdentidad.Normalizar(NumeroDocumento)) is { } error)
                yield return new ValidationResult(error, new[] { nameof(NumeroDocumento) });

            if (esEmpresa)
            {
                if (string.IsNullOrWhiteSpace(NombreEmpresa))
                    yield return new ValidationResult("Escribe el nombre de la empresa.", new[] { nameof(NombreEmpresa) });
                yield break;
            }

            if (FechaNacimiento is null)
                yield return new ValidationResult("Escribe tu fecha de nacimiento.", new[] { nameof(FechaNacimiento) });
            else if (FechaNacimiento.Value.Date >= DateTime.Today || FechaNacimiento.Value.Year < DateTime.Today.Year - 120)
                yield return new ValidationResult("La fecha de nacimiento no es válida.", new[] { nameof(FechaNacimiento) });

            if (string.IsNullOrWhiteSpace(Nacionalidad))
                yield return new ValidationResult("Escribe tu nacionalidad.", new[] { nameof(Nacionalidad) });
        }
    }
}
