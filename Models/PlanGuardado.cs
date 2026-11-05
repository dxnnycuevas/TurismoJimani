#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    // Plan de viaje del planificador de presupuesto que un visitante guardó desde el chat.
    // Se copia completo (partidas en Detalle, en JSON) para que no cambie si después cambian los precios.
    [Table("PlanesGuardados")]
    public class PlanGuardado
    {
        [Key]
        public int IdPlan { get; set; }

        [Required]
        public int IdUsuario { get; set; }

        // Ciudad de salida; vacío si el plan no incluye pasaje
        [StringLength(100)]
        public string Origen { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12,2)")]
        public decimal Monto { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal Total { get; set; }

        public int Noches { get; set; }

        public bool Cabe { get; set; }

        // Partidas del plan (List<LineaPresupuestoChat> serializada)
        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string Detalle { get; set; } = "[]";

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        [ForeignKey(nameof(IdUsuario))]
        public virtual Usuario? Usuario { get; set; }
    }
}
