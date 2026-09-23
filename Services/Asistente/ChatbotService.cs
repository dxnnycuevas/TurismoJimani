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
public class ChatbotService
{
    private const int MaximoResultados = 8;
    private static readonly TimeSpan DuracionContexto = TimeSpan.FromMinutes(30);
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    public const string MensajeNoReconocido =
        "No estoy seguro de lo que buscas. Puedes preguntarme por atractivos, alojamientos, restaurantes, rutas o transporte.";

    private static readonly string[] TiposFuenteDocumento = { "Lugar", "Atractivo", "Alojamiento", "Restaurante", "Transporte" };

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
        var lugares = await CargarLugaresAsync(ct);
        var lugarMencionado = TextoChat.BuscarPorNombre(c.Normalizado, lugares, l => l.Nombre);
        var intencion = c.Intencion;

        if (TextoChat.EsSoloReferencia(c.Normalizado) && c.Contexto.Dominio is { } dominioAnterior
            && (c.Contexto.UltimosLugares.Count > 0 || c.Contexto.UltimasRutas.Count > 0))
        {
            // Seguimiento corto como "el 2" sobre la última lista mostrada
            intencion = IntencionConsultar(dominioAnterior);
        }
        else if (c.Confianza < _umbralConfianza)
        {
            // BERT no está seguro: si el mensaje nombra un lugar registrado se da su información
            if (lugarMencionado is null)
            {
                Responder(r, TiposRespuesta.NoReconocida, MensajeNoReconocido);
                c.Contexto.UltimaIntencion = null;
                return;
            }
            intencion = NombresIntencion.ConsultarAtractivo;
        }
        else if (intencion == NombresIntencion.FueraDeAlcance && lugarMencionado is not null)
        {
            // "¿Cómo es Las Caritas?": si nombra un lugar registrado, se da su información
            intencion = NombresIntencion.ConsultarAtractivo;
        }

        r.Intencion = intencion;

        switch (intencion)
        {
            case NombresIntencion.Saludo:
                Responder(r, TiposRespuesta.Conversacional,
                    "¡Hola! Soy el asistente turístico de Jimaní. Puedo ayudarte a encontrar atractivos, alojamientos, " +
                    "restaurantes, rutas y transporte. ¿Qué te gustaría saber?");
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
                await BuscarAlojamientosAsync(c, r, lugares, lugarMencionado, ct);
                break;

            case NombresIntencion.BuscarRestaurante:
                await BuscarRestaurantesAsync(c, r, lugares, lugarMencionado, ct);
                break;

            case NombresIntencion.BuscarAtractivo:
                await BuscarAtractivosAsync(c, r, lugares, lugarMencionado, ct);
                break;

            case NombresIntencion.BuscarTransporte:
                await BuscarTransportesAsync(c, r, lugares, lugarMencionado, ct);
                break;

            case NombresIntencion.BuscarRuta:
                await BuscarRutasAsync(c, r, lugares, lugarMencionado, ct);
                break;

            case NombresIntencion.ConsultarRuta:
                await ConsultarRutaAsync(c, r, lugarMencionado, ct);
                break;

            case NombresIntencion.ConsultarAtractivo:
                await ConsultarLugarAsync(c, r, lugares, lugarMencionado, Dominio.Atractivo, ct);
                break;

            case NombresIntencion.ConsultarAlojamiento:
                await ConsultarLugarAsync(c, r, lugares, lugarMencionado, Dominio.Alojamiento, ct);
                break;

            case NombresIntencion.ConsultarRestaurante:
                await ConsultarLugarAsync(c, r, lugares, lugarMencionado, Dominio.Restaurante, ct);
                break;

            default: // FueraDeAlcance o una intención nueva sin lógica asociada
                Responder(r, TiposRespuesta.NoReconocida, MensajeNoReconocido);
                break;
        }

