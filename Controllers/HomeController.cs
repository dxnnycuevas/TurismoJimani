using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using AppDonnyCuevas20210074.Models.Panel;
using AppDonnyCuevas20210074.Services.Asistente;

namespace AppDonnyCuevas20210074.Controllers;

[Authorize]
public class HomeController : Controller
{
    private const int DiasGraficoConsultas = 14;
    private const int MaximoIntenciones = 8;

    private readonly TurismoJimaniContext _context;

    public HomeController(TurismoJimaniContext context)
    {
        _context = context;
    }

    // Tablero: indicadores, gráficos y accesos rápidos
    public async Task<IActionResult> Index()
    {
        var hoy = DateTime.Today;
        var desde30 = hoy.AddDays(-29);   // los últimos 30 días, hoy incluido
        var desde60 = hoy.AddDays(-59);
        var consultas30 = _context.ConsultasAsistente.AsNoTracking().Where(c => c.FechaConsulta >= desde30);

        var modelo = new TableroViewModel
        {
            Lugares = await _context.Lugares.CountAsync(l => l.Activo),
            Atractivos = await _context.Atractivos.CountAsync(a => a.Lugar!.Activo),
            FotosLugares = await _context.Imagenes.CountAsync(i => i.IdLugar != null),
            Prestadores = await _context.Prestadores.CountAsync(p => p.Activo),
            Personas = await _context.Prestadores.CountAsync(p => p.Activo && p.TipoPrestador!.Clase == ClasesPrestador.Persona),
            Establecimientos = await _context.Prestadores.CountAsync(p => p.Activo && p.TipoPrestador!.Clase == ClasesPrestador.Establecimiento),
            EventosProximos = await _context.Eventos.CountAsync(e => e.Publicado && (e.FechaFin ?? e.FechaInicio) >= hoy),
            Consultas30Dias = await consultas30.CountAsync(),
            ConsultasPeriodoAnterior = await _context.ConsultasAsistente.CountAsync(c => c.FechaConsulta >= desde60 && c.FechaConsulta < desde30),
            RespuestasConDatos = await consultas30.CountAsync(c => c.TipoRespuesta == TiposRespuesta.Datos),
            RespuestasSinResultados = await consultas30.CountAsync(c => c.TipoRespuesta == TiposRespuesta.SinResultados),
            RespuestasNoReconocidas = await consultas30.CountAsync(c => c.TipoRespuesta == TiposRespuesta.NoReconocida),
            RespuestasConError = await consultas30.CountAsync(c =>
                c.TipoRespuesta == TiposRespuesta.Error || c.TipoRespuesta == TiposRespuesta.ErrorIA || c.TipoRespuesta == TiposRespuesta.ErrorBD),
            ConsultasPorDia = await ConsultasPorDiaAsync(hoy),
            PrestadoresPorGrupo = await PrestadoresPorGrupoAsync(),
            IntencionesFrecuentes = await IntencionesFrecuentesAsync(desde30),
            ConsultasSinResultado = await _context.ConsultasAsistente.AsNoTracking()
                .Where(c => c.TipoRespuesta == TiposRespuesta.SinResultados || c.TipoRespuesta == TiposRespuesta.NoReconocida)
                .OrderByDescending(c => c.FechaConsulta)
                .Take(6)
                .Select(c => new ConsultaReciente(c.MensajeUsuario, c.IntencionDetectada, c.TipoRespuesta, c.FechaConsulta))
                .ToListAsync(),
            Modulos = await ModulosAsync()
        };

        return View(modelo);
    }

