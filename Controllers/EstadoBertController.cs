using AppDonnyCuevas20210074.Services.Asistente;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppDonnyCuevas20210074.Controllers;

// Panel con el estado del servicio BERT: memoria, CPU, modelo cargado y actividad
[Authorize]
public class EstadoBertController : Controller
{
    private readonly IClasificadorIntenciones _clasificador;

    public EstadoBertController(IClasificadorIntenciones clasificador)
    {
        _clasificador = clasificador;
    }

    // GET: /EstadoBert
    public IActionResult Index()
    {
        return View();
    }

    // GET: /EstadoBert/Datos  -> lo consulta la página cada pocos segundos
    [HttpGet]
    public async Task<IActionResult> Datos(CancellationToken ct)
    {
        var json = await _clasificador.ObtenerEstadoJsonAsync(ct);
        Response.Headers.CacheControl = "no-store";

        return json is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { disponible = false })
            : Content(json, "application/json");
    }
}