        c.Contexto.UltimaIntencion = intencion;
    }

    // ------------------------------------------------------------------ búsquedas (listas)

    private async Task BuscarAlojamientosAsync(ConsultaChat c, RespuestaChat r, List<LugarResumen> lugares,
        LugarResumen? mencionado, CancellationToken ct)
    {
        var alojamientos = await _context.Alojamientos.AsNoTracking()
            .Where(a => a.Lugar!.Activo)
            .Select(a => new { a.IdLugar, a.Lugar!.Nombre, a.TipoAlojamiento, a.PrecioMinimo, a.PrecioMaximo })
            .ToListAsync(ct);

        if (alojamientos.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré alojamientos registrados en este momento.");
            return;
        }

        var elementos = alojamientos.Select(a => new ElementoLista(
            a.IdLugar, a.Nombre,
            Unir(", ", a.TipoAlojamiento, TextoPrecio(a.PrecioMinimo, a.PrecioMaximo) is { } precio ? $"precio {precio}" : null),
            a.PrecioMinimo ?? a.PrecioMaximo)).ToList();

        var intro = "Claro. Estos son algunos alojamientos registrados en Jimaní:";
        if (TextoChat.PideMasBarato(c.Normalizado))
        {
            elementos = elementos.OrderBy(e => e.Precio ?? decimal.MaxValue).ThenBy(e => e.Nombre).ToList();
            intro = "Estos son los alojamientos registrados, del más económico al más caro:";
        }
        else if (TextoChat.PideMasCaro(c.Normalizado))
        {
            elementos = elementos.OrderByDescending(e => e.Precio ?? decimal.MinValue).ThenBy(e => e.Nombre).ToList();
            intro = "Estos son los alojamientos registrados, del más caro al más económico:";
        }

        ResponderLista(c, r, lugares, mencionado, Dominio.Alojamiento, elementos, intro,
            "alojamientos", "¿Quieres que te muestre información sobre alguno?");
    }

    private async Task BuscarRestaurantesAsync(ConsultaChat c, RespuestaChat r, List<LugarResumen> lugares,
        LugarResumen? mencionado, CancellationToken ct)
    {
        var restaurantes = await _context.Restaurantes.AsNoTracking()
            .Where(x => x.Lugar!.Activo)
            .Select(x => new { x.IdLugar, x.Lugar!.Nombre, x.TipoComida, x.NivelPrecio, x.HoraApertura, x.HoraCierre, x.ServicioDomicilio })
            .ToListAsync(ct);

        if (restaurantes.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré restaurantes registrados en este momento.");
            return;
        }

        var intro = "Claro. Estos son algunos restaurantes registrados en Jimaní:";

        // Filtro por tipo de comida mencionado ("comida criolla", "pizza"...)
        var filtro = TextoChat.PalabrasFiltro(c.Normalizado);
        var porTipo = restaurantes
            .Where(x => x.TipoComida != null && TextoChat.PalabrasClave(TextoChat.Normalizar(x.TipoComida))
                .Any(t => filtro.Any(f => TextoChat.CoincideRaiz(f, t))))
            .ToList();
        if (porTipo.Count > 0)
        {
            restaurantes = porTipo;
            intro = "Estos son los restaurantes registrados con ese tipo de comida:";
        }

        if (TextoChat.PideDomicilio(c.Normalizado))
        {
            restaurantes = restaurantes.Where(x => x.ServicioDomicilio).ToList();
            if (restaurantes.Count == 0)
            {
                Responder(r, TiposRespuesta.SinResultados, "No encontré restaurantes registrados con servicio a domicilio.");
                return;
            }
            intro = "Estos restaurantes registrados ofrecen servicio a domicilio:";
        }

        var elementos = restaurantes.Select(x => new ElementoLista(
            x.IdLugar, x.Nombre,
            Unir(", ", x.TipoComida, Formato.NivelPrecio(x.NivelPrecio), TextoHorario(x.HoraApertura, x.HoraCierre),
                x.ServicioDomicilio ? "servicio a domicilio" : null),
            x.NivelPrecio)).ToList();

        if (TextoChat.PideMasBarato(c.Normalizado))
        {
            elementos = elementos.OrderBy(e => e.Precio ?? decimal.MaxValue).ThenBy(e => e.Nombre).ToList();
            intro = "Estos son los restaurantes registrados, del más económico al más caro:";
        }

        ResponderLista(c, r, lugares, mencionado, Dominio.Restaurante, elementos, intro,
            "restaurantes", "¿Quieres que te muestre información sobre alguno?");
    }

    private async Task BuscarAtractivosAsync(ConsultaChat c, RespuestaChat r, List<LugarResumen> lugares,
        LugarResumen? mencionado, CancellationToken ct)
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
                Categorias = a.Lugar.Categorias.Where(cat => cat.Activo).Select(cat => cat.Nombre).ToList()
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
            .Select(a => new ElementoLista(a.IdLugar, a.Nombre,
                Unir(", ", a.TipoAtractivo, string.IsNullOrWhiteSpace(a.NivelDificultad) ? null : $"dificultad {a.NivelDificultad.ToLower()}"),
                null))
            .ToList();

        ResponderLista(c, r, lugares, mencionado, Dominio.Atractivo, elementos, intro,
            "atractivos", "¿Quieres que te dé más información sobre alguno?");
    }

    private async Task BuscarTransportesAsync(ConsultaChat c, RespuestaChat r, List<LugarResumen> lugares,
        LugarResumen? mencionado, CancellationToken ct)
    {
        var transportes = await _context.Transportes.AsNoTracking()
            .Where(t => t.Lugar!.Activo)
            .Select(t => new
            {
                t.IdLugar,
                t.Lugar!.Nombre,
                t.TipoTransporte,
                t.ZonaCobertura,
                t.Horario,
                t.InformacionPrecio,
                t.RequiereReserva,
                Telefono = t.Lugar.Contactos.OrderByDescending(x => x.EsPrincipal).Select(x => x.ValorContacto).FirstOrDefault()
                           ?? t.Lugar.Telefono
            })
            .ToListAsync(ct);

        if (transportes.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré servicios de transporte registrados en este momento.");
            return;
        }

        var intro = "Estos son los servicios de transporte registrados:";
        var filtro = TextoChat.PalabrasFiltro(c.Normalizado);
        var porTipo = transportes
            .Where(t => TextoChat.PalabrasClave(TextoChat.Normalizar(t.TipoTransporte)).Any(p => filtro.Any(f => TextoChat.CoincideRaiz(f, p))))
            .ToList();
        if (porTipo.Count > 0)
        {
            transportes = porTipo;
            intro = "Estos son los servicios de ese tipo de transporte registrados:";
        }

        var elementos = transportes.Select(t => new ElementoLista(t.IdLugar, t.Nombre,
            Unir(". ", t.TipoTransporte,
                string.IsNullOrWhiteSpace(t.ZonaCobertura) ? null : $"Cobertura: {t.ZonaCobertura}",
                string.IsNullOrWhiteSpace(t.Horario) ? null : $"Horario: {t.Horario}",
                string.IsNullOrWhiteSpace(t.InformacionPrecio) ? null : $"Precio: {t.InformacionPrecio}",
                t.RequiereReserva ? "Requiere reserva" : null,
                string.IsNullOrWhiteSpace(t.Telefono) ? null : $"Contacto: {t.Telefono}"),
            null)).ToList();

        ResponderLista(c, r, lugares, mencionado, Dominio.Transporte, elementos, intro,
            "servicios de transporte", "¿Quieres más información sobre alguno?");
    }

    private async Task BuscarRutasAsync(ConsultaChat c, RespuestaChat r, List<LugarResumen> lugares,
        LugarResumen? mencionado, CancellationToken ct)
    {
        // "¿Cómo llego ahí?" usa el lugar del que se estaba hablando
        var destino = mencionado;
        if (destino is null && c.Contexto.LugarEnFoco is { } enFoco && TextoChat.TieneReferencia(c.Normalizado))
            destino = lugares.FirstOrDefault(l => l.Id == enFoco);

        var consulta = _context.Rutas.AsNoTracking().Where(x => x.Activa);
        if (destino is not null)
            consulta = consulta.Where(x => x.IdLugarDestino == destino.Id || x.IdLugarOrigen == destino.Id);

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

    private async Task ConsultarLugarAsync(ConsultaChat c, RespuestaChat r, List<LugarResumen> lugares,
        LugarResumen? mencionado, Dominio dominioIntencion, CancellationToken ct)
    {
        // "¿Cuál tiene menor precio?" sobre la última lista mostrada
        var barato = TextoChat.PideMasBarato(c.Normalizado);
        if (mencionado is null && (barato || TextoChat.PideMasCaro(c.Normalizado)))
        {
            var dominio = c.Contexto.Dominio is Dominio.Alojamiento or Dominio.Restaurante
                ? c.Contexto.Dominio.Value
                : dominioIntencion;

            if (dominio == Dominio.Alojamiento)
            {
                await CompararPreciosAlojamientoAsync(c, r, barato, ct);
                return;
            }
            if (dominio == Dominio.Restaurante)
            {
                await CompararPreciosRestauranteAsync(c, r, barato, ct);
                return;
            }
        }

        // "¿Cuál tiene servicio a domicilio?" filtra los restaurantes registrados
        if (mencionado is null && TextoChat.PideDomicilio(c.Normalizado)
            && (dominioIntencion == Dominio.Restaurante || c.Contexto.Dominio == Dominio.Restaurante))
        {
            await BuscarRestaurantesAsync(c, r, lugares, null, ct);
            return;
        }

        var objetivo = mencionado ?? ResolverReferencia(c, lugares);

        if (objetivo is null)
        {
            PedirAclaracion(c, r, lugares, dominioIntencion);
            return;
        }

        await DescribirLugarAsync(c, r, objetivo.Id, dominioIntencion, ct);
    }

    private LugarResumen? ResolverReferencia(ConsultaChat c, List<LugarResumen> lugares)
    {
        var ctx = c.Contexto;
        var ordinal = TextoChat.ObtenerOrdinal(c.Normalizado, ctx.UltimosLugares.Count);

        int? id = null;
        if (ordinal is { } indice)
            id = ctx.UltimosLugares[indice];
        else if (ctx.EnfoqueReciente && ctx.LugarEnFoco is { } enFoco)
            id = enFoco;
        else if (ctx.UltimosLugares.Count == 1)
            id = ctx.UltimosLugares[0];
        else if (ctx.UltimosLugares.Count == 0 && ctx.LugarEnFoco is { } anterior)
            id = anterior;

        return id is null ? null : lugares.FirstOrDefault(l => l.Id == id);
    }

    private static void PedirAclaracion(ConsultaChat c, RespuestaChat r, List<LugarResumen> lugares, Dominio dominio)
    {
        // Si hay una lista reciente con varios elementos, se pregunta cuál
        if (c.Contexto.UltimosLugares.Count > 1 && !c.Contexto.EnfoqueReciente)
        {
            var opciones = c.Contexto.UltimosLugares
                .Select(id => lugares.FirstOrDefault(l => l.Id == id)?.Nombre)
                .Where(n => n != null).Take(3).ToList();

            Responder(r, TiposRespuesta.Aclaracion,
                $"¿Sobre cuál quieres información? Puedes escribir su nombre o su número, por ejemplo «el 1» ({string.Join(", ", opciones)}).");
            return;
        }

        var nombres = dominio switch
        {
            Dominio.Alojamiento => lugares.Where(l => l.EsAlojamiento),
            Dominio.Restaurante => lugares.Where(l => l.EsRestaurante),
            Dominio.Transporte => lugares.Where(l => l.EsTransporte),
            _ => lugares.Where(l => l.EsAtractivo)
        };
        var ejemplos = nombres.Select(l => l.Nombre).OrderBy(n => n).Take(3).ToList();
        var tipo = NombrePlural(dominio);

        if (ejemplos.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados, $"No encontré {tipo} registrados en este momento.");
            return;
        }

        Responder(r, TiposRespuesta.Aclaracion,
            $"¿Sobre qué lugar quieres información? Por ejemplo: {string.Join(", ", ejemplos)}. " +
            $"También puedo mostrarte la lista de {tipo} registrados.");
    }

    private async Task CompararPreciosAlojamientoAsync(ConsultaChat c, RespuestaChat r, bool barato, CancellationToken ct)
    {
        var ids = c.Contexto.Dominio == Dominio.Alojamiento ? c.Contexto.UltimosLugares : new List<int>();

        var consulta = _context.Alojamientos.AsNoTracking()
            .Where(a => a.Lugar!.Activo && (a.PrecioMinimo != null || a.PrecioMaximo != null));
        if (ids.Count > 0)
            consulta = consulta.Where(a => ids.Contains(a.IdLugar));

        var candidatos = await consulta
            .Select(a => new { a.IdLugar, a.Lugar!.Nombre, a.PrecioMinimo, a.PrecioMaximo })
            .ToListAsync(ct);

        if (candidatos.Count == 0)
        {
            var hayAlojamientos = await _context.Alojamientos.AnyAsync(a => a.Lugar!.Activo, ct);
            Responder(r, TiposRespuesta.SinResultados, hayAlojamientos
                ? "No tengo precios registrados para esos alojamientos."
                : "No encontré alojamientos registrados en este momento.");
            return;
        }

        var elegido = barato
            ? candidatos.OrderBy(a => a.PrecioMinimo ?? a.PrecioMaximo).First()
            : candidatos.OrderByDescending(a => a.PrecioMaximo ?? a.PrecioMinimo).First();

        var entre = ids.Count > 0 ? " entre los que te mostré" : " registrado";
        Responder(r, TiposRespuesta.Datos,
            $"El alojamiento con {(barato ? "menor" : "mayor")} precio{entre} es {elegido.Nombre}, " +
            $"con precio {TextoPrecio(elegido.PrecioMinimo, elegido.PrecioMaximo)}.\n\n¿Quieres más información sobre él?");

        c.Contexto.RecordarLugar(Dominio.Alojamiento, elegido.IdLugar);
        r.Resultados.Add(new ResultadoChat(elegido.IdLugar, elegido.Nombre, "Alojamiento", TextoPrecio(elegido.PrecioMinimo, elegido.PrecioMaximo)));
    }

    private async Task CompararPreciosRestauranteAsync(ConsultaChat c, RespuestaChat r, bool barato, CancellationToken ct)
    {
        var ids = c.Contexto.Dominio == Dominio.Restaurante ? c.Contexto.UltimosLugares : new List<int>();

        var consulta = _context.Restaurantes.AsNoTracking().Where(x => x.Lugar!.Activo && x.NivelPrecio != null);
        if (ids.Count > 0)
            consulta = consulta.Where(x => ids.Contains(x.IdLugar));

        var candidatos = await consulta.Select(x => new { x.IdLugar, x.Lugar!.Nombre, x.NivelPrecio }).ToListAsync(ct);

        if (candidatos.Count == 0)
        {
            var hayRestaurantes = await _context.Restaurantes.AnyAsync(x => x.Lugar!.Activo, ct);
            Responder(r, TiposRespuesta.SinResultados, hayRestaurantes
                ? "No tengo el nivel de precio registrado para esos restaurantes."
                : "No encontré restaurantes registrados en este momento.");
            return;
        }

        var elegido = barato
            ? candidatos.OrderBy(x => x.NivelPrecio).First()
            : candidatos.OrderByDescending(x => x.NivelPrecio).First();

        Responder(r, TiposRespuesta.Datos,
            $"El restaurante {(barato ? "más económico" : "más caro")}{(ids.Count > 0 ? " entre los que te mostré" : " registrado")} " +
            $"es {elegido.Nombre} (nivel de precio {Formato.NivelPrecio(elegido.NivelPrecio)}).\n\n¿Quieres más información sobre él?");

        c.Contexto.RecordarLugar(Dominio.Restaurante, elegido.IdLugar);
        r.Resultados.Add(new ResultadoChat(elegido.IdLugar, elegido.Nombre, "Restaurante", Formato.NivelPrecio(elegido.NivelPrecio)));
    }

    private async Task DescribirLugarAsync(ConsultaChat c, RespuestaChat r, int idLugar, Dominio preferido, CancellationToken ct)
    {
        var lugar = await _context.Lugares.AsNoTracking()
            .Include(l => l.Atractivo)
            .Include(l => l.Alojamiento)
            .Include(l => l.Restaurante)
            .Include(l => l.Transporte)
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
            .Where(d => d.Activo && d.IdReferencia == idLugar && (d.TipoFuente == null || TiposFuenteDocumento.Contains(d.TipoFuente)))
            .Select(d => d.Contenido)
            .ToListAsync(ct);

        // El tipo real lo decide la base de datos (un hotel siempre se describe como alojamiento)
        Dominio? dominio = preferido switch
        {
            Dominio.Alojamiento when lugar.Alojamiento != null => Dominio.Alojamiento,
            Dominio.Restaurante when lugar.Restaurante != null => Dominio.Restaurante,
            Dominio.Atractivo when lugar.Atractivo != null => Dominio.Atractivo,
            _ when lugar.Atractivo != null => Dominio.Atractivo,
            _ when lugar.Alojamiento != null => Dominio.Alojamiento,
            _ when lugar.Restaurante != null => Dominio.Restaurante,
            _ when lugar.Transporte != null => Dominio.Transporte,
            _ => null
        };

        var texto = new StringBuilder();
        texto.AppendLine(lugar.Nombre);

        if (!string.IsNullOrWhiteSpace(lugar.DescripcionCorta))
            texto.AppendLine(lugar.DescripcionCorta.Trim());
        if (!string.IsNullOrWhiteSpace(lugar.Descripcion) && lugar.Descripcion.Trim() != lugar.DescripcionCorta?.Trim())
            texto.AppendLine(TextoChat.Recortar(lugar.Descripcion, 700));

        var datos = new List<string>();
        var parrafos = new List<string>();

        switch (dominio)
        {
            case Dominio.Atractivo:
                var a = lugar.Atractivo!;
                Agregar(datos, "Tipo", a.TipoAtractivo);
                Agregar(datos, "Dificultad", a.NivelDificultad);
                Agregar(datos, "Duración de la visita", TextoDuracion(a.DuracionVisitaMinutos));
                Agregar(datos, "Estado de conservación", a.EstadoConservacion);
                Agregar(parrafos, "Información natural", TextoChat.Recortar(a.InformacionNatural, 400));
                Agregar(parrafos, "Información cultural", TextoChat.Recortar(a.InformacionCultural, 400));
                Agregar(parrafos, "Cómo acceder", TextoChat.Recortar(a.InformacionAcceso, 400));
                break;

            case Dominio.Alojamiento:
                var h = lugar.Alojamiento!;
                Agregar(datos, "Tipo", h.TipoAlojamiento);
                Agregar(datos, "Precio", TextoPrecio(h.PrecioMinimo, h.PrecioMaximo));
                Agregar(datos, "Habitaciones", h.CantidadHabitaciones?.ToString());
                Agregar(datos, "Hora de entrada", Formato.Hora(h.HoraEntrada));
                Agregar(datos, "Hora de salida", Formato.Hora(h.HoraSalida));
                Agregar(datos, "Reservas", h.EnlaceReserva);
                break;

            case Dominio.Restaurante:
                var x = lugar.Restaurante!;
                Agregar(datos, "Tipo de comida", x.TipoComida);
                Agregar(datos, "Nivel de precio", Formato.NivelPrecio(x.NivelPrecio));
                Agregar(datos, "Horario", TextoHorario(x.HoraApertura, x.HoraCierre));
                datos.Add($"Servicio a domicilio: {(x.ServicioDomicilio ? "sí" : "no")}");
                Agregar(datos, "Menú", x.EnlaceMenu);
                break;

            case Dominio.Transporte:
                var t = lugar.Transporte!;
                Agregar(datos, "Tipo", t.TipoTransporte);
                Agregar(datos, "Cobertura", t.ZonaCobertura);
                Agregar(datos, "Horario", t.Horario);
                Agregar(datos, "Precio", t.InformacionPrecio);
                datos.Add($"Requiere reserva: {(t.RequiereReserva ? "sí" : "no")}");
                break;
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
        Agregar(datos, "Servicios", string.Join(", ", lugar.Servicios.Where(x => x.Activo).Select(x => x.Nombre)));

        if (datos.Count > 0)
            texto.AppendLine().Append(string.Join("\n", datos.Select(d => "• " + d))).AppendLine();

        foreach (var parrafo in parrafos.Concat(documentos.Select(d => TextoChat.Recortar(d, 600))))
            texto.AppendLine().AppendLine(parrafo);

        texto.AppendLine().Append(dominio switch
        {
            Dominio.Alojamiento => "¿Quieres ver otros alojamientos o lugares cercanos para visitar?",
            Dominio.Restaurante => "¿Quieres ver otros restaurantes o saber cómo llegar?",
            _ => "¿Quieres saber cómo llegar o qué otros lugares hay cerca?"
        });

        c.Contexto.RecordarLugar(dominio, lugar.IdLugar);
        r.Resultados.Add(new ResultadoChat(lugar.IdLugar, lugar.Nombre, dominio?.ToString() ?? "Lugar", lugar.DescripcionCorta));
        Responder(r, TiposRespuesta.Datos, texto.ToString().Trim(), limpiarResultados: false);
    }

    private async Task DescribirUbicacionAsync(ConsultaChat c, RespuestaChat r, LugarResumen destino, CancellationToken ct)
    {
        var lugar = await _context.Lugares.AsNoTracking()
            .Where(l => l.IdLugar == destino.Id)
            .Select(l => new { l.Nombre, l.Direccion, l.Municipio, l.Provincia, l.Latitud, l.Longitud })
            .FirstAsync(ct);

        var texto = new StringBuilder($"No tengo una ruta registrada hacia {lugar.Nombre}");
        var ubicacion = Unir(", ", lugar.Direccion, lugar.Municipio, lugar.Provincia);
        texto.Append(string.IsNullOrEmpty(ubicacion) ? "." : $", pero según los datos registrados se encuentra en {ubicacion}.");

        if (lugar.Latitud.HasValue && lugar.Longitud.HasValue)
        {
            var coordenadas = $"{lugar.Latitud.Value.ToString("0.######", Invariante)},{lugar.Longitud.Value.ToString("0.######", Invariante)}";
            texto.Append($"\n\nCoordenadas: {coordenadas}\nPuedes abrirlas en un mapa: https://www.google.com/maps?q={coordenadas}");
        }

        var hayTransporte = await _context.Transportes.AnyAsync(t => t.Lugar!.Activo, ct);
        if (hayTransporte)
            texto.Append("\n\nSi quieres, también puedo mostrarte los servicios de transporte registrados.");

        c.Contexto.RecordarLugar(null, destino.Id);
        r.Resultados.Add(new ResultadoChat(destino.Id, lugar.Nombre, "Lugar", ubicacion));
        Responder(r, TiposRespuesta.Datos, texto.ToString(), limpiarResultados: false);
    }

    private async Task ConsultarRutaAsync(ConsultaChat c, RespuestaChat r, LugarResumen? mencionado, CancellationToken ct)
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

        if (ruta is null && mencionado is not null)
            ruta = rutas.FirstOrDefault(x => x.IdLugarDestino == mencionado.Id) ?? rutas.FirstOrDefault(x => x.IdLugarOrigen == mencionado.Id);

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
            c.Contexto.LugarEnFoco = destino;

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
        texto.AppendLine("• ¿Hay transporte?");
        texto.AppendLine("• ¿Qué rutas hay?");
        if (ejemploAtractivo != null)
        {
            texto.AppendLine($"• Háblame de {ejemploAtractivo}");
            texto.AppendLine($"• ¿Cómo llego a {ejemploAtractivo}?");
        }

        Responder(r, TiposRespuesta.Conversacional, texto.ToString().Trim());
    }

    // ------------------------------------------------------------------ respuesta de listas

    private void ResponderLista(ConsultaChat c, RespuestaChat r, List<LugarResumen> lugares, LugarResumen? mencionado,
        Dominio dominio, List<ElementoLista> elementos, string intro, string nombrePlural, string pregunta)
    {
        string? nota = null;

        if (TextoChat.PideCercania(c.Normalizado))
        {
            // Punto de referencia: el lugar mencionado o el último del que se habló
            var referencia = mencionado
                ?? (c.Contexto.LugarEnFoco is { } enFoco ? lugares.FirstOrDefault(l => l.Id == enFoco) : null)
                ?? (c.Contexto.UltimosLugares.Count > 0 ? lugares.FirstOrDefault(l => l.Id == c.Contexto.UltimosLugares[0]) : null);

            if (referencia?.Latitud is { } lat && referencia.Longitud is { } lon)
            {
                var coordenadas = lugares.ToDictionary(l => l.Id);
                elementos = elementos
                    .Where(e => e.IdLugar != referencia.Id)
                    .Select(e => coordenadas.TryGetValue(e.IdLugar, out var l) && l.Latitud.HasValue && l.Longitud.HasValue
                        ? e with { DistanciaKm = DistanciaKm(lat, lon, l.Latitud.Value, l.Longitud.Value) }
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

        c.Contexto.RecordarLista(dominio, mostrados.Select(e => e.IdLugar));
        r.Resultados.AddRange(mostrados.Select(e => new ResultadoChat(e.IdLugar, e.Nombre, dominio.ToString(), e.Detalle)));
        Responder(r, TiposRespuesta.Datos, texto.ToString(), limpiarResultados: false);
    }

    // ------------------------------------------------------------------ utilidades

    private async Task<List<LugarResumen>> CargarLugaresAsync(CancellationToken ct) =>
        await _context.Lugares.AsNoTracking()
            .Where(l => l.Activo)
            .Select(l => new LugarResumen(l.IdLugar, l.Nombre,
                l.Atractivo != null, l.Alojamiento != null, l.Restaurante != null, l.Transporte != null,
                l.Latitud, l.Longitud))
            .ToListAsync(ct);

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
        Dominio.Restaurante => "restaurantes",
        Dominio.Transporte => "servicios de transporte",
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
        ({ } min, { } max) when min == max => TextoNumero(min),
        ({ } min, { } max) => $"desde {TextoNumero(min)} hasta {TextoNumero(max)}",
        ({ } min, null) => $"desde {TextoNumero(min)}",
        (null, { } max) => $"hasta {TextoNumero(max)}",
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

    private sealed record LugarResumen(int Id, string Nombre, bool EsAtractivo, bool EsAlojamiento, bool EsRestaurante,
        bool EsTransporte, decimal? Latitud, decimal? Longitud);

    private sealed record RutaResumen(int IdRuta, string Nombre, string? Descripcion, int? IdLugarOrigen, int? IdLugarDestino,
        string? Origen, string? Destino, decimal? DistanciaKilometros, int? DuracionMinutos, string? NivelDificultad,
        string? TipoTransporte, string? Instrucciones);

    private sealed record ElementoLista(int IdLugar, string Nombre, string? Detalle, decimal? Precio, double? DistanciaKm = null);
}
