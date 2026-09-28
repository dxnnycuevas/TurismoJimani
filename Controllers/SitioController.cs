using System.Text;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using AppDonnyCuevas20210074.Models.Sitio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

// Sitio público para visitantes. No requiere iniciar sesión.
[AllowAnonymous]
public class SitioController : Controller
{
    private readonly TurismoJimaniContext _context;

    public SitioController(TurismoJimaniContext context)
    {
        _context = context;
    }

    // Lugares activos con todo lo necesario para las tarjetas (tipo e imagen)
    private IQueryable<Lugar> LugaresPublicados() => _context.Lugares
        .AsNoTracking()
        .Where(l => l.Activo)
        .Include(l => l.Atractivo)
        .Include(l => l.Imagenes);

    // Prestadores publicados con su tipo y fotos
    private IQueryable<Prestador> PrestadoresPublicados() => _context.Prestadores
        .AsNoTracking()
        .Where(p => p.Activo)
        .Include(p => p.TipoPrestador)
        .Include(p => p.Imagenes);

    private Task<int> ContarGrupoAsync(string grupo) =>
        _context.Prestadores.CountAsync(p => p.Activo && p.TipoPrestador!.Grupo == grupo);

    private IQueryable<Evento> EventosPublicados() => _context.Eventos
        .AsNoTracking()
        .Where(e => e.Publicado)
        .Include(e => e.Lugar);

    // Un evento sigue en la agenda hasta que termina (o hasta el final de su día si no tiene fin)
    private IQueryable<Evento> EventosProximos() =>
        EventosPublicados().Where(e => (e.FechaFin ?? e.FechaInicio) >= DateTime.Today);

    private async Task<EstadisticasSitio> ObtenerEstadisticasAsync() => new()
    {
        Lugares = await _context.Lugares.CountAsync(l => l.Activo),
        Atractivos = await _context.Atractivos.CountAsync(a => a.Lugar!.Activo),
        Prestadores = await _context.Prestadores.CountAsync(p => p.Activo),
        Alojamientos = await ContarGrupoAsync(GruposPrestador.Alojamiento),
        Restaurantes = await ContarGrupoAsync(GruposPrestador.Gastronomia),
        Transportes = await ContarGrupoAsync(GruposPrestador.Transporte),
        Guias = await ContarGrupoAsync(GruposPrestador.Guia),
        Rutas = await _context.Rutas.CountAsync(r => r.Activa),
        Eventos = await EventosProximos().CountAsync()
    };

    // GET: /  (página de inicio)
    public async Task<IActionResult> Index()
    {
        var destinos = await LugaresPublicados()
            .Where(l => l.Atractivo != null)
            .OrderByDescending(l => l.Atractivo!.Destacado)
            .ThenBy(l => l.IdLugar)
            .Take(6)
            .ToListAsync();

        var modelo = new InicioViewModel
        {
            Destinos = destinos,
            TiposAtractivo = destinos
                .Select(l => l.Atractivo!.TipoAtractivo)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!)
                .Distinct()
                .OrderBy(t => t)
                .ToList(),
            Eventos = await EventosProximos()
                .OrderByDescending(e => e.Destacado)
                .ThenBy(e => e.FechaInicio)
                .Take(3)
                .ToListAsync(),
            Rutas = await _context.Rutas.AsNoTracking()
                .Where(r => r.Activa)
                .Include(r => r.LugarOrigen)
                .Include(r => r.LugarDestino)
                .OrderBy(r => r.Nombre)
                .Take(3)
                .ToListAsync(),
            Estadisticas = await ObtenerEstadisticasAsync()
        };

