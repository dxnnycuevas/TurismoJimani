#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    // Catálogo de tipos de servicio que gestiona el administrador:
    // personas (chofer, guía, barbero...) y establecimientos (hotel, restaurante, pyme...)
    [Table("TiposPrestador")]
    public class TipoPrestador
    {
        [Key]
        public int IdTipoPrestador { get; set; }

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        // Persona o Establecimiento (ver ClasesPrestador)
        [Required]
        [StringLength(20)]
        public string Clase { get; set; } = ClasesPrestador.Persona;

        // Agrupa los tipos para el sitio público y el asistente (ver GruposPrestador)
        [Required]
        [StringLength(30)]
        public string Grupo { get; set; } = GruposPrestador.Servicio;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        // Icono de Font Awesome (ej.: fa-car). Si está vacío se usa el del grupo.
        [StringLength(50)]
        public string? Icono { get; set; }

        public bool Activo { get; set; } = true;

        public virtual ICollection<Prestador> Prestadores { get; set; } = new List<Prestador>();
    }

    public static class ClasesPrestador
    {
        public const string Persona = "Persona";
        public const string Establecimiento = "Establecimiento";

        public static readonly string[] Todas = { Persona, Establecimiento };

        public static string Plural(string clase) => clase == Establecimiento ? "Establecimientos" : "Personas";
    }

    // Grupo de un tipo de servicio: decide en qué sección del sitio aparece,
    // qué campos extra pide el formulario y qué intención del asistente lo encuentra.
    public static class GruposPrestador
    {
        public const string Alojamiento = "Alojamiento";
        public const string Gastronomia = "Gastronomia";
        public const string Transporte = "Transporte";
        public const string Guia = "Guia";
        public const string Comercio = "Comercio";
        public const string Servicio = "Servicio";

        // (clave, nombre visible, icono)
        public static readonly (string Clave, string Nombre, string Icono)[] Todos =
        {
            (Alojamiento, "Alojamiento", "fa-hotel"),
            (Gastronomia, "Comida y bebida", "fa-utensils"),
            (Transporte, "Transporte", "fa-car"),
            (Guia, "Guías turísticos", "fa-hiking"),
            (Comercio, "Comercios y pymes", "fa-store"),
            (Servicio, "Otros servicios", "fa-concierge-bell")
        };

        public static string Nombre(string? grupo) =>
            Todos.FirstOrDefault(g => g.Clave == grupo).Nombre ?? "Servicios";

        public static string Icono(string? grupo) =>
            Todos.FirstOrDefault(g => g.Clave == grupo).Icono ?? "fa-concierge-bell";

        // Etiqueta del campo Capacidad según el grupo
        public static string EtiquetaCapacidad(string? grupo) => grupo switch
        {
            Alojamiento => "Habitaciones",
            Transporte => "Pasajeros",
            Guia => "Personas por grupo",
            _ => "Capacidad"
        };

        // Etiqueta del campo Especialidad según el grupo
        public static string EtiquetaEspecialidad(string? grupo) => grupo switch
        {
            Gastronomia => "Tipo de comida",
            Transporte => "Vehículo",
            Guia => "Idiomas y especialidad",
            Alojamiento => "Tipo de alojamiento",
            _ => "Especialidad"
        };
    }
}
