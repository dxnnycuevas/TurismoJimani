#nullable enable
using System;
using System.Collections.Generic;

namespace AppDonnyCuevas20210074.Models.Panel
{
    // Datos del tablero del panel de administración (Home/Index)
    public class TableroViewModel
    {
        // ---- Indicadores ----
        public int Lugares { get; set; }
        public int Atractivos { get; set; }
        public int FotosLugares { get; set; }
        public int Prestadores { get; set; }
        public int Personas { get; set; }
        public int Establecimientos { get; set; }
        public int EventosProximos { get; set; }
        public int Consultas30Dias { get; set; }
        public int ConsultasPeriodoAnterior { get; set; }

        // Respuestas del asistente en los últimos 30 días según su tipo
        public int RespuestasConDatos { get; set; }
        public int RespuestasSinResultados { get; set; }
        public int RespuestasNoReconocidas { get; set; }
        public int RespuestasConError { get; set; }

        // ---- Gráficos (cada uno tiene también su vista de tabla) ----
        public List<PuntoSerie> ConsultasPorDia { get; set; } = new();
        public List<PuntoSerie> PrestadoresPorGrupo { get; set; } = new();
        public List<PuntoSerie> IntencionesFrecuentes { get; set; } = new();

        // ---- Listas ----
        public List<ConsultaReciente> ConsultasSinResultado { get; set; } = new();
        public List<ModuloPanel> Modulos { get; set; } = new();

        // Variación de consultas frente a los 30 días anteriores (null si no hay con qué comparar)
        public double? VariacionConsultas => ConsultasPeriodoAnterior == 0
            ? null
            : (Consultas30Dias - ConsultasPeriodoAnterior) * 100.0 / ConsultasPeriodoAnterior;

        public double PorcentajeConDatos => Consultas30Dias == 0 ? 0 : RespuestasConDatos * 100.0 / Consultas30Dias;
    }

    public record PuntoSerie(string Etiqueta, int Valor);

    public record ConsultaReciente(string Mensaje, string? Intencion, string? Tipo, DateTime Fecha);

    public record ModuloPanel(string Titulo, string Controlador, string Icono, int Total);
}
