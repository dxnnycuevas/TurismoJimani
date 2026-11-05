#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    // Lugar o prestador que un visitante guardó desde su ficha (IV-20: función de la cuenta de visitante).
    // Cada fila apunta a uno solo de los dos (CK_Favoritos_Elemento).
    [Table("Favoritos")]
    public class Favorito
    {
        [Key]
        public int IdFavorito { get; set; }

        [Required]
        public int IdUsuario { get; set; }

        public int? IdLugar { get; set; }

        public int? IdPrestador { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        [ForeignKey(nameof(IdUsuario))]
        public virtual Usuario? Usuario { get; set; }

        [ForeignKey(nameof(IdLugar))]
        public virtual Lugar? Lugar { get; set; }

        [ForeignKey(nameof(IdPrestador))]
        public virtual Prestador? Prestador { get; set; }
    }
}
