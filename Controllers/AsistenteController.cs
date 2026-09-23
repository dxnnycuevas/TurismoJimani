using AppDonnyCuevas20210074.Models.Asistente;
using AppDonnyCuevas20210074.Services.Asistente;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppDonnyCuevas20210074.Controllers;

// Chat del asistente turístico. Es público para que los visitantes puedan usarlo sin iniciar sesión.
[AllowAnonymous]
public class AsistenteController : Controller
{
    private const int LongitudMaximaMensaje = 500;

    private readonly ChatbotService _chatbot;
    private readonly IClasificadorIntenciones _clasificador;
    private readonly ILogger<AsistenteController> _logger;

    public AsistenteController(ChatbotService chatbot, IClasificadorIntenciones clasificador, ILogger<AsistenteController> logger)
    {
        _chatbot = chatbot;
        _clasificador = clasificador;
        _logger = logger;
    }

    // GET: /Asistente
    public IActionResult Index()
    {
        return View();
    }

    // POST: /Asistente/EnviarMensaje   { "mensaje": "Quiero un hotel", "idSesion": "..." }
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("chat")]
    public async Task<IActionResult> EnviarMensaje([FromBody] SolicitudChat? solicitud, CancellationToken ct)
    {
        var mensaje = solicitud?.Mensaje?.Trim();

        if (string.IsNullOrEmpty(mensaje))
            return BadRequest(new { error = "Escribe un mensaje antes de enviarlo." });

        if (mensaje.Length > LongitudMaximaMensaje)
            return BadRequest(new { error = $"El mensaje es demasiado largo (máximo {LongitudMaximaMensaje} caracteres)." });

        var idSesion = Guid.TryParse(solicitud!.IdSesion, out var id) && id != Guid.Empty ? id : Guid.NewGuid();

        try
        {
            var respuesta = await _chatbot.ProcesarAsync(mensaje, idSesion, ct);
            return Ok(respuesta);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new EmptyResult(); // el usuario cerró la página
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al procesar un mensaje del chat");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Ocurrió un error inesperado. Intenta de nuevo." });
        }
    }

    // GET: /Asistente/Estado  -> indica si el servicio BERT está disponible
    [HttpGet]
    public async Task<IActionResult> Estado(CancellationToken ct)
    {
        return Ok(new { disponible = await _clasificador.EstaDisponibleAsync(ct) });
    }
}
