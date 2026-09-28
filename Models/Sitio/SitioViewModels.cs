#nullable enable
using System.Collections.Generic;

namespace AppDonnyCuevas20210074.Models.Sitio
{
    // Cantidad de registros publicados, usada en "Sobre Jimaní" y "Planifica tu visita"
    public class EstadisticasSitio
    {
        public int Lugares { get; set; }
        public int Atractivos { get; set; }
        public int Prestadores { get; set; }
        public int Alojamientos { get; set; }
        public int Restaurantes { get; set; }
        public int Transportes { get; set; }
        public int Guias { get; set; }
        public int Rutas { get; set; }
        public int Eventos { get; set; }
    }

    public class InicioViewModel
    {
        public List<Lugar> Destinos { get; set; } = new();
        public List<string> TiposAtractivo { get; set; } = new();
        public List<Evento> Eventos { get; set; } = new();
        public List<Ruta> Rutas { get; set; } = new();
        public EstadisticasSitio Estadisticas { get; set; } = new();
    }

    public class LugaresViewModel
    {
        public List<Lugar> Lugares { get; set; } = new();
        public List<Categoria> Categorias { get; set; } = new();
        public string? Buscar { get; set; }
        public string? Tipo { get; set; }
        public int? Categoria { get; set; }
    }

    public class LugarViewModel
    {
        public Lugar Lugar { get; set; } = null!;
        public List<Ruta> Rutas { get; set; } = new();
        public List<Evento> Eventos { get; set; } = new();
        public List<Lugar> Relacionados { get; set; } = new();
    }

    // Listado público de prestadores de servicios (personas y establecimientos)
    public class ServiciosViewModel
    {
        public List<Prestador> Prestadores { get; set; } = new();
        public List<TipoPrestador> Tipos { get; set; } = new();
        public Dictionary<string, int> TotalesPorGrupo { get; set; } = new();
        public string? Buscar { get; set; }
        public string? Grupo { get; set; }
        public string? Clase { get; set; }
        public int? Tipo { get; set; }
    }

    public class PrestadorViewModel
    {
        public Prestador Prestador { get; set; } = null!;
        public List<Prestador> Relacionados { get; set; } = new();
    }

    public class EventosViewModel
    {
        public List<Evento> Eventos { get; set; } = new();
        public List<string> Tipos { get; set; } = new();
        public string? Tipo { get; set; }
        public bool Anteriores { get; set; }
    }

    public class EventoViewModel
    {
        public Evento Evento { get; set; } = null!;
        public List<Evento> Otros { get; set; } = new();
    }
}
