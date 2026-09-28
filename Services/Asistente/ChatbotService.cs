using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using AppDonnyCuevas20210074.Models.Asistente;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AppDonnyCuevas20210074.Services.Asistente;

public static class NombresIntencion
{
    public const string BuscarAlojamiento = "BuscarAlojamiento";
    public const string BuscarRestaurante = "BuscarRestaurante";
    public const string BuscarAtractivo = "BuscarAtractivo";
    public const string BuscarTransporte = "BuscarTransporte";
    public const string BuscarGuia = "BuscarGuia";
    public const string BuscarServicio = "BuscarServicio";
    public const string BuscarRuta = "BuscarRuta";
    public const string ConsultarAtractivo = "ConsultarAtractivo";
    public const string ConsultarAlojamiento = "ConsultarAlojamiento";
    public const string ConsultarRestaurante = "ConsultarRestaurante";
    public const string ConsultarRuta = "ConsultarRuta";
    public const string Saludo = "Saludo";
    public const string Despedida = "Despedida";
    public const string Agradecimiento = "Agradecimiento";
    public const string Ayuda = "Ayuda";
    public const string FueraDeAlcance = "FueraDeAlcance";
}

public static class TiposRespuesta
{
    public const string Datos = "Datos";
    public const string SinResultados = "SinResultados";
    public const string Aclaracion = "Aclaracion";
    public const string Conversacional = "Conversacional";
    public const string NoReconocida = "NoReconocida";
    public const string ErrorIA = "ErrorIA";
    public const string ErrorBD = "ErrorBD";
    public const string Error = "Error";
}

// Orquesta el flujo: BERT (intención) -> SQL Server (datos) -> respuesta en texto.
// Nunca inventa información: todo lo que menciona sale de la base de datos TurismoJimani.
// Hay dos tipos de fichas: lugares turísticos (Lugares/Atractivos) y prestadores de servicios
// (personas y establecimientos: hoteles, restaurantes, choferes, guías, comercios...).
public class ChatbotService
{
    private const int MaximoResultados = 8;
    private static readonly TimeSpan DuracionContexto = TimeSpan.FromMinutes(30);
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    public const string MensajeNoReconocido =
        "No estoy seguro de lo que buscas. Puedes preguntarme por atractivos, alojamientos, lugares para comer, " +
        "transporte, guías turísticos, otros servicios o rutas.";

    private static readonly string[] TiposFuenteLugar = { "Lugar", "Atractivo" };
    private const string TipoFuentePrestador = "Prestador";

    private readonly TurismoJimaniContext _context;
    private readonly IClasificadorIntenciones _clasificador;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ChatbotService> _logger;
    private readonly double _umbralConfianza;

    public ChatbotService(
        TurismoJimaniContext context,
        IClasificadorIntenciones clasificador,
        IMemoryCache cache,
        ILogger<ChatbotService> logger,
        IConfiguration configuracion)
    {
        _context = context;
        _clasificador = clasificador;
        _cache = cache;
        _logger = logger;
        _umbralConfianza = configuracion.GetValue("ServicioBert:UmbralConfianza", 0.5);
    }

    public async Task<RespuestaChat> ProcesarAsync(string mensaje, Guid idSesion, CancellationToken ct = default)
    {
        var reloj = Stopwatch.StartNew();
        var contexto = _cache.GetOrCreate($"chat:{idSesion}", entrada =>
        {
            entrada.SlidingExpiration = DuracionContexto;
            return new ContextoConversacion();
        })!;

        var respuesta = new RespuestaChat { IdSesion = idSesion };

        try
        {
            // 1. BERT detecta la intención
            var clasificacion = await _clasificador.ClasificarAsync(mensaje, ct);
            respuesta.Intencion = clasificacion.Intencion;
            respuesta.Confianza = Math.Round(clasificacion.Confianza, 4);

            // 2 y 3. Se consulta SQL Server según la intención y se arma la respuesta
            var consulta = new ConsultaChat(mensaje, TextoChat.Normalizar(mensaje), clasificacion, contexto);
            await ResolverAsync(consulta, respuesta, ct);
        }
        catch (ServicioIaException ex)
        {
            _logger.LogWarning(ex, "Fallo al consultar el servicio BERT");
            respuesta.Intencion = null;
            respuesta.TipoRespuesta = TiposRespuesta.ErrorIA;
            respuesta.Respuesta = ex.EsTimeout
                ? "El asistente tardó demasiado en responder. Intenta de nuevo en un momento."
                : "El servicio de inteligencia artificial no está disponible en este momento. Intenta de nuevo en unos minutos.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (EsErrorBaseDatos(ex))
        {
            _logger.LogError(ex, "Error de base de datos en el asistente");
            Responder(respuesta, TiposRespuesta.ErrorBD,
                "Tuve un problema al consultar la información turística. Intenta de nuevo en unos minutos.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado en el asistente");
            Responder(respuesta, TiposRespuesta.Error, "Ocurrió un error inesperado. Intenta de nuevo.");
        }

        reloj.Stop();
        respuesta.IdConsulta = await RegistrarConsultaAsync(mensaje, respuesta, (int)reloj.ElapsedMilliseconds);
        return respuesta;
    }

    // ------------------------------------------------------------------ enrutamiento por intención

    private async Task ResolverAsync(ConsultaChat c, RespuestaChat r, CancellationToken ct)
    {
        var fichas = await CargarFichasAsync(ct);
        var mencionada = TextoChat.BuscarPorNombre(c.Normalizado, fichas, f => f.Nombre);
        var intencion = c.Intencion;

        if (TextoChat.EsSoloReferencia(c.Normalizado) && c.Contexto.Dominio is { } dominioAnterior
            && (c.Contexto.UltimosLugares.Count > 0 || c.Contexto.UltimasRutas.Count > 0))
        {
            // Seguimiento corto como "el 2" sobre la última lista mostrada
            intencion = IntencionConsultar(dominioAnterior);
        }
        else if (c.Confianza < _umbralConfianza)
        {
            // BERT no está seguro: si el mensaje nombra un lugar o prestador registrado se da su información
            if (mencionada is null)
            {
                Responder(r, TiposRespuesta.NoReconocida, MensajeNoReconocido);
                c.Contexto.UltimaIntencion = null;
                return;
            }
            intencion = NombresIntencion.ConsultarAtractivo;
        }
        else if (intencion == NombresIntencion.FueraDeAlcance && mencionada is not null)
        {
            // "¿Cómo es Las Caritas?" o "¿quién es Raúl Pérez?": si nombra una ficha registrada, se describe
            intencion = NombresIntencion.ConsultarAtractivo;
        }

        r.Intencion = intencion;

        switch (intencion)
        {
            case NombresIntencion.Saludo:
                Responder(r, TiposRespuesta.Conversacional,
                    "¡Hola! Soy el asistente turístico de Jimaní. Puedo ayudarte a encontrar atractivos, alojamientos, " +
                    "lugares para comer, transporte, guías turísticos, otros servicios y rutas. ¿Qué te gustaría saber?");
                break;

            case NombresIntencion.Despedida:
                Responder(r, TiposRespuesta.Conversacional, "¡Hasta luego! Espero que disfrutes tu visita a Jimaní.");
                break;

            case NombresIntencion.Agradecimiento:
                Responder(r, TiposRespuesta.Conversacional, "¡Con gusto! ¿Hay algo más en lo que te pueda ayudar?");
                break;

            case NombresIntencion.Ayuda:
                await AyudaAsync(r, ct);
                break;

            case NombresIntencion.BuscarAlojamiento:
                await BuscarPrestadoresAsync(c, r, fichas, mencionada, GruposPrestador.Alojamiento, ct);
                break;

            case NombresIntencion.BuscarRestaurante:
                await BuscarPrestadoresAsync(c, r, fichas, mencionada, GruposPrestador.Gastronomia, ct);
                break;

            case NombresIntencion.BuscarTransporte:
                await BuscarPrestadoresAsync(c, r, fichas, mencionada, GruposPrestador.Transporte, ct);
                break;

            case NombresIntencion.BuscarGuia:
                await BuscarPrestadoresAsync(c, r, fichas, mencionada, GruposPrestador.Guia, ct);
                break;

            case NombresIntencion.BuscarServicio:
                await BuscarServiciosAsync(c, r, fichas, mencionada, ct);
                break;

            case NombresIntencion.BuscarAtractivo:
                await BuscarAtractivosAsync(c, r, fichas, mencionada, ct);
                break;

            case NombresIntencion.BuscarRuta:
                await BuscarRutasAsync(c, r, fichas, mencionada, ct);
                break;

            case NombresIntencion.ConsultarRuta:
                await ConsultarRutaAsync(c, r, mencionada, ct);
                break;

            case NombresIntencion.ConsultarAtractivo:
                await ConsultarFichaAsync(c, r, fichas, mencionada, Dominio.Atractivo, ct);
                break;

            case NombresIntencion.ConsultarAlojamiento:
                await ConsultarFichaAsync(c, r, fichas, mencionada, Dominio.Alojamiento, ct);
                break;

            case NombresIntencion.ConsultarRestaurante:
                await ConsultarFichaAsync(c, r, fichas, mencionada, Dominio.Restaurante, ct);
                break;

            default: // FueraDeAlcance o una intención nueva sin lógica asociada
                Responder(r, TiposRespuesta.NoReconocida, MensajeNoReconocido);
                break;
        }

        c.Contexto.UltimaIntencion = intencion;
    }

    // ------------------------------------------------------------------ búsquedas de prestadores

    // Textos de cada grupo de prestadores: (plural, introducción, pregunta final, sin resultados)
    private static (string Plural, string Intro, string Pregunta, string SinResultados) TextosGrupo(string grupo) => grupo switch
    {
        GruposPrestador.Alojamiento => ("alojamientos",
            "Claro. Estos son algunos alojamientos registrados en Jimaní:",
            "¿Quieres que te muestre información sobre alguno?",
            "No encontré alojamientos registrados en este momento."),
        GruposPrestador.Gastronomia => ("lugares para comer",
            "Claro. Estos son algunos lugares para comer registrados en Jimaní:",
            "¿Quieres que te muestre información sobre alguno?",
            "No encontré restaurantes ni lugares para comer registrados en este momento."),
        GruposPrestador.Transporte => ("servicios de transporte",
            "Estos son los servicios de transporte registrados:",
            "¿Quieres más información sobre alguno?",
            "No encontré servicios de transporte registrados en este momento."),
        GruposPrestador.Guia => ("guías turísticos",
            "Estos son los guías turísticos registrados en Jimaní:",
            "¿Quieres más información sobre alguno?",
            "No encontré guías turísticos registrados en este momento."),
        GruposPrestador.Comercio => ("comercios",
            "Estos son los comercios registrados en Jimaní:",
            "¿Quieres más información sobre alguno?",
            "No encontré comercios registrados en este momento."),
        _ => ("servicios",
            "Estos son los servicios registrados en Jimaní:",
            "¿Quieres más información sobre alguno?",
            "No encontré servicios registrados en este momento.")
    };

    // Prestadores publicados. Los filtros se aplican sobre esta consulta (la entidad) y después se proyecta:
    // EF Core no puede traducir a SQL un Where sobre las propiedades de un record ya construido.
    private IQueryable<Prestador> PrestadoresActivos() => _context.Prestadores.AsNoTracking().Where(p => p.Activo);

    private static IQueryable<FilaPrestador> Proyectar(IQueryable<Prestador> consulta) =>
        consulta.Select(p => new FilaPrestador(
            p.IdPrestador, p.Nombre, p.TipoPrestador!.Nombre, p.TipoPrestador.Grupo, p.TipoPrestador.Icono, p.Especialidad,
            p.PrecioDesde, p.PrecioHasta, p.InformacionPrecio, p.ZonaCobertura, p.HorarioAtencion,
            p.WhatsApp, p.Telefono, p.ServicioDomicilio, p.RequiereReserva, p.Destacado,
            p.Imagenes.OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.OrdenVisualizacion).Select(i => i.UrlImagen).FirstOrDefault()));

