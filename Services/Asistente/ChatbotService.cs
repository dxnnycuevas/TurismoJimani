using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using AppDonnyCuevas20210074.Models.Asistente;
using Microsoft.Data.SqlClient;
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
    public const string PlanificarViaje = "PlanificarViaje";
    public const string BuscarEvento = "BuscarEvento";
    public const string DatoCurioso = "DatoCurioso";
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
        "transporte, guías turísticos, otros servicios, rutas, eventos o datos curiosos de Jimaní.";

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

    // idUsuario: visitante con sesión iniciada (la consulta queda en su historial de Mi perfil)
    public async Task<RespuestaChat> ProcesarAsync(string mensaje, Guid idSesion, int? idUsuario = null, CancellationToken ct = default)
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

            // Se recuerda el último plan para que el visitante pueda guardarlo en su perfil
            if (respuesta.Presupuesto != null)
                contexto.UltimoPresupuesto = respuesta.Presupuesto;
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
        respuesta.IdConsulta = await RegistrarConsultaAsync(mensaje, respuesta, idUsuario, (int)reloj.ElapsedMilliseconds);
        return respuesta;
    }

    // ------------------------------------------------------------------ enrutamiento por intención

    private async Task ResolverAsync(ConsultaChat c, RespuestaChat r, CancellationToken ct)
    {
        // Planificador de presupuesto ("tengo 10000 pesos para 5 días") y sus respuestas de seguimiento
        if (await ResolverPlanAsync(c, r, ct))
        {
            r.Intencion = NombresIntencion.PlanificarViaje;
            c.Contexto.UltimaIntencion = NombresIntencion.PlanificarViaje;
            return;
        }

        var fichas = await CargarFichasAsync(ct);
        var (mencionada, puntajeFicha) = TextoChat.BuscarConPuntaje(c.Normalizado, fichas, f => f.Nombre);
        var intencion = c.Intencion;

        // Eventos y rutas nombrados en el mensaje. Si se nombran con más claridad que una ficha, ganan:
        // "¿cuándo es la Observación de flamencos?" es un evento y "¿cuánto dura la Excursión a Isla Cabritos?", una ruta
        var eventos = await CargarEventosAsync(ct);
        var (eventoParecido, puntajeEvento) = TextoChat.BuscarConPuntaje(c.Normalizado, eventos, e => e.Titulo);
        var nombresRutas = await _context.Rutas.AsNoTracking().Where(x => x.Activa).Select(x => x.Nombre).ToListAsync(ct);
        var (_, puntajeRuta) = TextoChat.BuscarConPuntaje(c.Normalizado, nombresRutas, n => n);

        // "¿Hay eventos en el Lago Enriquillo?" nombra el lago, no un evento: el evento cuenta solo si se nombra con más claridad
        var eventoMencionado = puntajeEvento > puntajeFicha && puntajeEvento > puntajeRuta ? eventoParecido : null;

        // Tipos de servicio registrados que nombra el mensaje ("farmacia", "colmado", "taller"...)
        var gruposMencionados = mencionada is null ? await GruposDeTiposMencionadosAsync(c, ct) : null;

        if (TextoChat.EsSoloReferencia(c.Normalizado) && c.Contexto.Dominio is { } dominioAnterior
            && (c.Contexto.UltimosLugares.Count > 0 || c.Contexto.UltimasRutas.Count > 0 || c.Contexto.UltimosEventos.Count > 0))
        {
            // Seguimiento corto como "el 2" sobre la última lista mostrada
            intencion = IntencionConsultar(dominioAnterior);
        }
        else if (eventoMencionado is not null)
        {
            intencion = NombresIntencion.BuscarEvento;
        }
        else if (puntajeRuta >= 1000 && puntajeRuta > puntajeFicha)
        {
            // Nombre completo de una ruta ("Excursión a Isla Cabritos"), aunque contenga el de un lugar
            intencion = NombresIntencion.ConsultarRuta;
        }
        else if (TextoChat.PideDatoCurioso(c.Normalizado))
        {
            intencion = NombresIntencion.DatoCurioso;
        }
        else if (TextoChat.PideEventos(c.Normalizado))
        {
            // "¿Qué eventos hay?", "¿hay alguna feria?", "promociones": BERT puede confundirlos con lugares o servicios
            intencion = NombresIntencion.BuscarEvento;
        }
        else if (gruposMencionados is not null && !gruposMencionados.Contains(GrupoDeIntencion(intencion) ?? "")
                 && intencion is not (NombresIntencion.BuscarRuta or NombresIntencion.ConsultarRuta))
        {
            // Nombra un tipo de servicio pero BERT lo clasificó en otra cosa: se buscan esos prestadores
            intencion = NombresIntencion.BuscarServicio;
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
        else if (intencion == NombresIntencion.DatoCurioso && mencionada is not null)
        {
            // BERT puede tomar "¿cómo es Las Caritas?", "¿quién es María Féliz?" o "¿por qué el lago es salado?" por un
            // dato curioso: se describe la ficha (la de un lugar ya trae el dato curioso que mejor responde)
            intencion = NombresIntencion.ConsultarAtractivo;
        }
        else if (intencion == NombresIntencion.BuscarEvento && eventoMencionado is null
                 && TextoChat.PeriodoPedido(c.Normalizado, DateTime.Today) is null
                 && (mencionada is not null || c.Contexto.EnfoqueReciente))
        {
            // "¿A qué hora abren?" sin palabras de evento ni fechas: se refiere a la ficha nombrada o de la que se hablaba
            intencion = IntencionConsultar(mencionada?.Dominio ?? c.Contexto.Dominio ?? Dominio.Atractivo);
        }

        r.Intencion = intencion;

        switch (intencion)
        {
            case NombresIntencion.Saludo:
                Responder(r, TiposRespuesta.Conversacional,
                    "¡Hola! Soy el asistente turístico de Jimaní. Puedo ayudarte a encontrar atractivos, alojamientos, " +
                    "lugares para comer, transporte, guías turísticos, otros servicios, rutas y eventos, armar un presupuesto " +
                    "para tu viaje o contarte datos curiosos. ¿Qué te gustaría saber?");
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

            case NombresIntencion.BuscarEvento:
                await BuscarEventosAsync(c, r, eventos, eventoMencionado, mencionada, ct);
                break;

            case NombresIntencion.DatoCurioso:
                await ContarDatoCuriosoAsync(c, r, fichas, ct);
                break;

            default: // FueraDeAlcance o una intención nueva sin lógica asociada
                Responder(r, TiposRespuesta.NoReconocida, MensajeNoReconocido);
                break;
        }

        c.Contexto.UltimaIntencion = intencion;
    }

    // ------------------------------------------------------------------ planificador de presupuesto

    private const string OpcionSinPasaje = "Ya estoy en Jimaní";

    // Devuelve true si el mensaje pertenece al plan de viaje (nuevo o en curso) y ya se respondió
    private async Task<bool> ResolverPlanAsync(ConsultaChat c, RespuestaChat r, CancellationToken ct)
    {
        var ctx = c.Contexto;
        var esNuevo = c.Intencion == NombresIntencion.PlanificarViaje || TextoChat.EsPresupuesto(c.Mensaje)
                      || c.Normalizado.Contains("presupuesto") || c.Normalizado.Contains("planific");
        var planEnCurso = ctx.Plan is not null && ctx.UltimaIntencion == NombresIntencion.PlanificarViaje;

        // Solo se consulta la base de datos si el mensaje tiene que ver con un plan de viaje
        if (!esNuevo && !planEnCurso)
        {
            ctx.Plan = null;
            return false;
        }

        var (monto, noches) = TextoChat.DatosDeViaje(c.Mensaje);
        var origenes = await OrigenesAsync(ct);
        var sinPasaje = TextoChat.SinPasaje(c.Normalizado);
        var origen = sinPasaje ? "" : origenes.Count > 0 ? TextoChat.BuscarPorNombre(c.Normalizado, origenes, o => o) : null;

        if (planEnCurso)
        {
            // Seguimiento: completar lo que faltaba o ajustar el plan ("¿y si tengo 8000?", "4 noches", "Más barato")
            var plan = ctx.Plan!;
            var cambio = false;
            if (monto is not null && monto != plan.Monto) { plan.Monto = monto; plan.Modo = "normal"; cambio = true; }
            if (noches is not null && noches != plan.Noches) { plan.Noches = noches; cambio = true; }
            if (origen is not null && origen != plan.Origen) { plan.Origen = origen; cambio = true; }

            if (!cambio && plan.Completo)
            {
                if (TextoChat.PideMasBarato(c.Normalizado)) { plan.Modo = "barato"; cambio = true; }
                else if (TextoChat.PideMenosNoches(c.Normalizado))
                {
                    var alcanza = await NochesQueAlcanzanAsync(plan, ct);
                    plan.Noches = alcanza is > 0 && alcanza < plan.Noches ? alcanza : Math.Max(1, plan.Noches!.Value - 1);
                    cambio = true;
                }
            }

            // Se le preguntó desde dónde viaja y respondió con una ciudad que no está registrada
            // ("salgo de Higüey", "Higüey"): BERT puede clasificarlo como otra cosa, por eso se mira la forma de la respuesta
            if (!cambio && plan.Monto.HasValue && plan.Noches.HasValue && plan.Origen is null
                && (c.Confianza < _umbralConfianza
                    || c.Intencion is NombresIntencion.FueraDeAlcance or NombresIntencion.PlanificarViaje
                    || TextoChat.PareceUnOrigen(c.Mensaje, c.Normalizado)))
            {
                Responder(r, TiposRespuesta.Aclaracion,
                    $"Todavía no tengo el precio del pasaje desde ese lugar. Tengo tarifas desde: {string.Join(", ", origenes)}. " +
                    "¿Desde cuál sales? Si ya estás en Jimaní o vas en tu propio vehículo, calculo el plan sin pasaje.");
                r.Sugerencias.AddRange(origenes.Take(5).Append(OpcionSinPasaje));
                return true;
            }

            if (!cambio && !esNuevo)
            {
                ctx.Plan = null;   // cambió de tema
                return false;
            }
        }
        else
        {
            ctx.Plan = new PlanViaje { Monto = monto, Noches = noches, Origen = origen };
        }

        var actual = ctx.Plan!;

        // Si no hay tarifas de pasaje registradas, se calcula sin pasaje
        if (actual.Origen is null && origenes.Count == 0)
            actual.Origen = "";

        if (!actual.Monto.HasValue)
        {
            Responder(r, TiposRespuesta.Aclaracion, "¡Con gusto te ayudo a planificar el viaje! ¿Cuánto dinero tienes para gastar? Por ejemplo: «10,000 pesos».");
            return true;
        }
        if (!actual.Noches.HasValue)
        {
            Responder(r, TiposRespuesta.Aclaracion, $"Perfecto, RD$ {TextoNumero(actual.Monto.Value)}. ¿Cuántas noches vas a pasar en Jimaní?");
            r.Sugerencias.AddRange(new[] { "2 noches", "3 noches", "5 noches" });
            return true;
        }
        if (actual.Origen is null)
        {
            Responder(r, TiposRespuesta.Aclaracion, "¿Desde dónde viajas? Así calculo también el pasaje de ida y vuelta.");
            r.Sugerencias.AddRange(origenes.Take(5).Append(OpcionSinPasaje));
            return true;
        }

        await ResponderPlanAsync(c, r, actual, ct);
        return true;
    }

    // Ciudades con pasaje registrado; primero las que tienen más opciones (suelen ser las más comunes)
    private async Task<List<string>> OrigenesAsync(CancellationToken ct) =>
        await _context.TarifasViaje.AsNoTracking()
            .Where(t => t.Activo)
            .GroupBy(t => t.Origen)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key)
            .ToListAsync(ct);

    // El procedimiento almacenado arma el presupuesto (pasaje, hospedaje, comida y transporte local).
    // Con monto escoge la combinación que cabe en ese dinero; si ninguna cabe, la más económica.
    private Task<List<LineaPresupuesto>> PresupuestoAsync(string? origen, int noches, string modo, decimal? monto, CancellationToken ct)
    {
        var parametroMonto = new SqlParameter("@Monto", SqlDbType.Decimal)
        {
            Precision = 12,
            Scale = 2,
            Value = (object?)monto ?? DBNull.Value
        };

        return _context.Database
            .SqlQuery<LineaPresupuesto>($"EXEC dbo.sp_PresupuestoViaje @Noches = {noches}, @Origen = {origen ?? ""}, @Modo = {modo}, @Monto = {parametroMonto}")
            .ToListAsync(ct);
    }

    // Cuántas noches cubre el dinero con las opciones más económicas
    private async Task<int?> NochesQueAlcanzanAsync(PlanViaje plan, CancellationToken ct)
    {
        if (!plan.Monto.HasValue) return null;
        var unaNoche = await PresupuestoAsync(plan.Origen, 1, "barato", null, ct);
        return NochesQueAlcanzan(unaNoche, 1, plan.Monto.Value);
    }

    // El pasaje se paga una vez; lo demás, por noche
    private static int? NochesQueAlcanzan(List<LineaPresupuesto> lineas, int noches, decimal monto)
    {
        var fijo = lineas.Where(l => l.IdPrestador == null).Sum(l => l.Subtotal);
        var porNoche = lineas.Where(l => l.IdPrestador != null).Sum(l => l.Subtotal) / noches;
        if (porNoche <= 0) return null;
        return (int)Math.Floor((monto - fijo) / porNoche);
    }

    private async Task ResponderPlanAsync(ConsultaChat c, RespuestaChat r, PlanViaje plan, CancellationToken ct)
    {
        var noches = plan.Noches!.Value;
        var monto = plan.Monto!.Value;
        var lineas = await PresupuestoAsync(plan.Origen, noches, plan.Modo, monto, ct);

        if (lineas.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados,
                "Todavía no tengo precios registrados de hospedaje, comida o transporte para armar el presupuesto.");
            c.Contexto.Plan = null;
            return;
        }

        var total = lineas.Sum(l => l.Subtotal);
        var cabe = total <= monto;
        var economico = plan.Modo == "barato";

        // Plan con las opciones más económicas: dice si se puede abaratar más y cuántas noches alcanzan
        var masBarato = await PresupuestoAsync(plan.Origen, noches, "barato", null, ct);

        // Si el plan habitual no cabía en el dinero, el procedimiento escogió opciones más baratas
        var ajustado = false;
        if (cabe && !economico)
        {
            var habitual = await PresupuestoAsync(plan.Origen, noches, "normal", null, ct);
            ajustado = habitual.Sum(l => l.Subtotal) > monto;
        }

        var desde = string.IsNullOrEmpty(plan.Origen) ? " sin contar pasaje" : $" saliendo de {plan.Origen}";
        var duracion = noches == 1 ? "1 día (1 noche)" : $"{noches} días ({noches} noches)";
        var dinero = $"RD$ {TextoNumero(monto)}";

        var introduccion =
            !cabe ? $"Con {dinero} no alcanza para {duracion}{desde}, ni con las opciones más económicas. Este es el plan más barato:"
            : ajustado ? $"¡Sí te alcanza! Escogí opciones más económicas para que el viaje de {duracion}{desde} quepa en tus {dinero}:"
            : economico ? $"Este es el plan más económico para {duracion}{desde} con {dinero}:"
            : $"¡Te alcanza! Este es tu plan para {duracion}{desde} con {dinero}:";

        var texto = new StringBuilder(introduccion).Append('\n');
        foreach (var l in lineas)
            texto.Append($"• {l.Concepto} – {l.Opcion}: RD$ {TextoNumero(l.Subtotal)}\n");

        var faltantes = new List<string>();
        if (!string.IsNullOrEmpty(plan.Origen) && lineas.All(l => l.Orden != 1)) faltantes.Add("el pasaje");
        if (lineas.All(l => l.Orden != 2)) faltantes.Add("el hospedaje");
        if (lineas.All(l => l.Orden != 3)) faltantes.Add("la comida");
        var aviso = faltantes.Count > 0 ? $"No tengo precios registrados para {string.Join(" ni ", faltantes)}." : null;
        if (aviso != null)
            texto.Append($"({aviso})\n");

        texto.Append($"\nTotal: RD$ {TextoNumero(total)}");

        if (cabe)
        {
            texto.Append(total == monto
                ? " → usas justo todo tu presupuesto. ¡Buen viaje!"
                : $" → te sobran RD$ {TextoNumero(monto - total)} para excursiones, guías y recuerdos. ¡Buen viaje!");
            if (!economico && masBarato.Sum(l => l.Subtotal) < total)
                r.Sugerencias.Add("Más barato");
            r.Sugerencias.AddRange(new[] { "¿Hay guías turísticos?", "¿Qué lugares puedo visitar?" });
        }
        else
        {
            texto.Append($" → te faltan RD$ {TextoNumero(total - monto)}.");

            // Opciones: menos noches (las que alcanzan con lo más económico) o más dinero
            var alcanza = NochesQueAlcanzan(masBarato, noches, monto);
            if (alcanza is > 0 && alcanza < noches)
            {
                texto.Append($" Con tu dinero alcanza para {alcanza} {(alcanza == 1 ? "noche" : "noches")}.");
                r.Sugerencias.Add(alcanza == 1 ? "Solo 1 noche" : $"Solo {alcanza} noches");
            }
            else
            {
                texto.Append(" Con los precios registrados no alcanza ni para una noche.");
            }

            // Monto redondeado a 500 que sí cubre el plan más barato
            var necesario = Math.Ceiling(total / 500m) * 500m;
            r.Sugerencias.Add($"Tengo RD$ {TextoNumero(necesario)}");
        }

        r.Presupuesto = new PresupuestoChat
        {
            Monto = monto,
            Total = total,
            Noches = noches,
            Origen = plan.Origen ?? "",
            Cabe = cabe,
            Ajustado = ajustado,
            Economico = economico,
            Lineas = lineas.Select(l => new LineaPresupuestoChat(ClaseLinea(l.Orden), l.Concepto, l.Opcion, DetalleLinea(l), l.Subtotal)
            {
                Url = l.IdPrestador is { } id ? $"/Sitio/Prestador/{id}" : null
            }).ToList()
        };

        // Fichas de los prestadores sugeridos (con su botón de contacto)
        var ids = lineas.Where(l => l.IdPrestador != null).Select(l => l.IdPrestador!.Value).ToList();
        var prestadores = await Proyectar(PrestadoresActivos().Where(p => ids.Contains(p.IdPrestador))).ToListAsync(ct);
        r.Resultados.AddRange(ids
            .Select(id => prestadores.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .Select(p => Tarjeta(ElementoPrestador(p!, true))));

        r.TipoRespuesta = TiposRespuesta.Datos;
        r.Respuesta = texto.ToString();
        r.TextoCorto = aviso is null ? introduccion : $"{introduccion}\n({aviso})";
    }

    // Orden que devuelve sp_PresupuestoViaje: 1 pasaje, 2 hospedaje, 3 comida, 4 transporte local
    private static string ClaseLinea(int orden) => orden switch
    {
        1 => "pasaje",
        2 => "hospedaje",
        3 => "comida",
        _ => "transporte"
    };

    // "RD$ 800 × 5 noches", "RD$ 75 × 15 comidas"...
    private static string DetalleLinea(LineaPresupuesto l)
    {
        var unidad = l.Orden switch
        {
            1 => "trayectos",
            2 => l.Cantidad == 1 ? "noche" : "noches",
            3 => "comidas",
            _ => "viajes"
        };
        return $"RD$ {TextoNumero(l.PrecioUnitario)} × {l.Cantidad} {unidad}";
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

    // Grupos de los tipos de servicio (con prestadores publicados) que nombra el mensaje; null si no nombra ninguno
    private async Task<HashSet<string>?> GruposDeTiposMencionadosAsync(ConsultaChat c, CancellationToken ct)
    {
        var filtro = TextoChat.PalabrasFiltro(c.Normalizado);
        if (filtro.Count == 0) return null;

        var tipos = await _context.TiposPrestador.AsNoTracking()
            .Where(t => t.Activo && t.Prestadores.Any(p => p.Activo))
            .Select(t => new { t.Nombre, t.Grupo })
            .ToListAsync(ct);

        var grupos = tipos
            .Where(t => TextoChat.PalabrasClave(TextoChat.Normalizar(t.Nombre)).Any(p => filtro.Any(f => TextoChat.CoincideRaiz(f, p))))
            .Select(t => t.Grupo)
            .ToHashSet();

        return grupos.Count > 0 ? grupos : null;
    }

    private static string? GrupoDeIntencion(string? intencion) => intencion switch
    {
        NombresIntencion.BuscarAlojamiento or NombresIntencion.ConsultarAlojamiento => GruposPrestador.Alojamiento,
        NombresIntencion.BuscarRestaurante or NombresIntencion.ConsultarRestaurante => GruposPrestador.Gastronomia,
        NombresIntencion.BuscarTransporte => GruposPrestador.Transporte,
        NombresIntencion.BuscarGuia => GruposPrestador.Guia,
        _ => null
    };

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

        // Las rutas unen lugares turísticos; para un prestador, o si pregunta "¿dónde queda…?", se da la ubicación
        if (destino is not null && (destino.Ref.EsPrestador || TextoChat.PideUbicacion(c.Normalizado)))
        {
            await DescribirUbicacionAsync(c, r, destino, ct, pidioUbicacion: true);
            return;
        }

        // "¿Cómo llego a X?" usa las rutas que terminan en X; "rutas desde X", las que salen de X
        var consulta = _context.Rutas.AsNoTracking().Where(x => x.Activa);
        if (destino is not null)
        {
            var id = destino.Ref.Id;
            consulta = TextoChat.PideComoLlegar(c.Normalizado) ? consulta.Where(x => x.IdLugarDestino == id)
                : TextoChat.PideSalida(c.Normalizado) ? consulta.Where(x => x.IdLugarOrigen == id)
                : consulta.Where(x => x.IdLugarDestino == id || x.IdLugarOrigen == id);
        }

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
        r.Resultados.AddRange(mostradas.Select((x, i) => new ResultadoChat(x.IdRuta, x.Nombre, "Ruta", ResumenRuta(x).Trim(' ', '—'))
        {
            Numero = i + 1,
            Origen = x.Origen,
            Destino = x.Destino
        }));
        r.TextoCorto = (destino is null ? "Estas son las rutas registradas:" : $"Estas son las rutas registradas relacionadas con {destino.Nombre}:")
                       + "\n\n¿Quieres los detalles de alguna? Toca la que te interese.";
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
            // "¿Qué es el larimar?" no nombra una ficha, pero puede responderse con un dato curioso
            if (!await ResponderConDatoAsync(c, r, fichas, ct))
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
            .Select(d => new { d.Titulo, d.Contenido })
            .ToListAsync(ct);

        var foto = await _context.Imagenes.AsNoTracking()
            .Where(i => i.IdLugar == lugar.IdLugar)
            .OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.OrdenVisualizacion)
            .Select(i => i.UrlImagen).FirstOrDefaultAsync(ct);

        var tipo = lugar.Atractivo?.TipoAtractivo ?? (lugar.Atractivo != null ? "Atractivo" : "Lugar de interés");
        var detalle = new DetalleChat
        {
            Clase = "lugar",
            Titulo = lugar.Nombre,
            Subtitulo = tipo,
            Imagen = SitioPublico.UrlSegura(foto),
            Icono = lugar.Atractivo != null ? "fa-mountain" : "fa-map-marker-alt",
            Resumen = string.IsNullOrWhiteSpace(lugar.DescripcionCorta) ? null : lugar.DescripcionCorta.Trim()
        };
        if (!string.IsNullOrWhiteSpace(lugar.Descripcion) && lugar.Descripcion.Trim() != lugar.DescripcionCorta?.Trim())
            Seccion(detalle, null, lugar.Descripcion, 700);

        if (lugar.Atractivo is { } a)
        {
            Dato(detalle, "fa-hiking", "Dificultad", a.NivelDificultad);
            Dato(detalle, "fa-clock", "Duración de la visita", TextoDuracion(a.DuracionVisitaMinutos));
            Dato(detalle, "fa-leaf", "Estado de conservación", a.EstadoConservacion);
            Seccion(detalle, "Información natural", a.InformacionNatural, 400);
            Seccion(detalle, "Información cultural", a.InformacionCultural, 400);
            Seccion(detalle, "Cómo acceder", a.InformacionAcceso, 400);
        }

        Dato(detalle, "fa-map-marker-alt", "Ubicación", Unir(", ", lugar.Direccion, lugar.Municipio, lugar.Provincia));

        if (lugar.Horarios.Count > 0)
        {
            Dato(detalle, "fa-door-open", "Horario de visita", string.Join("; ", lugar.Horarios.OrderBy(x => x.DiaSemana).Select(x =>
                $"{Formato.DiaSemana(x.DiaSemana)}: {(x.Cerrado ? "cerrado" : TextoHorario(x.HoraApertura, x.HoraCierre) ?? "abierto")}")));
        }

        var contactos = lugar.Contactos.OrderByDescending(x => x.EsPrincipal).Select(x => $"{x.TipoContacto}: {x.ValorContacto}").ToList();
        if (!string.IsNullOrWhiteSpace(lugar.Telefono)) contactos.Add($"Teléfono: {lugar.Telefono}");
        if (!string.IsNullOrWhiteSpace(lugar.Correo)) contactos.Add($"Correo: {lugar.Correo}");
        if (!string.IsNullOrWhiteSpace(lugar.SitioWeb)) contactos.Add($"Sitio web: {lugar.SitioWeb}");
        Dato(detalle, "fa-phone", "Contacto", string.Join("; ", contactos.Distinct()));

        Dato(detalle, "fa-tags", "Categorías", string.Join(", ", lugar.Categorias.Where(x => x.Activo).Select(x => x.Nombre)));
        Dato(detalle, "fa-concierge-bell", "Comodidades", string.Join(", ", lugar.Servicios.Where(x => x.Activo).Select(x => x.Nombre)));

        foreach (var documento in documentos)
            Seccion(detalle, documento.Titulo, documento.Contenido);

        // Un dato curioso que nombra el lugar; con otras palabras en la pregunta ("¿por qué es salado?"), el que mejor responde
        var datosDelLugar = (await DatosCuriososAsync(ct))
            .Where(d => TextoChat.BuscarPorNombre(TextoChat.Normalizar(d.Texto), new[] { lugar.Nombre }, n => n) != null)
            .ToList();
        if (ElegirDato(datosDelLugar, TextoChat.PalabrasTema(c.Normalizado), c.Contexto.DatosMostrados).Dato is { } dato)
        {
            c.Contexto.DatosMostrados.Add(dato.IdDato);
            Seccion(detalle, "Dato curioso", dato.Texto, 500);
        }

        Accion(detalle, "ficha", "Ver ficha", $"/Sitio/Lugar/{lugar.IdLugar}");
        Accion(detalle, "mapa", "Ver en el mapa", EnlaceMapa(lugar.Latitud, lugar.Longitud));
        Accion(detalle, "telefono", "Llamar", SitioPublico.EnlaceTelefono(lugar.Telefono));

        c.Contexto.RecordarLugar(Dominio.Atractivo, RefFicha.Lugar(lugar.IdLugar));
        r.Resultados.Add(Tarjeta(new ElementoLista(RefFicha.Lugar(lugar.IdLugar), lugar.Nombre, lugar.DescripcionCorta, null)
        {
            Subtitulo = tipo,
            Imagen = foto,
            Icono = detalle.Icono,
            Telefono = lugar.Telefono
        }));
        r.Sugerencias.AddRange(new[] { $"¿Cómo llego a {lugar.Nombre}?", "¿Hay guías turísticos?" });
        ResponderDetalle(r, detalle, $"Esto es lo que sé de {lugar.Nombre}:",
            "¿Quieres saber cómo llegar, buscar un guía o ver qué otros lugares hay cerca?");
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
            .Select(d => new { d.Titulo, d.Contenido })
            .ToListAsync(ct);

        var foto = await _context.Imagenes.AsNoTracking()
            .Where(i => i.IdPrestador == p.IdPrestador)
            .OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.OrdenVisualizacion)
            .Select(i => i.UrlImagen).FirstOrDefaultAsync(ct);

        var grupo = p.TipoPrestador?.Grupo;
        var dominio = DominioDeGrupo(grupo);

        var detalle = new DetalleChat
        {
            Clase = "prestador",
            Titulo = p.Nombre,
            Subtitulo = p.TipoPrestador?.Nombre,
            Imagen = SitioPublico.UrlSegura(foto),
            Icono = IconoPrestador(p.TipoPrestador?.Icono, grupo),
            Resumen = string.IsNullOrWhiteSpace(p.DescripcionCorta) ? null : p.DescripcionCorta.Trim()
        };
        if (!string.IsNullOrWhiteSpace(p.Descripcion) && p.Descripcion.Trim() != p.DescripcionCorta?.Trim())
            Seccion(detalle, null, p.Descripcion, 700);

        Dato(detalle, "fa-star", GruposPrestador.EtiquetaEspecialidad(grupo), p.Especialidad);
        Dato(detalle, "fa-money-bill-wave", "Precio", TextoPrecio(p.PrecioDesde, p.PrecioHasta));
        Dato(detalle, "fa-info-circle", "Sobre el precio", p.InformacionPrecio);
        Dato(detalle, "fa-users", GruposPrestador.EtiquetaCapacidad(grupo), p.Capacidad > 0 ? p.Capacidad.ToString() : null);
        Dato(detalle, "fa-map", "Zona de servicio", p.ZonaCobertura);
        Dato(detalle, "fa-clock", "Horario", p.HorarioAtencion);
        if (p.RequiereReserva) Dato(detalle, "fa-calendar-check", "Reserva", "Requiere reserva o cita");
        if (p.ServicioDomicilio) Dato(detalle, "fa-motorcycle", "A domicilio", "Ofrece servicio a domicilio");
        Dato(detalle, "fa-map-marker-alt", "Ubicación", Unir(", ", p.Direccion, p.Municipio, p.Provincia));

        var contactos = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Telefono)) contactos.Add($"Teléfono: {p.Telefono}");
        if (!string.IsNullOrWhiteSpace(p.WhatsApp)) contactos.Add($"WhatsApp: {p.WhatsApp}");
        if (!string.IsNullOrWhiteSpace(p.Correo)) contactos.Add($"Correo: {p.Correo}");
        if (!string.IsNullOrWhiteSpace(p.RedSocial)) contactos.Add($"Redes: {p.RedSocial}");
        if (!string.IsNullOrWhiteSpace(p.SitioWeb)) contactos.Add($"Sitio web: {p.SitioWeb}");
        Dato(detalle, "fa-phone", "Contacto", string.Join("; ", contactos));
        Dato(detalle, "fa-concierge-bell", "Comodidades", string.Join(", ", p.Servicios.Where(x => x.Activo).Select(x => x.Nombre)));

        foreach (var documento in documentos)
            Seccion(detalle, documento.Titulo, documento.Contenido);

        Accion(detalle, "whatsapp", "WhatsApp",
            SitioPublico.EnlaceWhatsApp(p.WhatsApp, $"Hola {p.Nombre}, le escribo desde el asistente de Turismo Jimaní."));
        Accion(detalle, "telefono", "Llamar", SitioPublico.EnlaceTelefono(p.Telefono));
        Accion(detalle, "ficha", "Ver perfil", $"/Sitio/Prestador/{p.IdPrestador}");
        Accion(detalle, "enlace", grupo == GruposPrestador.Gastronomia ? "Ver menú" : "Reservar", SitioPublico.UrlSegura(p.EnlaceExterno));
        Accion(detalle, "mapa", "Ver en el mapa", EnlaceMapa(p.Latitud, p.Longitud));

        c.Contexto.RecordarLugar(dominio, RefFicha.Prestador(p.IdPrestador));
        r.Resultados.Add(Tarjeta(new ElementoLista(RefFicha.Prestador(p.IdPrestador), p.Nombre, p.DescripcionCorta, null)
        {
            Subtitulo = p.TipoPrestador?.Nombre,
            Imagen = foto,
            Icono = detalle.Icono,
            WhatsApp = p.WhatsApp,
            Telefono = p.Telefono
        }));
        ResponderDetalle(r, detalle, $"Esta es la información de {p.Nombre}:", grupo switch
        {
            GruposPrestador.Alojamiento => "¿Quieres ver otros alojamientos o lugares cercanos para visitar?",
            GruposPrestador.Gastronomia => "¿Quieres ver otros lugares para comer o saber cómo llegar?",
            GruposPrestador.Guia => "¿Quieres ver otros guías o los atractivos que puedes visitar?",
            _ => "¿Quieres ver otras opciones parecidas?"
        });
    }

    // pidioUbicacion: respuesta a "¿dónde queda…?" (si no, se explica que no hay ruta registrada hacia el lugar)
    private async Task DescribirUbicacionAsync(ConsultaChat c, RespuestaChat r, FichaResumen destino, CancellationToken ct,
        bool pidioUbicacion = false)
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

        var directa = destino.Ref.EsPrestador || pidioUbicacion;
        var texto = new StringBuilder(directa
            ? $"Esta es la ubicación registrada de {destino.Nombre}"
            : $"No tengo una ruta registrada hacia {destino.Nombre}");
        texto.Append(string.IsNullOrEmpty(ubicacionTexto)
            ? (directa ? ": no tiene dirección registrada." : ".")
            : directa ? $": {ubicacionTexto}." : $", pero según los datos registrados se encuentra en {ubicacionTexto}.");

        var coordenadas = Coordenadas(destino.Latitud, destino.Longitud);
        if (coordenadas != null)
            texto.Append($"\n\nCoordenadas: {coordenadas}\nPuedes abrirlas en un mapa: {EnlaceMapa(destino.Latitud, destino.Longitud)}");

        var hayTransporte = await _context.Prestadores.AnyAsync(x => x.Activo && x.TipoPrestador!.Grupo == GruposPrestador.Transporte, ct);
        if (hayTransporte)
        {
            texto.Append("\n\nSi quieres, también puedo mostrarte los servicios de transporte registrados.");
            r.Sugerencias.Add("Necesito transporte");
        }

        var detalle = new DetalleChat
        {
            Clase = "ubicacion",
            Titulo = destino.Nombre,
            Subtitulo = "Ubicación",
            Icono = "fa-map-marked-alt",
            Resumen = directa ? null : "Todavía no hay una ruta registrada hacia este lugar."
        };
        Dato(detalle, "fa-map-marker-alt", "Dirección", string.IsNullOrEmpty(ubicacionTexto) ? "Sin dirección registrada" : ubicacionTexto);
        Dato(detalle, "fa-crosshairs", "Coordenadas", coordenadas);
        Accion(detalle, "mapa", "Abrir en Google Maps", EnlaceMapa(destino.Latitud, destino.Longitud));
        Accion(detalle, "ficha", destino.Ref.EsPrestador ? "Ver perfil" : "Ver ficha",
            destino.Ref.EsPrestador ? $"/Sitio/Prestador/{destino.Ref.Id}" : $"/Sitio/Lugar/{destino.Ref.Id}");

        c.Contexto.RecordarLugar(null, destino.Ref);
        r.Resultados.Add(new ResultadoChat(destino.Ref.Id, destino.Nombre, destino.Ref.EsPrestador ? "Prestador" : "Lugar", ubicacionTexto));
        r.Detalle = detalle;
        r.TextoCorto = directa
            ? $"Esta es la ubicación de {destino.Nombre}:"
            : $"No tengo una ruta registrada hacia {destino.Nombre}, pero te muestro dónde queda:";
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
        var detalle = new DetalleChat
        {
            Clase = "ruta",
            Titulo = ruta.Nombre,
            Subtitulo = "Ruta",
            Icono = "fa-route",
            Origen = ruta.Origen,
            Destino = ruta.Destino,
            MapaRuta = SitioPublico.UrlMapaRuta(ruta.OrigenLatitud, ruta.OrigenLongitud,
                ruta.DestinoLatitud, ruta.DestinoLongitud, ruta.TipoTransporte)
        };
        Dato(detalle, "fa-road", "Distancia", ruta.DistanciaKilometros.HasValue ? $"{TextoNumero(ruta.DistanciaKilometros.Value)} km" : null);
        Dato(detalle, "fa-clock", "Duración aproximada", TextoDuracion(ruta.DuracionMinutos));
        Dato(detalle, "fa-hiking", "Dificultad", ruta.NivelDificultad);
        Dato(detalle, IconoTransporte(ruta.TipoTransporte), "Transporte", ruta.TipoTransporte);
        Seccion(detalle, null, ruta.Descripcion);
        Seccion(detalle, "Instrucciones", ruta.Instrucciones, 700);

        Accion(detalle, "mapa", "Indicaciones en Google Maps", EnlaceIndicaciones(
            Coordenadas(ruta.OrigenLatitud, ruta.OrigenLongitud), Coordenadas(ruta.DestinoLatitud, ruta.DestinoLongitud)));
        if (ruta.IdLugarDestino is { } idDestino)
            Accion(detalle, "ficha", "Ver destino", $"/Sitio/Lugar/{idDestino}");

        c.Contexto.RecordarRutas(new[] { ruta.IdRuta });
        if (ruta.IdLugarDestino is { } destino)
            c.Contexto.LugarEnFoco = RefFicha.Lugar(destino);

        r.Resultados.Add(new ResultadoChat(ruta.IdRuta, ruta.Nombre, "Ruta", ResumenRuta(ruta).Trim(' ', '—')));
        if (ruta.Destino != null)
            r.Sugerencias.Add($"Háblame de {ruta.Destino}");
        ResponderDetalle(r, detalle, $"Esta es la ruta «{ruta.Nombre}»:", null);
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
        texto.AppendLine("• ¿Qué eventos hay este mes?");
        texto.AppendLine("• Tengo 10,000 pesos para 3 días");
        texto.AppendLine("• Cuéntame un dato curioso");
        if (ejemploAtractivo != null)
        {
            texto.AppendLine($"• Háblame de {ejemploAtractivo}");
            texto.AppendLine($"• ¿Cómo llego a {ejemploAtractivo}?");
        }

        Responder(r, TiposRespuesta.Conversacional, texto.ToString().Trim());
    }

    // ------------------------------------------------------------------ eventos

    // Eventos publicados en el sitio (también los pasados, para responder "ese evento ya pasó")
    private async Task<List<EventoResumen>> CargarEventosAsync(CancellationToken ct) =>
        await _context.Eventos.AsNoTracking()
            .Where(e => e.Publicado)
            .Select(e => new EventoResumen(e.IdEvento, e.Titulo, e.Tipo, e.Resumen, e.FechaInicio, e.FechaFin, e.IdLugar,
                e.Lugar != null ? e.Lugar.Nombre : e.LugarTexto, e.ImagenUrl))
            .ToListAsync(ct);

    private async Task BuscarEventosAsync(ConsultaChat c, RespuestaChat r, List<EventoResumen> eventos,
        EventoResumen? mencionado, FichaResumen? ficha, CancellationToken ct)
    {
        var ctx = c.Contexto;

        // "El 2" sobre la última lista de eventos
        if (mencionado is null && ctx.Dominio == Dominio.Evento
            && TextoChat.ObtenerOrdinal(c.Normalizado, ctx.UltimosEventos.Count) is { } indice)
            mencionado = eventos.FirstOrDefault(e => e.IdEvento == ctx.UltimosEventos[indice]);

        if (mencionado is not null)
        {
            await DescribirEventoAsync(c, r, mencionado.IdEvento, ct);
            return;
        }

        var hoy = DateTime.Today;
        var proximos = eventos.Where(e => (e.FechaFin ?? e.FechaInicio).Date >= hoy).OrderBy(e => e.FechaInicio).ToList();
        if (proximos.Count == 0)
        {
            Responder(r, TiposRespuesta.SinResultados,
                "Por ahora no hay eventos próximos publicados. ¿Quieres que te muestre los lugares que puedes visitar?");
            r.Sugerencias.AddRange(new[] { "¿Qué lugares puedo visitar?", "Cuéntame un dato curioso" });
            return;
        }

        // Filtros del mensaje: tipo ("promociones"), periodo ("este mes") y lugar o prestador ("en el lago")
        var elegidos = proximos;
        var tipo = TextoChat.TipoEventoPedido(c.Normalizado);
        var descripcion = tipo switch
        {
            null => "eventos",
            "Promoción" => "promociones",
            _ => $"eventos de tipo «{tipo}»"
        };
        if (tipo != null)
            elegidos = elegidos.Where(e => e.Tipo == tipo).ToList();

        if (TextoChat.PeriodoPedido(c.Normalizado, hoy) is { } periodo)
        {
            elegidos = elegidos.Where(e => e.FechaInicio.Date <= periodo.Hasta && (e.FechaFin ?? e.FechaInicio).Date >= periodo.Desde).ToList();
            descripcion += " " + periodo.Texto;
        }

        if (ficha is not null)
        {
            var nombre = TextoChat.Normalizar(ficha.Nombre);
            elegidos = elegidos.Where(e => (!ficha.Ref.EsPrestador && e.IdLugar == ficha.Ref.Id)
                                           || TextoChat.Normalizar(e.Titulo).Contains(nombre)).ToList();
            descripcion += $" en {ficha.Nombre}";
        }

        var filtrado = elegidos != proximos;
        if (filtrado && elegidos.Count == 1)
        {
            await DescribirEventoAsync(c, r, elegidos[0].IdEvento, ct);
            return;
        }

        string introduccion;
        if (filtrado && elegidos.Count == 0)
        {
            introduccion = $"No encontré {descripcion}. Estos son los próximos eventos en Jimaní:";
            elegidos = proximos;
        }
        else
        {
            introduccion = filtrado ? $"Estos son los {descripcion} que tengo registrados:" : "Estos son los próximos eventos en Jimaní:";
        }

        var mostrados = elegidos.Take(MaximoResultados).ToList();
        var texto = new StringBuilder(introduccion).Append('\n');
        for (var i = 0; i < mostrados.Count; i++)
        {
            var e = mostrados[i];
            texto.Append($"{i + 1}. {e.Titulo} — {TextoFechaEvento(e.FechaInicio, e.FechaFin)}{(e.Lugar != null ? $", {e.Lugar}" : "")}\n");
        }
        texto.Append("\n¿Quieres los detalles de alguno? Por ejemplo, escribe «el 1».");

        ctx.RecordarEventos(mostrados.Select(e => e.IdEvento));
        r.Resultados.AddRange(mostrados.Select((e, i) => FichaEvento(e) with { Numero = i + 1 }));
        r.TextoCorto = $"{introduccion}\n\n¿Quieres los detalles de alguno?";
        Responder(r, TiposRespuesta.Datos, texto.ToString(), limpiarResultados: false);
    }

    private async Task DescribirEventoAsync(ConsultaChat c, RespuestaChat r, int idEvento, CancellationToken ct)
    {
        var evento = await _context.Eventos.AsNoTracking()
            .Include(e => e.Lugar)
            .FirstOrDefaultAsync(e => e.IdEvento == idEvento && e.Publicado, ct);

        if (evento is null)
        {
            Responder(r, TiposRespuesta.SinResultados, "No encontré información publicada sobre ese evento.");
            return;
        }

        var lugar = evento.Lugar?.Nombre ?? evento.LugarTexto;
        var pasado = (evento.FechaFin ?? evento.FechaInicio).Date < DateTime.Today;
        var cultura = CultureInfo.CurrentCulture;

        var detalle = new DetalleChat
        {
            Clase = "evento",
            Titulo = evento.Titulo,
            Subtitulo = evento.Tipo,
            Imagen = SitioPublico.UrlSegura(evento.ImagenUrl),
            Icono = "fa-calendar-alt",
            Resumen = string.IsNullOrWhiteSpace(evento.Resumen) ? null : evento.Resumen.Trim(),
            Fecha = new FechaDetalle(evento.FechaInicio.ToString("ddd", cultura).TrimEnd('.'),
                evento.FechaInicio.Day.ToString(), evento.FechaInicio.ToString("MMM", cultura).TrimEnd('.'))
        };
        Dato(detalle, "fa-calendar-alt", "Fecha", TextoFechaEvento(evento.FechaInicio, evento.FechaFin));
        Dato(detalle, "fa-map-marker-alt", "Lugar", lugar);
        Dato(detalle, "fa-users", "Organiza", evento.Organizador);
        if (pasado)
            Dato(detalle, "fa-history", "Estado", "Este evento ya pasó");
        Seccion(detalle, null, evento.Contenido);

        Accion(detalle, "ficha", "Ver evento", $"/Sitio/Evento/{evento.IdEvento}");
        Accion(detalle, "enlace", "Más información", SitioPublico.UrlSegura(evento.EnlaceExterno));
        Accion(detalle, "mapa", "Ver en el mapa", EnlaceMapa(evento.Lugar?.Latitud, evento.Lugar?.Longitud));

        c.Contexto.RecordarEventos(new[] { evento.IdEvento });
        if (evento.Lugar is { } lugarRegistrado)
        {
            // "¿Cómo llego?" después del evento se refiere a su lugar
            c.Contexto.LugarEnFoco = RefFicha.Lugar(lugarRegistrado.IdLugar);
            r.Sugerencias.Add($"¿Cómo llego a {lugarRegistrado.Nombre}?");
        }
        r.Sugerencias.Add("¿Qué otros eventos hay?");

        r.Resultados.Add(FichaEvento(new EventoResumen(evento.IdEvento, evento.Titulo, evento.Tipo, evento.Resumen,
            evento.FechaInicio, evento.FechaFin, evento.IdLugar, lugar, evento.ImagenUrl)));
        ResponderDetalle(r, detalle,
            pasado ? "Este evento ya pasó; estos fueron sus detalles:" : "Estos son los detalles del evento:",
            pasado ? "¿Quieres ver los próximos eventos?" : "¿Quieres ver otros eventos o saber cómo llegar?");
    }

    private static ResultadoChat FichaEvento(EventoResumen e) =>
        new(e.IdEvento, e.Titulo, "Evento", e.Resumen)
        {
            Subtitulo = $"{TextoFechaEvento(e.FechaInicio, e.FechaFin)} · {e.Tipo}",
            Imagen = SitioPublico.UrlSegura(e.ImagenUrl),
            Icono = "fa-calendar-alt",
            Url = $"/Sitio/Evento/{e.IdEvento}"
        };

    // "sábado 17 de octubre, 10:00 a. m." o "del 17 al 18 de octubre"
    private static string TextoFechaEvento(DateTime inicio, DateTime? fin)
    {
        var cultura = CultureInfo.CurrentCulture;
        var anio = inicio.Year != DateTime.Today.Year ? $" de {inicio.Year}" : "";

        if (fin is { } f && f.Date != inicio.Date)
        {
            return f.Month == inicio.Month
                ? $"del {inicio.Day} al {f.Day} de {f.ToString("MMMM", cultura)}{anio}"
                : $"del {inicio.ToString("d 'de' MMMM", cultura)} al {f.ToString("d 'de' MMMM", cultura)}{anio}";
        }

        var texto = inicio.ToString("dddd d 'de' MMMM", cultura) + anio;
        return inicio.TimeOfDay == TimeSpan.Zero ? texto : $"{texto}, {inicio.ToString("h:mm tt", cultura)}";
    }

    // ------------------------------------------------------------------ datos curiosos

    private Task<List<DatoCurioso>> DatosCuriososAsync(CancellationToken ct) =>
        _context.DatosCuriosos.AsNoTracking().Where(d => d.Activo).ToListAsync(ct);

    // El dato que mejor coincide con el tema de la pregunta (DelTema = true) o, si ninguno coincide, uno al azar.
    // Prefiere los que todavía no se contaron en la conversación.
    private static (DatoCurioso? Dato, bool DelTema) ElegirDato(List<DatoCurioso> datos, List<string> tema, HashSet<int> mostrados)
    {
        if (datos.Count == 0)
            return (null, false);

        var nuevos = datos.Where(d => !mostrados.Contains(d.IdDato)).ToList();
        if (nuevos.Count == 0)
            nuevos = datos;

        if (tema.Count > 0)
        {
            var mejor = nuevos
                .Select(d => (Dato: d, Puntos: TextoChat.Coincidencias(tema, d.Texto + " " + d.Categoria)))
                .Where(x => x.Puntos > 0)
                .OrderByDescending(x => x.Puntos).ThenBy(_ => Random.Shared.Next())
                .FirstOrDefault();
            if (mejor.Dato is not null)
                return (mejor.Dato, true);
        }

        return (nuevos[Random.Shared.Next(nuevos.Count)], false);
    }

    private async Task ContarDatoCuriosoAsync(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas, CancellationToken ct)
    {
        var datos = await DatosCuriososAsync(ct);
        var mostrados = c.Contexto.DatosMostrados;
        if (datos.All(d => mostrados.Contains(d.IdDato)))
            mostrados.Clear();   // ya se contaron todos: se empieza otra vuelta

        var tema = TextoChat.PalabrasTema(c.Normalizado);
        var (dato, delTema) = ElegirDato(datos, tema, mostrados);
        if (dato is null)
        {
            Responder(r, TiposRespuesta.SinResultados, "Todavía no tengo datos curiosos registrados.");
            return;
        }

        ResponderDato(c, r, fichas, dato, tema.Count > 0 && !delTema ? "No tengo un dato sobre eso, pero aquí va otro. " : "Dato curioso: ");
    }

    // Si todas las palabras distintivas de la pregunta están en un dato curioso, responde con él
    private async Task<bool> ResponderConDatoAsync(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas, CancellationToken ct)
    {
        var tema = TextoChat.PalabrasDistintivas(c.Normalizado);
        if (tema.Count == 0)
            return false;

        var dato = (await DatosCuriososAsync(ct))
            .Where(d => TextoChat.Coincidencias(tema, d.Texto) == tema.Count)
            .OrderBy(d => c.Contexto.DatosMostrados.Contains(d.IdDato))
            .FirstOrDefault();
        if (dato is null)
            return false;

        r.Intencion = NombresIntencion.DatoCurioso;
        ResponderDato(c, r, fichas, dato, "Dato curioso: ");
        return true;
    }

    private static void ResponderDato(ConsultaChat c, RespuestaChat r, List<FichaResumen> fichas, DatoCurioso dato, string introduccion)
    {
        c.Contexto.DatosMostrados.Add(dato.IdDato);
        var texto = new StringBuilder(introduccion).Append(dato.Texto.Trim());
        if (!string.IsNullOrWhiteSpace(dato.Fuente))
            texto.Append("\n\nFuente: " + dato.Fuente.Trim());

        r.Sugerencias.Add("Otro dato curioso");
        // Si el dato nombra un lugar turístico registrado, se ofrece su información
        if (TextoChat.BuscarPorNombre(TextoChat.Normalizar(dato.Texto), fichas.Where(f => !f.Ref.EsPrestador), f => f.Nombre) is { } lugar)
            r.Sugerencias.Add($"Háblame de {lugar.Nombre}");

        var detalle = new DetalleChat
        {
            Clase = "dato",
            Titulo = "¿Sabías que…?",
            Subtitulo = dato.Categoria,
            Icono = "fa-lightbulb",
            Resumen = dato.Texto.Trim()
        };
        // La fuente es un enlace si es una dirección web; si no, se muestra como texto
        if (SitioPublico.UrlSegura(dato.Fuente) is { } fuente && fuente.StartsWith("http"))
            Accion(detalle, "enlace", "Ver la fuente", fuente);
        else
            Dato(detalle, "fa-book", "Fuente", dato.Fuente);

        r.Detalle = detalle;
        r.TextoCorto = introduccion.StartsWith("No tengo") ? introduccion.Trim() : "Aquí tienes un dato curioso:";
        Responder(r, TiposRespuesta.Conversacional, texto.ToString());
    }

    // ------------------------------------------------------------------ tarjetas de detalle

    private static void Dato(DetalleChat d, string icono, string etiqueta, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
            d.Datos.Add(new DatoDetalle(icono, etiqueta, valor.Trim()));
    }

    private static void Seccion(DetalleChat d, string? titulo, string? texto, int maximo = 600)
    {
        if (!string.IsNullOrWhiteSpace(texto))
            d.Secciones.Add(new SeccionDetalle(titulo, TextoChat.Recortar(texto, maximo)));
    }

    private static void Accion(DetalleChat d, string tipo, string texto, string? url)
    {
        if (url != null)
            d.Acciones.Add(new AccionDetalle(tipo, texto, url));
    }

    // Texto completo de la tarjeta: se guarda en ConsultasAsistente y es lo que se lee si la tarjeta no se dibuja
    private static string TextoDeDetalle(DetalleChat d, string? cierre)
    {
        var texto = new StringBuilder(d.Subtitulo != null && d.Clase is "lugar" or "prestador" or "evento"
            ? $"{d.Titulo} — {d.Subtitulo}"
            : d.Titulo).AppendLine();

        if (d.Resumen != null)
            texto.AppendLine(d.Resumen);
        foreach (var s in d.Secciones.Where(s => s.Titulo == null))
            texto.AppendLine(s.Texto);

        var lineas = new List<string>();
        if (d.Origen != null || d.Destino != null)
            lineas.Add($"Recorrido: {d.Origen ?? "—"} → {d.Destino ?? "—"}");
        lineas.AddRange(d.Datos.Select(x => $"{x.Etiqueta}: {x.Valor}"));
        lineas.AddRange(d.Acciones.Where(a => a.Tipo is "mapa" or "enlace").Select(a => $"{a.Texto}: {a.Url}"));
        if (lineas.Count > 0)
            texto.AppendLine().AppendLine(string.Join("\n", lineas.Select(l => "• " + l)));

        foreach (var s in d.Secciones.Where(s => s.Titulo != null))
            texto.AppendLine().AppendLine($"{s.Titulo}: {s.Texto}");

        if (cierre != null)
            texto.AppendLine().Append(cierre);
        return texto.ToString().Trim();
    }

    private static void ResponderDetalle(RespuestaChat r, DetalleChat d, string textoCorto, string? cierre)
    {
        r.Detalle = d;
        r.TextoCorto = textoCorto;
        Responder(r, TiposRespuesta.Datos, TextoDeDetalle(d, cierre), limpiarResultados: false);
    }

    private static string? Coordenadas(decimal? latitud, decimal? longitud) =>
        latitud.HasValue && longitud.HasValue
            ? $"{latitud.Value.ToString("0.######", Invariante)},{longitud.Value.ToString("0.######", Invariante)}"
            : null;

    private static string? EnlaceMapa(decimal? latitud, decimal? longitud) =>
        Coordenadas(latitud, longitud) is { } punto ? "https://www.google.com/maps?q=" + punto : null;

    // Indicaciones de Google Maps de un lugar a otro (o solo hasta el destino)
    private static string? EnlaceIndicaciones(string? origen, string? destino) =>
        destino is null ? null
            : "https://www.google.com/maps/dir/?api=1" + (origen != null ? "&origin=" + origen : "") + "&destination=" + destino;

    private static string IconoTransporte(string? transporte)
    {
        var t = TextoChat.Normalizar(transporte ?? "");
        return t.Contains("bote") || t.Contains("lancha") ? "fa-ship"
            : t.Contains("moto") ? "fa-motorcycle"
            : t.Contains("guagua") || t.Contains("autobus") ? "fa-bus"
            : t.Contains("caminata") || t.Contains("pie") || t.Contains("sendero") ? "fa-walking"
            : "fa-car";
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
            x.DistanciaKilometros, x.DuracionMinutos, x.NivelDificultad, x.TipoTransporte, x.Instrucciones,
            x.LugarOrigen != null ? x.LugarOrigen.Latitud : null,
            x.LugarOrigen != null ? x.LugarOrigen.Longitud : null,
            x.LugarDestino != null ? x.LugarDestino.Latitud : null,
            x.LugarDestino != null ? x.LugarDestino.Longitud : null));

    // Último plan de viaje que el asistente mostró en la sesión (null si no hay o la sesión expiró)
    public PresupuestoChat? UltimoPresupuesto(Guid idSesion) =>
        _cache.TryGetValue($"chat:{idSesion}", out ContextoConversacion? contexto) ? contexto?.UltimoPresupuesto : null;

    private async Task<long?> RegistrarConsultaAsync(string mensaje, RespuestaChat respuesta, int? idUsuario, int milisegundos)
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
                IdUsuario = idUsuario,
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
        Dominio.Evento => NombresIntencion.BuscarEvento,
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
        Dominio.Evento => "eventos",
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
        string? TipoTransporte, string? Instrucciones,
        decimal? OrigenLatitud, decimal? OrigenLongitud, decimal? DestinoLatitud, decimal? DestinoLongitud);

    // Lugar: nombre del lugar registrado o el texto libre del evento
    private sealed record EventoResumen(int IdEvento, string Titulo, string Tipo, string? Resumen, DateTime FechaInicio,
        DateTime? FechaFin, int? IdLugar, string? Lugar, string? ImagenUrl);

    private sealed record ElementoLista(RefFicha Ref, string Nombre, string? Detalle, decimal? Precio, double? DistanciaKm = null)
    {
        public string? Subtitulo { get; init; }
        public string? Imagen { get; init; }
        public string? Icono { get; init; }
        public string? WhatsApp { get; init; }
        public string? Telefono { get; init; }
    }
}
