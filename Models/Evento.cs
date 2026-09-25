#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    // Eventos y promociones del municipio (fiestas patronales, festivales, ofertas...),
    // publicados en el sitio como entradas de blog
    [Table("Eventos")]
    public class Evento
    {
        public static readonly string[] Tipos =
        {
            "Fiesta patronal", "Festival", "Actividad cultural", "Religioso",
            "Deportivo", "Gastronómico", "Promoción", "Otro"
        };

        [Key]
        public int IdEvento { get; set; }

        [Required]
        [StringLength(200)]
        public string Titulo { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Tipo { get; set; } = "Fiesta patronal";

        [StringLength(500)]
        public string? Resumen { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? Contenido { get; set; }

        [Required]
        public DateTime FechaInicio { get; set; } = DateTime.Today;

        public DateTime? FechaFin { get; set; }

        // Lugar registrado donde ocurre (opcional) o texto libre si no está en la lista
        public int? IdLugar { get; set; }

        [StringLength(200)]
        public string? LugarTexto { get; set; }

        [StringLength(150)]
        public string? Organizador { get; set; }

        [StringLength(500)]
        public string? EnlaceExterno { get; set; }

        [StringLength(500)]
        public string? ImagenUrl { get; set; }

        [StringLength(150)]
        public string? ImagenAutor { get; set; }

        [StringLength(500)]
        public string? ImagenFuenteUrl { get; set; }

        public bool Destacado { get; set; } = false;

        public bool Publicado { get; set; } = true;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public DateTime? FechaActualizacion { get; set; }

        [ForeignKey(nameof(IdLugar))]
        public virtual Lugar? Lugar { get; set; }
    }
}