    private async Task BuscarPrestadoresAsync(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas,
        FichaResumen? mencionada, string grupo, CancellationToken ct)
    {
        var textos = TextosGrupo(grupo);
        var prestadores = await Proyectar(PrestadoresActivos().Where(p => p.TipoPrestador!.Grupo == grupo)).ToListAsync(ct);

        if (prestadores.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, textos.SinResultados);
            return;
        }

        var intro = textos.Intro;

        // Filtro por tipo o especialidad mencionados ("comida criolla", "motoconcho", "habla inglés"...)
        var porFiltro = FiltrarPorPalabras(c, prestadores);
        if (porFiltro.Count > 0 && porFiltro.Count < prestadores.Count)
        {
            prestadores = porFiltro;
            intro = $"Estos son los {textos.Plural} registrados que coinciden con lo que buscas:";
        }

        if (TextoChat.PideDomicilio(c.Normalizado) && grupo is GruposPrestador.Gastronomia or GruposPrestador.Comercio)
        {
            prestadores = prestadores.Where(p => p.ServicioDomicilio).ToList();
            if (prestadores.Count == 0)
            {
                Responder(r, TiposRespuesta.SinResultados, $"No encontré {textos.Plural} registrados con servicio a domicilio.");
                return;
            }
            intro = $"Estos {textos.Plural} registrados ofrecen servicio a domicilio:";
        }

        var elementos = prestadores
            .OrderByDescending(p => p.Destacado).ThenBy(p => p.Nombre)
            .Select(p => ElementoPrestador(p, IncluirContacto(grupo)))
            .ToList();

        if (TextoChat.PideMasBarato(c.Normalizado))
        {
            elementos = elementos.OrderBy(e => e.Precio ?? decimal.MaxValue).ThenBy(e => e.Nombre).ToList();
            intro = $"Estos son los {textos.Plural} registrados, del más económico al más caro:";
        }
        else if (TextoChat.PideMasCaro(c.Normalizado))
        {
            elementos = elementos.OrderByDescending(e => e.Precio ?? decimal.MinValue).ThenBy(e => e.Nombre).ToList();
            intro = $"Estos son los {textos.Plural} registrados, del más caro al más económico:";
        }

