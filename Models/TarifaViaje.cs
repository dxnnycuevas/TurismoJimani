#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    // Precio del pasaje desde una ciudad hasta Jimaní. Lo usa el planificador de presupuesto
    // del asistente (sp_PresupuestoViaje). Tabla independiente: no se relaciona con las demás.
    [Table("TarifasViaje")]
    public class TarifaViaje
    {
        [Key]
        public int IdTarifa { get; set; }

        // Ciudad de salida, por ejemplo "Santo Domingo"
        [Required]
        [StringLength(100)]
        public string Origen { get; set; } = string.Empty;

        // Guagua, carro público, carro propio...
        [Required]
        [StringLength(50)]
        public string Medio { get; set; } = "Guagua";

        // Precio de un solo trayecto (el planificador lo multiplica por 2 para ida y vuelta)
        [Required]
        [Range(1, 1000000)]
        [Column(TypeName = "decimal(10,2)")]
        public decimal PrecioIda { get; set; }

        [Range(0, 48)]
        [Column(TypeName = "decimal(4,1)")]
        public decimal? HorasViaje { get; set; }

        [StringLength(300)]
        public string? Nota { get; set; }

        public bool Activo { get; set; } = true;
    }

    // Una línea del presupuesto que devuelve sp_PresupuestoViaje (no es una tabla)
    public class LineaPresupuesto
    {
        public int Orden { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public string Opcion { get; set; } = string.Empty;
        public int? IdPrestador { get; set; }
        public decimal PrecioUnitario { get; set; }
        public int Cantidad { get; set; }
        public decimal Subtotal { get; set; }
    }
}