        return View(modelo);
    }

    // GET: /Sitio/Nosotros
    public async Task<IActionResult> Nosotros()
    {
        return View(await ObtenerEstadisticasAsync());
    }

    // GET: /Sitio/Lugares?buscar=lago&tipo=atractivos&categoria=2
    public async Task<IActionResult> Lugares(string? buscar, string? tipo, int? categoria)
    {
        // Los enlaces antiguos a alojamientos, restaurantes y transporte ahora están en Servicios
        var grupoAntiguo = tipo switch
        {
            "alojamientos" => GruposPrestador.Alojamiento,
            "restaurantes" => GruposPrestador.Gastronomia,
            "transporte" => GruposPrestador.Transporte,
            _ => null
        };
        if (grupoAntiguo != null)
            return RedirectToAction(nameof(Servicios), new { grupo = grupoAntiguo, buscar });

        var consulta = LugaresPublicados();

        buscar = buscar?.Trim();
        if (!string.IsNullOrEmpty(buscar))
        {
            consulta = consulta.Where(l =>
                l.Nombre.Contains(buscar) ||
                (l.DescripcionCorta != null && l.DescripcionCorta.Contains(buscar)) ||
                (l.Municipio != null && l.Municipio.Contains(buscar)));
        }

        if (tipo == "atractivos")
            consulta = consulta.Where(l => l.Atractivo != null);
        else
            tipo = null;

        if (categoria.HasValue)
            consulta = consulta.Where(l => l.Categorias.Any(c => c.IdCategoria == categoria));

        var modelo = new LugaresViewModel
        {
            Lugares = await consulta.OrderBy(l => l.Nombre).ToListAsync(),
            Categorias = await _context.Categorias.AsNoTracking()
                .Where(c => c.Activo)
                .OrderBy(c => c.Nombre)
                .ToListAsync(),
            Buscar = buscar,
            Tipo = tipo,
            Categoria = categoria
        };

        return View(modelo);
    }

    // GET: /Sitio/Lugar/5
    public async Task<IActionResult> Lugar(int? id)
    {
        if (id == null) return NotFound();

        var lugar = await LugaresPublicados()
            .Include(l => l.Horarios)
            .Include(l => l.Contactos)
            .Include(l => l.Categorias)
            .Include(l => l.Servicios)
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.IdLugar == id);

        if (lugar == null) return NotFound();

        // Otros lugares para visitar (primero los destacados)
        var relacionados = LugaresPublicados()
            .Where(l => l.IdLugar != lugar.IdLugar)
            .OrderByDescending(l => l.Atractivo != null && l.Atractivo.Destacado);

        var modelo = new LugarViewModel
        {
            Lugar = lugar,
            Rutas = await _context.Rutas.AsNoTracking()
                .Where(r => r.Activa && (r.IdLugarOrigen == id || r.IdLugarDestino == id))
                .Include(r => r.LugarOrigen)
                .Include(r => r.LugarDestino)
                .OrderBy(r => r.Nombre)
                .ToListAsync(),
            Eventos = await EventosProximos()
                .Where(e => e.IdLugar == id)
                .OrderBy(e => e.FechaInicio)
                .Take(3)
                .ToListAsync(),
            Relacionados = await relacionados.ThenBy(l => l.Nombre).Take(3).ToListAsync()
        };

        return View(modelo);
    }

    // GET: /Sitio/Servicios?grupo=Transporte&clase=Persona&tipo=2&buscar=raul
    public async Task<IActionResult> Servicios(string? buscar, string? grupo, string? clase, int? tipo)
    {
        var consulta = PrestadoresPublicados();

        buscar = buscar?.Trim();
        if (!string.IsNullOrEmpty(buscar))
        {
            consulta = consulta.Where(p =>
                p.Nombre.Contains(buscar) ||
                p.TipoPrestador!.Nombre.Contains(buscar) ||
                (p.Especialidad != null && p.Especialidad.Contains(buscar)) ||
                (p.DescripcionCorta != null && p.DescripcionCorta.Contains(buscar)));
        }

        if (!GruposPrestador.Todos.Any(g => g.Clave == grupo)) grupo = null;
        if (!ClasesPrestador.Todas.Contains(clase)) clase = null;

        if (grupo != null) consulta = consulta.Where(p => p.TipoPrestador!.Grupo == grupo);
        if (clase != null) consulta = consulta.Where(p => p.TipoPrestador!.Clase == clase);
        if (tipo.HasValue) consulta = consulta.Where(p => p.IdTipoPrestador == tipo);

        var modelo = new ServiciosViewModel
        {
            Prestadores = await consulta
                .OrderByDescending(p => p.Destacado)
                .ThenBy(p => p.Nombre)
                .AsSplitQuery()
                .ToListAsync(),
            // Solo los tipos que tienen prestadores publicados (para el filtro)
            Tipos = await _context.TiposPrestador.AsNoTracking()
                .Where(t => t.Prestadores.Any(p => p.Activo) && (grupo == null || t.Grupo == grupo))
                .OrderBy(t => t.Nombre)
                .ToListAsync(),
            TotalesPorGrupo = await _context.Prestadores.AsNoTracking()
                .Where(p => p.Activo)
                .GroupBy(p => p.TipoPrestador!.Grupo)
                .Select(g => new { g.Key, Total = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Total),
            Buscar = buscar,
            Grupo = grupo,
            Clase = clase,
            Tipo = tipo
        };

        return View(modelo);
    }

    // GET: /Sitio/Prestador/5
    public async Task<IActionResult> Prestador(int? id)
    {
        if (id == null) return NotFound();

        // El personal puede ver un prestador aún no publicado para revisarlo
        var consulta = RolesSistema.EsPersonal(User)
            ? _context.Prestadores.AsNoTracking().Include(p => p.TipoPrestador).Include(p => p.Imagenes)
            : PrestadoresPublicados();

        var prestador = await consulta
            .Include(p => p.Servicios)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.IdPrestador == id);

        if (prestador == null) return NotFound();

        var modelo = new PrestadorViewModel
        {
            Prestador = prestador,
            // Otros del mismo grupo, primero los del mismo tipo
            Relacionados = await PrestadoresPublicados()
                .Where(p => p.IdPrestador != prestador.IdPrestador && p.TipoPrestador!.Grupo == prestador.TipoPrestador!.Grupo)
                .OrderByDescending(p => p.IdTipoPrestador == prestador.IdTipoPrestador)
                .ThenByDescending(p => p.Destacado)
                .ThenBy(p => p.Nombre)
                .Take(3)
                .ToListAsync()
        };

        return View(modelo);
    }

    // GET: /Sitio/Sorpresa  -> un lugar al azar
    public async Task<IActionResult> Sorpresa()
    {
        var id = await _context.Lugares.AsNoTracking()
            .Where(l => l.Activo)
            .OrderBy(l => Guid.NewGuid())
            .Select(l => (int?)l.IdLugar)
            .FirstOrDefaultAsync();

        return id.HasValue
            ? RedirectToAction(nameof(Lugar), new { id })
            : RedirectToAction(nameof(Lugares));
    }

    // GET: /Sitio/Dato?excluir=3  -> dato curioso al azar en JSON (para las caritas del sitio)
    [HttpGet]
    public async Task<IActionResult> Dato(int? excluir)
    {
        var activos = _context.DatosCuriosos.AsNoTracking().Where(d => d.Activo);

        var dato = await activos.Where(d => d.IdDato != excluir).OrderBy(d => Guid.NewGuid()).FirstOrDefaultAsync()
                   ?? await activos.FirstOrDefaultAsync();

        if (dato == null)
            return Ok(new { id = 0, texto = "Jimaní está entre el Lago Enriquillo y la Sierra de Bahoruco.", categoria = "Jimaní", fuente = (string?)null });

        return Ok(new { id = dato.IdDato, texto = dato.Texto, categoria = dato.Categoria, fuente = dato.Fuente });
    }

    // GET: /Sitio/Rutas
    public async Task<IActionResult> Rutas()
    {
        var rutas = await _context.Rutas.AsNoTracking()
            .Where(r => r.Activa)
            .Include(r => r.LugarOrigen)
            .Include(r => r.LugarDestino)
            .OrderBy(r => r.Nombre)
            .ToListAsync();

        return View(rutas);
    }

    // GET: /Sitio/Eventos?tipo=Fiesta%20patronal&anteriores=true
    public async Task<IActionResult> Eventos(string? tipo, bool anteriores = false)
    {
        var consulta = anteriores
            ? EventosPublicados().Where(e => (e.FechaFin ?? e.FechaInicio) < DateTime.Today)
            : EventosProximos();

        if (!string.IsNullOrWhiteSpace(tipo))
            consulta = consulta.Where(e => e.Tipo == tipo);

        consulta = anteriores
            ? consulta.OrderByDescending(e => e.FechaInicio)
            : consulta.OrderBy(e => e.FechaInicio);

        var modelo = new EventosViewModel
        {
            Eventos = await consulta.ToListAsync(),
            Tipos = await EventosPublicados().Select(e => e.Tipo).Distinct().OrderBy(t => t).ToListAsync(),
            Tipo = tipo,
            Anteriores = anteriores
        };

        return View(modelo);
    }

    // GET: /Sitio/Evento/5
    public async Task<IActionResult> Evento(int? id)
    {
        if (id == null) return NotFound();

        // El personal puede ver un evento aún no publicado para revisarlo
        var consulta = RolesSistema.EsPersonal(User)
            ? _context.Eventos.AsNoTracking().Include(e => e.Lugar)
            : EventosPublicados();

        var evento = await consulta.FirstOrDefaultAsync(e => e.IdEvento == id);
        if (evento == null) return NotFound();

        var modelo = new EventoViewModel
        {
            Evento = evento,
            Otros = await EventosProximos()
                .Where(e => e.IdEvento != evento.IdEvento)
                .OrderBy(e => e.FechaInicio)
                .Take(3)
                .ToListAsync()
        };

        return View(modelo);
    }

    // GET: /Sitio/Calendario/5  -> archivo .ics para agregar el evento al calendario del teléfono
    public async Task<IActionResult> Calendario(int id)
    {
        var evento = await EventosPublicados().FirstOrDefaultAsync(e => e.IdEvento == id);
        if (evento == null) return NotFound();

        var diaCompleto = evento.FechaInicio.TimeOfDay == TimeSpan.Zero &&
                          (evento.FechaFin == null || evento.FechaFin.Value.TimeOfDay == TimeSpan.Zero);

        var lineas = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Turismo Jimani//Eventos//ES",
            "CALSCALE:GREGORIAN",
            "BEGIN:VEVENT",
            $"UID:evento-{evento.IdEvento}@turismo-jimani",
            $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}"
        };

        if (diaCompleto)
        {
            var fin = (evento.FechaFin ?? evento.FechaInicio).Date.AddDays(1);
            lineas.Add($"DTSTART;VALUE=DATE:{evento.FechaInicio:yyyyMMdd}");
            lineas.Add($"DTEND;VALUE=DATE:{fin:yyyyMMdd}");
        }
        else
        {
            var fin = evento.FechaFin ?? evento.FechaInicio.AddHours(2);
            lineas.Add($"DTSTART:{evento.FechaInicio:yyyyMMdd'T'HHmmss}");
            lineas.Add($"DTEND:{fin:yyyyMMdd'T'HHmmss}");
        }

        lineas.Add("SUMMARY:" + EscaparIcs(evento.Titulo));
        var lugar = SitioPublico.LugarEvento(evento);
        if (!string.IsNullOrWhiteSpace(lugar))
            lineas.Add("LOCATION:" + EscaparIcs(lugar + ", Jimaní, República Dominicana"));
        if (!string.IsNullOrWhiteSpace(evento.Resumen))
            lineas.Add("DESCRIPTION:" + EscaparIcs(evento.Resumen));
        lineas.Add("URL:" + Url.Action(nameof(Evento), "Sitio", new { id = evento.IdEvento }, Request.Scheme));
        lineas.Add("END:VEVENT");
        lineas.Add("END:VCALENDAR");

        var contenido = string.Join("\r\n", lineas.Select(PlegarLineaIcs)) + "\r\n";
        return File(Encoding.UTF8.GetBytes(contenido), "text/calendar; charset=utf-8", $"evento-{evento.IdEvento}.ics");
    }

    private static string EscaparIcs(string texto) => texto
        .Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
        .Replace("\r\n", "\\n").Replace("\n", "\\n");

    // Las líneas de un .ics no deben pasar de 75 caracteres: las más largas continúan con un espacio
    private static string PlegarLineaIcs(string linea)
    {
        if (linea.Length <= 73) return linea;
        var partes = new List<string>();
        for (var i = 0; i < linea.Length; i += 73)
            partes.Add(linea.Substring(i, Math.Min(73, linea.Length - i)));
        return string.Join("\r\n ", partes);
    }

    // GET: /Sitio/Contacto
    public IActionResult Contacto()
    {
        return View();
    }
}