        ResponderLista(c, r, fichas, mencionada, DominioDeGrupo(grupo), elementos, intro, textos.Plural, textos.Pregunta);
    }

    // "¿Hay barbero?", "necesito un mecánico", "¿dónde compro artesanía?": busca en todos los prestadores
    private async Task BuscarServiciosAsync(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas,
        FichaResumen? mencionada, CancellationToken ct)
    {
        var prestadores = await Proyectar(PrestadoresActivos()).ToListAsync(ct);

        if (prestadores.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, "Todavía no hay prestadores de servicios registrados.");
            return;
        }

        var filtro = TextoChat.PalabrasFiltro(c.Normalizado);

        if (filtro.Count == 0)
        {
            // "Prestadores de servicio", "¿qué servicios hay?": se muestran todos (primero los destacados)
            var todos = prestadores
                .OrderByDescending(p => p.Destacado).ThenBy(p => p.Nombre)
                .Select(p => ElementoPrestador(p, true))
                .ToList();

            ResponderLista(c, r, fichas, mencionada, Dominio.Servicio, todos,
                "Estos son los prestadores de servicios registrados en Jimaní:", "prestadores",
                "También puedes pedirme uno en específico, por ejemplo «chofer», «guía» o «barbero».");
            return;
        }

        var coincidencias = FiltrarPorPalabras(c, prestadores);

        if (coincidencias.Count == 0)
        {
            // Sin coincidencias: se muestran los tipos de servicio disponibles
            var tipos = prestadores
                .GroupBy(p => p.Tipo)
                .OrderBy(g => g.Key)
                .Select(g => $"• {g.Key} ({g.Count()})");

            Responder(r, TiposRespuesta.SinResultados,
                "No encontré prestadores registrados para lo que buscas. Estos son los servicios registrados en Jimaní:\n" + string.Join("\n", tipos) +
                "\n\n¿Cuál te interesa? Por ejemplo, escribe «" + prestadores.OrderBy(p => p.Tipo).First().Tipo.Split('/')[0].Trim().ToLower() + "».");
            return;
        }

        // Si todos son del mismo grupo, la lista se recuerda como ese dominio (para "el más barato", etc.)
        var grupos = coincidencias.Select(p => p.Grupo).Distinct().ToList();
        var dominio = grupos.Count == 1 ? DominioDeGrupo(grupos[0]) : Dominio.Servicio;

        var elementos = coincidencias
            .OrderByDescending(p => p.Destacado).ThenBy(p => p.Nombre)
            .Select(p => ElementoPrestador(p, true))
            .ToList();

        ResponderLista(c, r, fichas, mencionada, dominio, elementos,
            "Estos son los prestadores registrados que ofrecen lo que buscas:", "prestadores",
            "¿Quieres más información sobre alguno? Escribe su nombre o su número.");
    }

    // Prestadores cuyo tipo, especialidad o nombre coincide con alguna palabra distintiva del mensaje
    private static List<FilaPrestador> FiltrarPorPalabras(ConsultaChat c, List<FilaPrestador> prestadores)
    {
        var filtro = TextoChat.PalabrasFiltro(c.Normalizado);
        if (filtro.Count == 0) return new List<FilaPrestador>();

        return prestadores
            .Where(p => new[] { p.Tipo, p.Especialidad }
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .SelectMany(e => TextoChat.PalabrasClave(TextoChat.Normalizar(e!)))
                .Any(t => filtro.Any(f => TextoChat.CoincideRaiz(f, t))))
            .ToList();
    }

    // A las personas (choferes, guías, barberos...) se les llama: su contacto va en la lista
    private static bool IncluirContacto(string grupo) =>
        grupo is GruposPrestador.Transporte or GruposPrestador.Guia or GruposPrestador.Servicio;

    private static ElementoLista ElementoPrestador(FilaPrestador p, bool conContacto) => new(
        RefFicha.Prestador(p.Id), p.Nombre,
        Unir(". ",
            Unir(", ", p.Tipo, p.Especialidad),
            TextoPrecio(p.PrecioDesde, p.PrecioHasta) is { } precio ? $"Precio: {precio}" : (string.IsNullOrWhiteSpace(p.InformacionPrecio) ? null : $"Precio: {p.InformacionPrecio}"),
            string.IsNullOrWhiteSpace(p.ZonaCobertura) ? null : $"Zona: {p.ZonaCobertura}",
            p.ServicioDomicilio ? "Servicio a domicilio" : null,
            conContacto && !string.IsNullOrWhiteSpace(p.Contacto) ? $"Contacto: {p.Contacto}" : null),
        p.PrecioDesde ?? p.PrecioHasta)
    {
        Subtitulo = p.Tipo,
        Imagen = p.Imagen,
        Icono = IconoPrestador(p.IconoTipo, p.Grupo),
        WhatsApp = p.WhatsApp,
        Telefono = p.Telefono
    };

    private static string IconoPrestador(string? iconoTipo, string? grupo) =>
        !string.IsNullOrWhiteSpace(iconoTipo) ? iconoTipo : GruposPrestador.Icono(grupo);

    // ------------------------------------------------------------------ búsquedas de lugares y rutas

    private async Task BuscarAtractivosAsync(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas,
        FichaResumen? mencionada, CancellationToken ct)
    {
        var atractivos = await _context.Atractivos.AsNoTracking()
            .Where(a => a.Lugar!.Activo)
            .Select(a => new
            {
                a.IdLugar,
                a.Lugar!.Nombre,
                a.TipoAtractivo,
                a.NivelDificultad,
                a.Destacado,
                Categorias = a.Lugar.Categorias.Where(cat => cat.Activo).Select(cat => cat.Nombre).ToList(),
                Imagen = a.Lugar.Imagenes.OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.OrdenVisualizacion).Select(i => i.UrlImagen).FirstOrDefault()
            })
            .ToListAsync(ct);

        if (atractivos.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré atractivos registrados en este momento.");
            return;
        }

        var intro = "Claro. Estos son algunos atractivos registrados en Jimaní:";

        // Filtro por tipo de atractivo o categoría mencionados ("naturales", "arqueológicos"...)
        var filtro = TextoChat.PalabrasFiltro(c.Normalizado);
        var filtrados = atractivos
            .Where(a => new[] { a.TipoAtractivo }.Concat(a.Categorias)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .SelectMany(e => TextoChat.PalabrasClave(TextoChat.Normalizar(e!)))
                .Any(t => filtro.Any(f => TextoChat.CoincideRaiz(f, t))))
            .ToList();

        if (filtrados.Count > 0)
        {
            atractivos = filtrados;
            intro = "Estos son los atractivos registrados que coinciden con lo que buscas:";
        }

        if (TextoChat.PideDestacados(c.Normalizado) && atractivos.Any(a => a.Destacado))
        {
            atractivos = atractivos.Where(a => a.Destacado).ToList();
            intro = "Estos son los atractivos destacados registrados en Jimaní:";
        }

        var elementos = atractivos
            .OrderByDescending(a => a.Destacado).ThenBy(a => a.Nombre)
            .Select(a => new ElementoLista(RefFicha.Lugar(a.IdLugar), a.Nombre,
                Unir(", ", a.TipoAtractivo, string.IsNullOrWhiteSpace(a.NivelDificultad) ? null : $"dificultad {a.NivelDificultad.ToLower()}"),
                null)
            {
                Subtitulo = a.TipoAtractivo ?? "Atractivo",
                Imagen = a.Imagen,
                Icono = "fa-mountain"
            })
            .ToList();

        ResponderLista(c, r, fichas, mencionada, Dominio.Atractivo, elementos, intro,
            "atractivos", "¿Quieres que te dé más información sobre alguno?");
    }

    private async Task BuscarRutasAsync(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas,
        FichaResumen? mencionada, CancellationToken ct)
    {
        // "¿Cómo llego ahí?" usa la ficha de la que se estaba hablando
        var destino = mencionada;
        if (destino is null && c.Contexto.LugarEnFoco is { } enFoco && TextoChat.TieneReferencia(c.Normalizado))
            destino = fichas.FirstOrDefault(f => f.Ref == enFoco);

        // Las rutas unen lugares turísticos; para un prestador se da su ubicación
        if (destino is { Ref.EsPrestador: true })
        {
            await DescribirUbicacionAsync(c, r, destino, ct);
            return;
        }

        var consulta = _context.Rutas.AsNoTracking().Where(x => x.Activa);
        if (destino is not null)
            consulta = consulta.Where(x => x.IdLugarDestino == destino.Ref.Id || x.IdLugarOrigen == destino.Ref.Id);

        var rutas = await ProyectarRutas(consulta).ToListAsync(ct);

        if (rutas.Count == 0 && destino is not null)
        {
            await DescribirUbicacionAsync(c, r, destino, ct);
            return;
        }

        if (rutas.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré rutas registradas en este momento.");
            return;
        }

        if (rutas.Count == 1)
        {
            DescribirRuta(c, r, rutas[0]);
            return;
        }

        var mostradas = rutas.OrderBy(x => x.Nombre).Take(MaximoResultados).ToList();
        var texto = new StringBuilder(destino is null
            ? "Estas son las rutas registradas:\n"
            : $"Estas son las rutas registradas relacionadas con {destino.Nombre}:\n");

        for (var i = 0; i < mostradas.Count; i++)
            texto.Append($"{i + 1}. {mostradas[i].Nombre}{ResumenRuta(mostradas[i])}\n");

        if (rutas.Count > mostradas.Count)
            texto.Append($"…y {rutas.Count - mostradas.Count} más.\n");

        texto.Append("\n¿Quieres los detalles de alguna? Por ejemplo, escribe «la 1».");

        c.Contexto.RecordarRutas(mostradas.Select(x => x.IdRuta));
        r.Resultados.AddRange(mostradas.Select(x => new ResultadoChat(x.IdRuta, x.Nombre, "Ruta", ResumenRuta(x).Trim(' ', '—'))));
        Responder(r, TiposRespuesta.Datos, texto.ToString().TrimEnd(), limpiarResultados: false);
    }

    // ------------------------------------------------------------------ consultas de detalle

    private async Task ConsultarFichaAsync(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas,
        FichaResumen? mencionada, Dominio dominioIntencion, CancellationToken ct)
    {
        // "¿Cuál tiene menor precio?" sobre la última lista mostrada
        var barato = TextoChat.PideMasBarato(c.Normalizado);
        if (mencionada is null && (barato || TextoChat.PideMasCaro(c.Normalizado)))
        {
            var dominio = c.Contexto.Dominio is Dominio.Alojamiento or Dominio.Restaurante or Dominio.Transporte or Dominio.Guia or Dominio.Servicio
                ? c.Contexto.Dominio.Value
                : dominioIntencion;

            if (dominio != Dominio.Atractivo)
            {
                await CompararPreciosAsync(c, r, dominio, barato, ct);
                return;
            }
        }

        // "¿Cuál tiene servicio a domicilio?" filtra los lugares para comer
        if (mencionada is null && TextoChat.PideDomicilio(c.Normalizado)
            && (dominioIntencion == Dominio.Restaurante || c.Contexto.Dominio == Dominio.Restaurante))
        {
            await BuscarPrestadoresAsync(c, r, fichas, null, GruposPrestador.Gastronomia, ct);
            return;
        }

        var objetivo = mencionada ?? ResolverReferencia(c, fichas);

        if (objetivo is null)
        {
            PedirAclaracion(c, r, fichas, dominioIntencion);
            return;
        }

        if (objetivo.Ref.EsPrestador)
            await DescribirPrestadorAsync(c, r, objetivo.Ref.Id, ct);
        else
            await DescribirLugarAsync(c, r, objetivo.Ref.Id, ct);
    }

    private static FichaResumen? ResolverReferencia(ConsultaChat c, List<FichaResumen> fichas)
    {
        var ctx = c.Contexto;
        var ordinal = TextoChat.ObtenerOrdinal(c.Normalizado, ctx.UltimosLugares.Count);

        RefFicha? referencia = null;
        if (ordinal is { } indice)
            referencia = ctx.UltimosLugares[indice];
        else if (ctx.EnfoqueReciente && ctx.LugarEnFoco is { } enFoco)
            referencia = enFoco;
        else if (ctx.UltimosLugares.Count == 1)
            referencia = ctx.UltimosLugares[0];
        else if (ctx.UltimosLugares.Count == 0 && ctx.LugarEnFoco is { } anterior)
            referencia = anterior;

        return referencia is null ? null : fichas.FirstOrDefault(f => f.Ref == referencia);
    }

    private static void PedirAclaracion(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas, Dominio dominio)
    {
        // Si hay una lista reciente con varios elementos, se pregunta cuál
        if (c.Contexto.UltimosLugares.Count > 1 && !c.Contexto.EnfoqueReciente)
        {
            var opciones = c.Contexto.UltimosLugares
                .Select(referencia => fichas.FirstOrDefault(f => f.Ref == referencia)?.Nombre)
                .Where(n => n != null).Take(3).ToList();

            Responder(r, TiposRespuesta.Aclaracion,
                $"¿Sobre cuál quieres información? Puedes escribir su nombre o su número, por ejemplo «el 1» ({string.Join(", ", opciones)}).");
            return;
        }

        var ejemplos = fichas.Where(f => f.Dominio == dominio).Select(f => f.Nombre).OrderBy(n => n).Take(3).ToList();
        var tipo = NombrePlural(dominio);

        if (ejemplos.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, $"No encontré {tipo} registrados en este momento.");
            return;
        }

        Responder(r, TiposRespuesta.Aclaracion,
            $"¿Sobre cuál quieres información? Por ejemplo: {string.Join(", ", ejemplos)}. " +
            $"También puedo mostrarte la lista de {tipo} registrados.");
    }

    private async Task CompararPreciosAsync(ConsultaChat c, RespuestaChat r, Dominio dominio, bool barato, CancellationToken ct)
    {
        var grupo = GrupoDeDominio(dominio);
        var ids = c.Contexto.Dominio == dominio
            ? c.Contexto.UltimosLugares.Where(x => x.EsPrestador).Select(x => x.Id).ToList()
            : new List<int>();

        var consulta = PrestadoresActivos().Where(p => p.PrecioDesde != null || p.PrecioHasta != null);
        if (ids.Count > 0)
            consulta = consulta.Where(p => ids.Contains(p.IdPrestador));
        else if (grupo != null)
            consulta = consulta.Where(p => p.TipoPrestador!.Grupo == grupo);

        var candidatos = await Proyectar(consulta).ToListAsync(ct);
        var plural = NombrePlural(dominio);

        if (candidatos.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, $"No tengo precios registrados para esos {plural}.");
            return;
        }

        var elegido = barato
            ? candidatos.OrderBy(p => p.PrecioDesde ?? p.PrecioHasta).First()
            : candidatos.OrderByDescending(p => p.PrecioHasta ?? p.PrecioDesde).First();

        var entre = ids.Count > 0 ? " entre los que te mostré" : " registrado";
        var precio = TextoPrecio(elegido.PrecioDesde, elegido.PrecioHasta);
        Responder(r, TiposRespuesta.Datos,
            $"El de {(barato ? "menor" : "mayor")} precio{entre} es {elegido.Nombre} ({elegido.Tipo}), " +
            $"con precio {precio}.\n\n¿Quieres más información?");

        c.Contexto.RecordarLugar(dominio, RefFicha.Prestador(elegido.Id));
        r.Resultados.Add(Tarjeta(ElementoPrestador(elegido, true)));
    }

    private async Task DescribirLugarAsync(ConsultaChat c, RespuestaChat r, int idLugar, CancellationToken ct)
    {
        var lugar = await _context.Lugares.AsNoTracking()
            .Include(l => l.Atractivo)
            .Include(l => l.Categorias)
            .Include(l => l.Servicios)
            .Include(l => l.Horarios)
            .Include(l => l.Contactos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.IdLugar == idLugar && l.Activo, ct);

        if (lugar is null)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré información registrada sobre ese lugar.");
            return;
        }

        var documentos = await _context.DocumentosConocimiento.AsNoTracking()
            .Where(d => d.Activo && d.IdReferencia == idLugar && (d.TipoFuente == null || TiposFuenteLugar.Contains(d.TipoFuente)))
            .Select(d => d.Contenido)
            .ToListAsync(ct);

        var texto = new StringBuilder();
        texto.AppendLine(lugar.Nombre);

        if (!string.IsNullOrWhiteSpace(lugar.DescripcionCorta))
            texto.AppendLine(lugar.DescripcionCorta.Trim());
        if (!string.IsNullOrWhiteSpace(lugar.Descripcion) && lugar.Descripcion.Trim() != lugar.DescripcionCorta?.Trim())
            texto.AppendLine(TextoChat.Recortar(lugar.Descripcion, 700));

        var datos = new List<string>();
        var parrafos = new List<string>();

        if (lugar.Atractivo is { } a)
        {
            Agregar(datos, "Tipo", a.TipoAtractivo);
            Agregar(datos, "Dificultad", a.NivelDificultad);
            Agregar(datos, "Duración de la visita", TextoDuracion(a.DuracionVisitaMinutos));
            Agregar(datos, "Estado de conservación", a.EstadoConservacion);
            Agregar(parrafos, "Información natural", TextoChat.Recortar(a.InformacionNatural, 400));
            Agregar(parrafos, "Información cultural", TextoChat.Recortar(a.InformacionCultural, 400));
            Agregar(parrafos, "Cómo acceder", TextoChat.Recortar(a.InformacionAcceso, 400));
        }

        Agregar(datos, "Ubicación", Unir(", ", lugar.Direccion, lugar.Municipio, lugar.Provincia));

        if (lugar.Horarios.Count > 0)
        {
            Agregar(datos, "Horario de visita", string.Join("; ", lugar.Horarios.OrderBy(x => x.DiaSemana).Select(x =>
                $"{Formato.DiaSemana(x.DiaSemana)}: {(x.Cerrado ? "cerrado" : TextoHorario(x.HoraApertura, x.HoraCierre) ?? "abierto")}")));
        }

        var contactos = lugar.Contactos.OrderByDescending(x => x.EsPrincipal).Select(x => $"{x.TipoContacto}: {x.ValorContacto}").ToList();
        if (!string.IsNullOrWhiteSpace(lugar.Telefono)) contactos.Add($"Teléfono: {lugar.Telefono}");
        if (!string.IsNullOrWhiteSpace(lugar.Correo)) contactos.Add($"Correo: {lugar.Correo}");
        if (!string.IsNullOrWhiteSpace(lugar.SitioWeb)) contactos.Add($"Sitio web: {lugar.SitioWeb}");
        Agregar(datos, "Contacto", string.Join("; ", contactos.Distinct()));

        Agregar(datos, "Categorías", string.Join(", ", lugar.Categorias.Where(x => x.Activo).Select(x => x.Nombre)));
        Agregar(datos, "Comodidades", string.Join(", ", lugar.Servicios.Where(x => x.Activo).Select(x => x.Nombre)));

        if (datos.Count > 0)
            texto.AppendLine().Append(string.Join("\n", datos.Select(d => "• " + d))).AppendLine();

        foreach (var parrafo in parrafos.Concat(documentos.Select(d => TextoChat.Recortar(d, 600))))
            texto.AppendLine().AppendLine(parrafo);

        texto.AppendLine().Append("¿Quieres saber cómo llegar, buscar un guía o ver qué otros lugares hay cerca?");

        var foto = await _context.Imagenes.AsNoTracking()
            .Where(i => i.IdLugar == lugar.IdLugar)
            .OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.OrdenVisualizacion)
            .Select(i => i.UrlImagen).FirstOrDefaultAsync(ct);

        c.Contexto.RecordarLugar(Dominio.Atractivo, RefFicha.Lugar(lugar.IdLugar));
        r.Resultados.Add(Tarjeta(new ElementoLista(RefFicha.Lugar(lugar.IdLugar), lugar.Nombre, lugar.DescripcionCorta, null)
        {
            Subtitulo = lugar.Atractivo?.TipoAtractivo ?? (lugar.Atractivo != null ? "Atractivo" : "Lugar de interés"),
            Imagen = foto,
            Icono = lugar.Atractivo != null ? "fa-mountain" : "fa-map-marker-alt",
            Telefono = lugar.Telefono
        }));
        Responder(r, TiposRespuesta.Datos, texto.ToString().Trim(), limpiarResultados: false);
    }

    private async Task DescribirPrestadorAsync(ConsultaChat c, RespuestaChat r, int idPrestador, CancellationToken ct)
    {
        var p = await _context.Prestadores.AsNoTracking()
            .Include(x => x.TipoPrestador)
            .Include(x => x.Servicios)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.IdPrestador == idPrestador && x.Activo, ct);

        if (p is null)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré información registrada sobre ese servicio.");
            return;
        }

        var documentos = await _context.DocumentosConocimiento.AsNoTracking()
            .Where(d => d.Activo && d.IdReferencia == idPrestador && d.TipoFuente == TipoFuentePrestador)
            .Select(d => d.Contenido)
            .ToListAsync(ct);

        var grupo = p.TipoPrestador?.Grupo;
        var dominio = DominioDeGrupo(grupo);

        var texto = new StringBuilder();
        texto.AppendLine($"{p.Nombre} — {p.TipoPrestador?.Nombre}");

        if (!string.IsNullOrWhiteSpace(p.DescripcionCorta))
            texto.AppendLine(p.DescripcionCorta.Trim());
        if (!string.IsNullOrWhiteSpace(p.Descripcion) && p.Descripcion.Trim() != p.DescripcionCorta?.Trim())
            texto.AppendLine(TextoChat.Recortar(p.Descripcion, 700));

        var datos = new List<string>();
        Agregar(datos, GruposPrestador.EtiquetaEspecialidad(grupo), p.Especialidad);
        Agregar(datos, "Precio", TextoPrecio(p.PrecioDesde, p.PrecioHasta));
        Agregar(datos, "Sobre el precio", p.InformacionPrecio);
        Agregar(datos, GruposPrestador.EtiquetaCapacidad(grupo), p.Capacidad > 0 ? p.Capacidad.ToString() : null);
        Agregar(datos, "Zona de servicio", p.ZonaCobertura);
        Agregar(datos, "Horario", p.HorarioAtencion);
        if (p.RequiereReserva) datos.Add("Requiere reserva o cita");
        if (p.ServicioDomicilio) datos.Add("Ofrece servicio a domicilio");
        Agregar(datos, "Ubicación", Unir(", ", p.Direccion, p.Municipio, p.Provincia));

        var contactos = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Telefono)) contactos.Add($"Teléfono: {p.Telefono}");
        if (!string.IsNullOrWhiteSpace(p.WhatsApp)) contactos.Add($"WhatsApp: {p.WhatsApp}");
        if (!string.IsNullOrWhiteSpace(p.Correo)) contactos.Add($"Correo: {p.Correo}");
        if (!string.IsNullOrWhiteSpace(p.RedSocial)) contactos.Add($"Redes: {p.RedSocial}");
        if (!string.IsNullOrWhiteSpace(p.SitioWeb)) contactos.Add($"Sitio web: {p.SitioWeb}");
        Agregar(datos, "Contacto", string.Join("; ", contactos));
        Agregar(datos, grupo == GruposPrestador.Gastronomia ? "Menú" : "Reservas", p.EnlaceExterno);
        Agregar(datos, "Comodidades", string.Join(", ", p.Servicios.Where(x => x.Activo).Select(x => x.Nombre)));

        if (datos.Count > 0)
            texto.AppendLine().Append(string.Join("\n", datos.Select(d => "• " + d))).AppendLine();

        foreach (var documento in documentos)
            texto.AppendLine().AppendLine(TextoChat.Recortar(documento, 600));

        texto.AppendLine().Append(grupo switch
        {
            GruposPrestador.Alojamiento => "¿Quieres ver otros alojamientos o lugares cercanos para visitar?",
            GruposPrestador.Gastronomia => "¿Quieres ver otros lugares para comer o saber cómo llegar?",
            GruposPrestador.Guia => "¿Quieres ver otros guías o los atractivos que puedes visitar?",
            _ => "¿Quieres ver otras opciones parecidas?"
        });

        var foto = await _context.Imagenes.AsNoTracking()
            .Where(i => i.IdPrestador == p.IdPrestador)
            .OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.OrdenVisualizacion)
            .Select(i => i.UrlImagen).FirstOrDefaultAsync(ct);

        c.Contexto.RecordarLugar(dominio, RefFicha.Prestador(p.IdPrestador));
        r.Resultados.Add(Tarjeta(new ElementoLista(RefFicha.Prestador(p.IdPrestador), p.Nombre, p.DescripcionCorta, null)
        {
            Subtitulo = p.TipoPrestador?.Nombre,
            Imagen = foto,
            Icono = IconoPrestador(p.TipoPrestador?.Icono, grupo),
            WhatsApp = p.WhatsApp,
            Telefono = p.Telefono
        }));
        Responder(r, TiposRespuesta.Datos, texto.ToString().Trim(), limpiarResultados: false);
    }

    private async Task DescribirUbicacionAsync(ConsultaChat c, RespuestaChat r, FichaResumen destino, CancellationToken ct)
    {
        string ubicacionTexto;
        if (destino.Ref.EsPrestador)
        {
            var p = await _context.Prestadores.AsNoTracking().Where(x => x.IdPrestador == destino.Ref.Id)
                .Select(x => new { x.Direccion, x.Municipio, x.Provincia }).FirstAsync(ct);
            ubicacionTexto = Unir(", ", p.Direccion, p.Municipio, p.Provincia);
        }
        else
        {
            var l = await _context.Lugares.AsNoTracking().Where(x => x.IdLugar == destino.Ref.Id)
                .Select(x => new { x.Direccion, x.Municipio, x.Provincia }).FirstAsync(ct);
            ubicacionTexto = Unir(", ", l.Direccion, l.Municipio, l.Provincia);
        }

        var texto = new StringBuilder(destino.Ref.EsPrestador
            ? $"Esta es la ubicación registrada de {destino.Nombre}"
            : $"No tengo una ruta registrada hacia {destino.Nombre}");
        texto.Append(string.IsNullOrEmpty(ubicacionTexto)
            ? (destino.Ref.EsPrestador ? ": no tiene dirección registrada." : ".")
            : destino.Ref.EsPrestador ? $": {ubicacionTexto}." : $", pero según los datos registrados se encuentra en {ubicacionTexto}.");

        if (destino.Latitud.HasValue && destino.Longitud.HasValue)
        {
            var coordenadas = $"{destino.Latitud.Value.ToString("0.######", Invariante)},{destino.Longitud.Value.ToString("0.######", Invariante)}";
            texto.Append($"\n\nCoordenadas: {coordenadas}\nPuedes abrirlas en un mapa: https://www.google.com/maps?q={coordenadas}");
        }

        var hayTransporte = await _context.Prestadores.AnyAsync(x => x.Activo && x.TipoPrestador!.Grupo == GruposPrestador.Transporte, ct);
        if (hayTransporte)
            texto.Append("\n\nSi quieres, también puedo mostrarte los servicios de transporte registrados.");

        c.Contexto.RecordarLugar(null, destino.Ref);
        r.Resultados.Add(new ResultadoChat(destino.Ref.Id, destino.Nombre, destino.Ref.EsPrestador ? "Prestador" : "Lugar", ubicacionTexto));
        Responder(r, TiposRespuesta.Datos, texto.ToString(), limpiarResultados: false);
    }

    private async Task ConsultarRutaAsync(ConsultaChat c, RespuestaChat r, FichaResumen? mencionada, CancellationToken ct)
    {
        var rutas = await ProyectarRutas(_context.Rutas.AsNoTracking().Where(x => x.Activa)).ToListAsync(ct);

        if (rutas.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré rutas registradas en este momento.");
            return;
        }

        var ctx = c.Contexto;
        var candidatas = ctx.UltimasRutas.Count > 0
            ? rutas.Where(x => ctx.UltimasRutas.Contains(x.IdRuta)).ToList()
            : rutas;

        // Nombre de la ruta en el mensaje, número de la lista anterior o comparación
        var ruta = TextoChat.BuscarPorNombre(c.Normalizado, rutas, x => x.Nombre);

        if (ruta is null && TextoChat.ObtenerOrdinal(c.Normalizado, ctx.UltimasRutas.Count) is { } indice)
            ruta = rutas.FirstOrDefault(x => x.IdRuta == ctx.UltimasRutas[indice]);

        if (ruta is null && (c.Normalizado.Contains("corta") || c.Normalizado.Contains("menor distancia") || c.Normalizado.Contains("mas cerca")))
            ruta = candidatas.Where(x => x.DistanciaKilometros.HasValue).OrderBy(x => x.DistanciaKilometros).FirstOrDefault();

        if (ruta is null && (c.Normalizado.Contains("rapida") || c.Normalizado.Contains("menos tiempo")))
            ruta = candidatas.Where(x => x.DuracionMinutos.HasValue).OrderBy(x => x.DuracionMinutos).FirstOrDefault();

        if (ruta is null && (c.Normalizado.Contains("facil") || c.Normalizado.Contains("sencilla")))
            ruta = candidatas.FirstOrDefault(x => x.NivelDificultad != null &&
                                                  TextoChat.Normalizar(x.NivelDificultad) is "baja" or "facil");

        if (ruta is null && mencionada is { Ref.EsPrestador: false })
            ruta = rutas.FirstOrDefault(x => x.IdLugarDestino == mencionada.Ref.Id) ?? rutas.FirstOrDefault(x => x.IdLugarOrigen == mencionada.Ref.Id);

        if (ruta is null && candidatas.Count == 1)
            ruta = candidatas[0];

        if (ruta is null)
        {
            var ejemplos = candidatas.Select(x => x.Nombre).Take(3);
            Responder(r, TiposRespuesta.Aclaracion,
                $"¿De cuál ruta quieres los detalles? Por ejemplo: {string.Join(", ", ejemplos)}.");
            return;
        }

        DescribirRuta(c, r, ruta);
    }

    private void DescribirRuta(ConsultaChat c, RespuestaChat r, RutaResumen ruta)
    {
        var texto = new StringBuilder(ruta.Nombre).AppendLine();

        var datos = new List<string>();
        if (ruta.Origen != null || ruta.Destino != null)
            datos.Add($"Recorrido: {ruta.Origen ?? "—"} → {ruta.Destino ?? "—"}");
        Agregar(datos, "Distancia", ruta.DistanciaKilometros.HasValue ? $"{TextoNumero(ruta.DistanciaKilometros.Value)} km" : null);
        Agregar(datos, "Duración aproximada", TextoDuracion(ruta.DuracionMinutos));
        Agregar(datos, "Dificultad", ruta.NivelDificultad);
        Agregar(datos, "Transporte", ruta.TipoTransporte);

        if (datos.Count > 0)
            texto.AppendLine(string.Join("\n", datos.Select(d => "• " + d)));
        if (!string.IsNullOrWhiteSpace(ruta.Descripcion))
            texto.AppendLine().AppendLine(TextoChat.Recortar(ruta.Descripcion, 600));
        if (!string.IsNullOrWhiteSpace(ruta.Instrucciones))
            texto.AppendLine().AppendLine("Instrucciones: " + TextoChat.Recortar(ruta.Instrucciones, 700));

        c.Contexto.RecordarRutas(new[] { ruta.IdRuta });
        if (ruta.IdLugarDestino is { } destino)
            c.Contexto.LugarEnFoco = RefFicha.Lugar(destino);

        r.Resultados.Add(new ResultadoChat(ruta.IdRuta, ruta.Nombre, "Ruta", ResumenRuta(ruta).Trim(' ', '—')));
        Responder(r, TiposRespuesta.Datos, texto.ToString().Trim(), limpiarResultados: false);
    }

    private async Task AyudaAsync(RespuestaChat r, CancellationToken ct)
    {
        var ejemploAtractivo = await _context.Atractivos.AsNoTracking()
            .Where(a => a.Lugar!.Activo)
            .OrderByDescending(a => a.Destacado)
            .Select(a => a.Lugar!.Nombre)
            .FirstOrDefaultAsync(ct);

        var texto = new StringBuilder("Puedo ayudarte con la información turística de Jimaní registrada en nuestra base de datos. Prueba con preguntas como:\n");
        texto.AppendLine("• ¿Qué lugares naturales puedo visitar?");
        texto.AppendLine("• Quiero un hotel");
        texto.AppendLine("• ¿Dónde puedo comer?");
        texto.AppendLine("• Necesito un chofer");
        texto.AppendLine("• ¿Hay guías turísticos?");
        texto.AppendLine("• ¿Hay barbería?");
        texto.AppendLine("• ¿Qué rutas hay?");
        if (ejemploAtractivo != null)
        {
            texto.AppendLine($"• Háblame de {ejemploAtractivo}");
            texto.AppendLine($"• ¿Cómo llego a {ejemploAtractivo}?");
        }

        Responder(r, TiposRespuesta.Conversacional, texto.ToString().Trim());
    }

    // ------------------------------------------------------------------ respuesta de listas

    private void ResponderLista(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas, FichaResumen? mencionada,
        Dominio dominio, List<ElementoLista> elementos, string intro, string nombrePlural, string pregunta)
    {
        string? nota = null;

        if (TextoChat.PideCercania(c.Normalizado))
        {
            // Punto de referencia: la ficha mencionada o la última de la que se habló
            var referencia = mencionada
                ?? (c.Contexto.LugarEnFoco is { } enFoco ? fichas.FirstOrDefault(f => f.Ref == enFoco) : null)
                ?? (c.Contexto.UltimosLugares.Count > 0 ? fichas.FirstOrDefault(f => f.Ref == c.Contexto.UltimosLugares[0]) : null);

            if (referencia?.Latitud is { } lat && referencia.Longitud is { } lon)
            {
                var coordenadas = fichas.ToDictionary(f => f.Ref);
                elementos = elementos
                    .Where(e => e.Ref != referencia.Ref)
                    .Select(e => coordenadas.TryGetValue(e.Ref, out var f) && f.Latitud.HasValue && f.Longitud.HasValue
                        ? e with { DistanciaKm = DistanciaKm(lat, lon, f.Latitud.Value, f.Longitud.Value) }
                        : e)
                    .OrderBy(e => e.DistanciaKm ?? double.MaxValue)
                    .ToList();

                intro = $"Estos son los {nombrePlural} registrados más cercanos a {referencia.Nombre}:";

                if (elementos.Count == 0)
                {
                    Responder(r, TiposRespuesta.SinResultados, $"No encontré otros {nombrePlural} registrados cerca de {referencia.Nombre}.");
                    return;
                }
            }
            else
            {
                nota = "No sé desde qué lugar quieres calcular la distancia. Dime un lugar de referencia, por ejemplo «cerca del Lago Enriquillo».";
            }
        }

        var mostrados = elementos.Take(MaximoResultados).ToList();
        var texto = new StringBuilder(intro).Append('\n');

        for (var i = 0; i < mostrados.Count; i++)
        {
            var e = mostrados[i];
            texto.Append($"{i + 1}. {e.Nombre}");
            if (!string.IsNullOrEmpty(e.Detalle)) texto.Append($" — {e.Detalle}");
            if (e.DistanciaKm is { } km) texto.Append($" (a {TextoNumero((decimal)Math.Round(km, 1))} km)");
            texto.Append('\n');
        }

        if (elementos.Count > mostrados.Count)
            texto.Append($"…y {elementos.Count - mostrados.Count} más.\n");

        if (nota != null)
            texto.Append('\n').Append(nota).Append('\n');

        texto.Append('\n').Append(pregunta);

        c.Contexto.RecordarLista(dominio, mostrados.Select(e => e.Ref));
        r.Resultados.AddRange(mostrados.Select((e, i) => Tarjeta(e, i + 1)));
        Responder(r, TiposRespuesta.Datos, texto.ToString(), limpiarResultados: false);

        // Junto a las fichas basta con la introducción y la pregunta (el detalle está en cada ficha)
        var corto = new StringBuilder(intro);
        if (elementos.Count > mostrados.Count)
            corto.Append($" (te muestro {mostrados.Count} de {elementos.Count})");
        if (nota != null)
            corto.Append("\n\n").Append(nota);
        corto.Append("\n\n").Append(pregunta);
        r.TextoCorto = corto.ToString();
    }

    // Ficha que el chat muestra con foto, tipo y botones de perfil y contacto
    private static ResultadoChat Tarjeta(ElementoLista e, int? numero = null) =>
        new(e.Ref.Id, e.Nombre, e.Ref.EsPrestador ? "Prestador" : "Lugar", e.Detalle)
        {
            Numero = numero,
            Subtitulo = e.Subtitulo,
            Imagen = SitioPublico.UrlSegura(e.Imagen),
            Icono = e.Icono,
            Url = e.Ref.EsPrestador ? $"/Sitio/Prestador/{e.Ref.Id}" : $"/Sitio/Lugar/{e.Ref.Id}",
            WhatsApp = SitioPublico.EnlaceWhatsApp(e.WhatsApp, $"Hola {e.Nombre}, le escribo desde el asistente de Turismo Jimaní."),
            Telefono = SitioPublico.EnlaceTelefono(e.Telefono)
        };

    // ------------------------------------------------------------------ utilidades

    // Todas las fichas que el asistente puede nombrar: lugares turísticos y prestadores publicados
    private async Task<List<FichaResumen>> CargarFichasAsync(CancellationToken ct)
    {
        var lugares = await _context.Lugares.AsNoTracking()
            .Where(l => l.Activo)
            .Select(l => new { l.IdLugar, l.Nombre, l.Latitud, l.Longitud })
            .ToListAsync(ct);

        var prestadores = await _context.Prestadores.AsNoTracking()
            .Where(p => p.Activo)
            .Select(p => new { p.IdPrestador, p.Nombre, p.TipoPrestador!.Grupo, p.Latitud, p.Longitud })
            .ToListAsync(ct);

        return lugares
            .Select(l => new FichaResumen(RefFicha.Lugar(l.IdLugar), l.Nombre, Dominio.Atractivo, l.Latitud, l.Longitud))
            .Concat(prestadores.Select(p => new FichaResumen(RefFicha.Prestador(p.IdPrestador), p.Nombre, DominioDeGrupo(p.Grupo), p.Latitud, p.Longitud)))
            .ToList();
    }

    private static Dominio DominioDeGrupo(string? grupo) => grupo switch
    {
        GruposPrestador.Alojamiento => Dominio.Alojamiento,
        GruposPrestador.Gastronomia => Dominio.Restaurante,
        GruposPrestador.Transporte => Dominio.Transporte,
        GruposPrestador.Guia => Dominio.Guia,
        _ => Dominio.Servicio
    };

    private static string? GrupoDeDominio(Dominio dominio) => dominio switch
    {
        Dominio.Alojamiento => GruposPrestador.Alojamiento,
        Dominio.Restaurante => GruposPrestador.Gastronomia,
        Dominio.Transporte => GruposPrestador.Transporte,
        Dominio.Guia => GruposPrestador.Guia,
        _ => null
    };

    private static IQueryable<RutaResumen> ProyectarRutas(IQueryable<Ruta> rutas) =>
        rutas.Select(x => new RutaResumen(
            x.IdRuta, x.Nombre, x.Descripcion, x.IdLugarOrigen, x.IdLugarDestino,
            x.LugarOrigen != null ? x.LugarOrigen.Nombre : null,
            x.LugarDestino != null ? x.LugarDestino.Nombre : null,
            x.DistanciaKilometros, x.DuracionMinutos, x.NivelDificultad, x.TipoTransporte, x.Instrucciones));

    private async Task<long?> RegistrarConsultaAsync(string mensaje, RespuestaChat respuesta, int milisegundos)
    {
        try
        {
            var consulta = new ConsultaAsistente
            {
                IdSesion = respuesta.IdSesion,
                MensajeUsuario = mensaje,
                IntencionDetectada = Truncar(respuesta.Intencion, 100),
                RespuestaAsistente = respuesta.Respuesta,
                TipoRespuesta = Truncar(respuesta.TipoRespuesta, 50),
                TiempoRespuestaMilisegundos = milisegundos,
                FechaConsulta = DateTime.Now
            };

            _context.ConsultasAsistente.Add(consulta);
            await _context.SaveChangesAsync(CancellationToken.None);
            return consulta.IdConsulta;
        }
        catch (Exception ex)
        {
            // Si no se puede registrar, el usuario igual recibe su respuesta
            _logger.LogWarning(ex, "No se pudo registrar la consulta del asistente");
            _context.ChangeTracker.Clear();
            return null;
        }
    }

    private static void Responder(RespuestaChat r, string tipo, string texto, bool limpiarResultados = true)
    {
        r.TipoRespuesta = tipo;
        r.Respuesta = texto;
        if (limpiarResultados)
            r.Resultados.Clear();
    }

    private static bool EsErrorBaseDatos(Exception ex)
    {
        for (var actual = ex; actual != null; actual = actual.InnerException)
        {
            if (actual is DbException or DbUpdateException)
                return true;
        }
        return false;
    }

    private static string IntencionConsultar(Dominio dominio) => dominio switch
    {
        Dominio.Alojamiento => NombresIntencion.ConsultarAlojamiento,
        Dominio.Restaurante => NombresIntencion.ConsultarRestaurante,
        Dominio.Ruta => NombresIntencion.ConsultarRuta,
        _ => NombresIntencion.ConsultarAtractivo
    };

    private static string NombrePlural(Dominio dominio) => dominio switch
    {
        Dominio.Alojamiento => "alojamientos",
        Dominio.Restaurante => "lugares para comer",
        Dominio.Transporte => "servicios de transporte",
        Dominio.Guia => "guías turísticos",
        Dominio.Servicio => "servicios",
        Dominio.Ruta => "rutas",
        _ => "atractivos"
    };

    private static string ResumenRuta(RutaResumen x)
    {
        var partes = Unir(", ",
            x.Origen != null || x.Destino != null ? $"{x.Origen ?? "—"} → {x.Destino ?? "—"}" : null,
            x.DistanciaKilometros.HasValue ? $"{TextoNumero(x.DistanciaKilometros.Value)} km" : null,
            TextoDuracion(x.DuracionMinutos));
        return string.IsNullOrEmpty(partes) ? string.Empty : $" — {partes}";
    }

    private static string? TextoPrecio(decimal? minimo, decimal? maximo) => (minimo, maximo) switch
    {
        ({ } min, { } max) when min == max => "RD$ " + TextoNumero(min),
        ({ } min, { } max) => $"desde RD$ {TextoNumero(min)} hasta RD$ {TextoNumero(max)}",
        ({ } min, null) => $"desde RD$ {TextoNumero(min)}",
        (null, { } max) => $"hasta RD$ {TextoNumero(max)}",
        _ => null
    };

    private static string? TextoHorario(TimeSpan? apertura, TimeSpan? cierre) =>
        apertura.HasValue || cierre.HasValue
            ? $"{(apertura.HasValue ? Formato.Hora(apertura) : "?")}–{(cierre.HasValue ? Formato.Hora(cierre) : "?")}"
            : null;

    private static string? TextoDuracion(int? minutos) => minutos switch
    {
        null or <= 0 => null,
        < 60 => $"{minutos} minutos",
        _ when minutos % 60 == 0 => $"{minutos / 60} h",
        _ => $"{minutos / 60} h {minutos % 60} min"
    };

    private static string TextoNumero(decimal valor) => valor.ToString("#,##0.##", CultureInfo.CurrentCulture);

    private static string Unir(string separador, params string?[] partes) =>
        string.Join(separador, partes
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => separador.StartsWith('.') ? p!.Trim().TrimEnd('.') : p!.Trim()));

    private static void Agregar(List<string> lista, string etiqueta, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
            lista.Add($"{etiqueta}: {valor.Trim()}");
    }

    private static string? Truncar(string? texto, int maximo) =>
        texto is null || texto.Length <= maximo ? texto : texto[..maximo];

    private static double DistanciaKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double radioTierra = 6371;
        double ARadianes(decimal grados) => (double)grados * Math.PI / 180;

        var dLat = ARadianes(lat2 - lat1);
        var dLon = ARadianes(lon2 - lon1);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ARadianes(lat1)) * Math.Cos(ARadianes(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * radioTierra * Math.Asin(Math.Sqrt(h));
    }

    // ------------------------------------------------------------------ tipos internos

    private sealed record ConsultaChat(string Mensaje, string Normalizado, ResultadoIntencion Clasificacion, ContextoConversacion Contexto)
    {
        public string Intencion => Clasificacion.Intencion;
        public double Confianza => Clasificacion.Confianza;
    }

    // Lugar turístico o prestador de servicios que el asistente puede nombrar y describir
    private sealed record FichaResumen(RefFicha Ref, string Nombre, Dominio Dominio, decimal? Latitud, decimal? Longitud);

    private sealed record FilaPrestador(int Id, string Nombre, string Tipo, string Grupo, string? IconoTipo, string? Especialidad,
        decimal? PrecioDesde, decimal? PrecioHasta, string? InformacionPrecio, string? ZonaCobertura, string? HorarioAtencion,
        string? WhatsApp, string? Telefono, bool ServicioDomicilio, bool RequiereReserva, bool Destacado, string? Imagen)
    {
        public string? Contacto => WhatsApp ?? Telefono;
    }

    private sealed record RutaResumen(int IdRuta, string Nombre, string? Descripcion, int? IdLugarOrigen, int? IdLugarDestino,
        string? Origen, string? Destino, decimal? DistanciaKilometros, int? DuracionMinutos, string? NivelDificultad,
        string? TipoTransporte, string? Instrucciones);

    private sealed record ElementoLista(RefFicha Ref, string Nombre, string? Detalle, decimal? Precio, double? DistanciaKm = null)
    {
        public string? Subtitulo { get; init; }
        public string? Imagen { get; init; }
        public string? Icono { get; init; }
        public string? WhatsApp { get; init; }
        public string? Telefono { get; init; }
    }
}
