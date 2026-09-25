#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    [Table("Imagenes")]
    public class Imagen
    {
        [Key]
        public int IdImagen { get; set; }

        [Required]
        public int IdLugar { get; set; }

        [Required]
        [StringLength(500)]
        public string UrlImagen { get; set; } = string.Empty;

        [StringLength(255)]
        public string? TextoAlternativo { get; set; }

        [StringLength(500)]
        public string? Descripcion { get; set; }

        public bool EsPrincipal { get; set; } = false;

        public int OrdenVisualizacion { get; set; } = 0;

        // Crédito al dueño original de la foto (se muestra en el sitio público)
        [StringLength(150)]
        public string? Autor { get; set; }

        [StringLength(500)]
        public string? FuenteUrl { get; set; }

        [StringLength(100)]
        public string? Licencia { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        [ForeignKey(nameof(IdLugar))]
        public virtual Lugar? Lugar { get; set; }
    }
}
