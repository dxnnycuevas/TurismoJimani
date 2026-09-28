#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    // Prestador de servicios turísticos: una persona (chofer, guía, barbero...)
    // o un establecimiento (hotel, restaurante, pyme...). El tipo decide cuál es.
    [Table("Prestadores")]
    public class Prestador
    {
        [Key]
        public int IdPrestador { get; set; }

        [Required]
        public int IdTipoPrestador { get; set; }

        // Nombre de la persona ("Raúl Pérez") o del negocio ("Hotel Jimaní")
        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? DescripcionCorta { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? Descripcion { get; set; }

        // ---- Contacto ----
        [StringLength(30)]
        public string? Telefono { get; set; }

        [StringLength(30)]
        public string? WhatsApp { get; set; }

        [StringLength(150)]
        [EmailAddress]
        public string? Correo { get; set; }

        [StringLength(300)]
        public string? SitioWeb { get; set; }

        [StringLength(300)]
        public string? RedSocial { get; set; }

        // ---- Ubicación (sobre todo para establecimientos) ----
        [StringLength(300)]
        public string? Direccion { get; set; }

        [StringLength(100)]
        public string? Municipio { get; set; } = "Jimaní";

        [StringLength(100)]
        public string? Provincia { get; set; } = "Independencia";

        [Column(TypeName = "decimal(10,8)")]
        public decimal? Latitud { get; set; }

        [Column(TypeName = "decimal(11,8)")]
        public decimal? Longitud { get; set; }

        // Dónde ofrece el servicio (ej.: "Jimaní, Duvergé y La Descubierta")
        [StringLength(300)]
        public string? ZonaCobertura { get; set; }

        // Texto libre: "Lunes a sábado, 8:00 a. m. – 6:00 p. m."
        [StringLength(300)]
        public string? HorarioAtencion { get; set; }

        // ---- Precios ----
        [Column(TypeName = "decimal(10,2)")]
        public decimal? PrecioDesde { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? PrecioHasta { get; set; }

        [StringLength(300)]
        public string? InformacionPrecio { get; set; }

        // ---- Datos según el grupo del tipo ----
        // Tipo de comida, vehículo, idiomas del guía...
        [StringLength(150)]
        public string? Especialidad { get; set; }

        // Habitaciones, pasajeros, personas por grupo...
        [Range(0, 100000)]
        public int? Capacidad { get; set; }

        public bool RequiereReserva { get; set; } = false;

        public bool ServicioDomicilio { get; set; } = false;

        // Enlace para reservar o ver el menú o catálogo
        [StringLength(500)]
        public string? EnlaceExterno { get; set; }

        public bool Destacado { get; set; } = false;

        public bool Activo { get; set; } = true;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public DateTime? FechaActualizacion { get; set; }

        [ForeignKey(nameof(IdTipoPrestador))]
        public virtual TipoPrestador? TipoPrestador { get; set; }

        public virtual ICollection<Imagen> Imagenes { get; set; } = new List<Imagen>();

        // Comodidades (wifi, parqueo, aire acondicionado...)
        public virtual ICollection<Servicio> Servicios { get; set; } = new List<Servicio>();
    }
}
