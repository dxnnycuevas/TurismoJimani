#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    [Table("Usuarios")]
    public class Usuario
    {
        [Key]
        public int IdUsuario { get; set; }

        [Required]
        public int IdRol { get; set; }

        [Required]
        [StringLength(150)]
        public string NombreCompleto { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Correo { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string ContrasenaHash { get; set; } = string.Empty;

        // Datos del registro público. Son opcionales aquí: el personal y las cuentas antiguas no los tienen.
        [StringLength(20)]
        public string? TipoCuenta { get; set; }

        [StringLength(20)]
        public string? TipoDocumento { get; set; }

        // Sin guiones ni espacios (Helpers/DocumentoIdentidad.cs). Único por tipo de documento.
        [StringLength(30)]
        public string? NumeroDocumento { get; set; }

        [StringLength(150)]
        public string? NombreEmpresa { get; set; }

        [StringLength(30)]
        public string? Telefono { get; set; }

        [Column(TypeName = "date")]
        public DateTime? FechaNacimiento { get; set; }

        [StringLength(80)]
        public string? Nacionalidad { get; set; }

        [StringLength(80)]
        public string? Pais { get; set; }

        [StringLength(100)]
        public string? Ciudad { get; set; }

        [StringLength(200)]
        public string? Direccion { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public DateTime? FechaActualizacion { get; set; }

        [ForeignKey(nameof(IdRol))]
        public virtual Rol? Rol { get; set; }
    }
}
