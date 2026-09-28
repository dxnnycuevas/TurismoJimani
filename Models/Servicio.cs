#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    [Table("Servicios")]
    public class Servicio
    {
        [Key]
        public int IdServicio { get; set; }

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        public bool Activo { get; set; } = true;

        public virtual ICollection<Lugar> Lugares { get; set; } = new List<Lugar>();

        public virtual ICollection<Prestador> Prestadores { get; set; } = new List<Prestador>();
    }
}