    // Consultas por día de los últimos 14 días (los días sin consultas cuentan como 0)
    private async Task<List<PuntoSerie>> ConsultasPorDiaAsync(DateTime hoy)
    {
        var desde = hoy.AddDays(-(DiasGraficoConsultas - 1));
        var totales = await _context.ConsultasAsistente.AsNoTracking()
            .Where(c => c.FechaConsulta >= desde)
            .GroupBy(c => c.FechaConsulta.Date)
            .Select(g => new { Dia = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.Dia, g => g.Total);

        var cultura = CultureInfo.GetCultureInfo("es-DO");
        return Enumerable.Range(0, DiasGraficoConsultas)
            .Select(i => desde.AddDays(i))
            .Select(dia => new PuntoSerie(dia.ToString("d MMM", cultura).TrimEnd('.'), totales.GetValueOrDefault(dia)))
            .ToList();
    }

    // Prestadores publicados por grupo, de mayor a menor (se muestran todos los grupos, aunque tengan 0)
    private async Task<List<PuntoSerie>> PrestadoresPorGrupoAsync()
    {
        var totales = await _context.Prestadores.AsNoTracking()
            .Where(p => p.Activo)
            .GroupBy(p => p.TipoPrestador!.Grupo)
            .Select(g => new { Grupo = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.Grupo, g => g.Total);

        return GruposPrestador.Todos
            .Select(g => new PuntoSerie(g.Nombre, totales.GetValueOrDefault(g.Clave)))
            .OrderByDescending(p => p.Valor)
            .ToList();
    }

    // Intenciones más detectadas por BERT en los últimos 30 días ("BuscarTransporte" -> "Buscar transporte")
    private async Task<List<PuntoSerie>> IntencionesFrecuentesAsync(DateTime desde)
    {
        var totales = await _context.ConsultasAsistente.AsNoTracking()
            .Where(c => c.FechaConsulta >= desde && c.IntencionDetectada != null)
            .GroupBy(c => c.IntencionDetectada!)
            .Select(g => new { Intencion = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .Take(MaximoIntenciones)
            .ToListAsync();

        return totales.Select(t => new PuntoSerie(NombreLegible(t.Intencion), t.Total)).ToList();
    }

    private static string NombreLegible(string intencion)
    {
        var texto = Regex.Replace(intencion, "(?<!^)(\\p{Lu})", " $1").ToLowerInvariant();
        return texto.Length == 0 ? intencion : char.ToUpperInvariant(texto[0]) + texto[1..];
    }

    // Accesos rápidos con la cantidad de registros de cada módulo
    private async Task<List<ModuloPanel>> ModulosAsync()
    {
        var modulos = new List<ModuloPanel>
        {
            new("Lugares", "Lugares", "fa-map-marker-alt", await _context.Lugares.CountAsync()),
            new("Atractivos", "Atractivos", "fa-mountain", await _context.Atractivos.CountAsync()),
            new("Prestadores", "Prestadores", "fa-concierge-bell", await _context.Prestadores.CountAsync()),
            new("Tipos de servicio", "TiposPrestador", "fa-list", await _context.TiposPrestador.CountAsync()),
            new("Rutas", "Rutas", "fa-route", await _context.Rutas.CountAsync()),
            new("Eventos", "Eventos", "fa-calendar-alt", await _context.Eventos.CountAsync()),
            new("Imágenes", "Imagenes", "fa-images", await _context.Imagenes.CountAsync()),
            new("Categorías", "Categorias", "fa-tags", await _context.Categorias.CountAsync()),
            new("Comodidades", "Servicios", "fa-wifi", await _context.Servicios.CountAsync()),
            new("Datos curiosos", "DatosCuriosos", "fa-lightbulb", await _context.DatosCuriosos.CountAsync()),
            new("Ejemplos de intención", "EjemplosIntencion", "fa-list-ul", await _context.EjemplosIntencion.CountAsync()),
            new("Consultas al asistente", "ConsultasAsistente", "fa-history", await _context.ConsultasAsistente.CountAsync())
        };

        if (User.IsInRole(RolesSistema.Administrador))
            modulos.Add(new("Usuarios", "Usuarios", "fa-users", await _context.Usuarios.CountAsync()));

        return modulos;
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
