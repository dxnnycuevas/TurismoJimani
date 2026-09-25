#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    // Datos cortos que el sitio público muestra al azar ("¿Sabías que...?")
    [Table("DatosCuriosos")]
    public class DatoCurioso
    {
        public static readonly string[] Categorias =
        {
            "Jimaní", "Naturaleza", "Cultura taína", "Cultura dominicana"
        };

        [Key]
        public int IdDato { get; set; }

        [Required]
        [StringLength(500)]
        public string Texto { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Categoria { get; set; } = "Jimaní";

        [StringLength(300)]
        public string? Fuente { get; set; }

        public bool Activo { get; set; } = true;
    }
}
