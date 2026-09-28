#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    [Table("Lugares")]
    public class Lugar
    {
        [Key]
        public int IdLugar { get; set; }

        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? DescripcionCorta { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? Descripcion { get; set; }

        [StringLength(300)]
        public string? Direccion { get; set; }

        [StringLength(100)]
        public string? Municipio { get; set; }

        [StringLength(100)]
        public string? Provincia { get; set; }

        [Column(TypeName = "decimal(10,8)")]
        public decimal? Latitud { get; set; }

        [Column(TypeName = "decimal(11,8)")]
        public decimal? Longitud { get; set; }

        [StringLength(30)]
        public string? Telefono { get; set; }

        [StringLength(150)]
        public string? Correo { get; set; }

        [StringLength(300)]
        public string? SitioWeb { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public DateTime? FechaActualizacion { get; set; }

        // Navegación 1:1
        public virtual Atractivo? Atractivo { get; set; }

        // Navegación 1:N
        public virtual ICollection<Imagen> Imagenes { get; set; } = new List<Imagen>();
        public virtual ICollection<Horario> Horarios { get; set; } = new List<Horario>();
        public virtual ICollection<Contacto> Contactos { get; set; } = new List<Contacto>();

        // Muchos a muchos (EF Core genera LugarCategoria / LugarServicio automáticamente)
        public virtual ICollection<Categoria> Categorias { get; set; } = new List<Categoria>();
        public virtual ICollection<Servicio> Servicios { get; set; } = new List<Servicio>();
    }
}
