#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppDonnyCuevas20210074.Models
{
    [Table("ConsultasAsistente")]
    public class ConsultaAsistente
    {
        [Key]
        public long IdConsulta { get; set; }

        [Required]
        public Guid IdSesion { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string MensajeUsuario { get; set; } = string.Empty;

        [StringLength(100)]
        public string? IntencionDetectada { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? RespuestaAsistente { get; set; }

        [StringLength(50)]
        public string? TipoRespuesta { get; set; }

        public bool? RespuestaCorrecta { get; set; }

        public int? TiempoRespuestaMilisegundos { get; set; }

        public DateTime FechaConsulta { get; set; } = DateTime.Now;

        // Visitante que hizo la consulta con su sesión iniciada; null si fue anónima o del personal.
        // Le permite ver y borrar su historial desde Mi perfil.
        public int? IdUsuario { get; set; }

        [ForeignKey(nameof(IdUsuario))]
        public virtual Usuario? Usuario { get; set; }
    }
}
