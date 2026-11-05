using System.Text.Json;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using AppDonnyCuevas20210074.Models.Asistente;
using AppDonnyCuevas20210074.Models.Sitio;
using AppDonnyCuevas20210074.Services.Asistente;
using AppDonnyCuevas20210074.Services.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

// Mi perfil del visitante (IV-20): favoritos, planes de viaje guardados desde el chat,
// historial de conversaciones con el asistente (con borrado total) y eliminación de la cuenta.
// [AllowAnonymous] evita la política global del panel (solo personal); cada acción exige
// una sesión de visitante con SesionVisitante.IdUsuario.
[AllowAnonymous]
public class PerfilController : Controller
{
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    private readonly TurismoJimaniContext _context;
    private readonly ChatbotService _chatbot;
    private readonly ProteccionAcceso _proteccion;
    private readonly PasswordHasher<Usuario> _hasher = new();

    public PerfilController(TurismoJimaniContext context, ChatbotService chatbot, ProteccionAcceso proteccion)
    {
        _context = context;
        _chatbot = chatbot;
        _proteccion = proteccion;
    }

    // Sin sesión de visitante: al inicio de sesión del sitio; el personal usa el panel
    private IActionResult SinVisitante(string? volver = null) =>
        RolesSistema.EsPersonal(User)
            ? RedirectToAction("Index", "Home")
            : RedirectToAction("Ingresar", "Cuenta", new { returnUrl = volver ?? Url.Action(nameof(Index)) });

    private IActionResult Volver(string seccion, string mensaje)
    {
        TempData["Exito"] = mensaje;
        return RedirectToAction(nameof(Index), new { seccion });
    }

    // GET: /Perfil
    [HttpGet]
    public async Task<IActionResult> Index(string? seccion = null)
    {
        if (SesionVisitante.IdUsuario(User) is not int id) return SinVisitante();

        var usuario = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdUsuario == id && u.Activo);
        if (usuario == null) return SinVisitante();

        // Favoritos publicados, del más reciente al más antiguo
        var favoritos = _context.Favoritos.Where(f => f.IdUsuario == id);

        var lugares = await _context.Lugares.AsNoTracking()
            .Where(l => l.Activo && favoritos.Any(f => f.IdLugar == l.IdLugar))
            .OrderByDescending(l => favoritos.Where(f => f.IdLugar == l.IdLugar).Max(f => f.FechaCreacion))
            .Include(l => l.Atractivo).Include(l => l.Imagenes)
            .ToListAsync();
        var prestadores = await _context.Prestadores.AsNoTracking()
            .Where(p => p.Activo && favoritos.Any(f => f.IdPrestador == p.IdPrestador))
            .OrderByDescending(p => favoritos.Where(f => f.IdPrestador == p.IdPrestador).Max(f => f.FechaCreacion))
            .Include(p => p.TipoPrestador).Include(p => p.Imagenes)
            .ToListAsync();

        var planes = (await _context.PlanesGuardados.AsNoTracking()
                .Where(p => p.IdUsuario == id)
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync())
            .Select(p => new PlanPerfil
            {
                Id = p.IdPlan, Fecha = p.FechaCreacion, Origen = p.Origen, Monto = p.Monto, Total = p.Total,
                Noches = p.Noches, Cabe = p.Cabe,
                Lineas = JsonSerializer.Deserialize<List<LineaPresupuestoChat>>(p.Detalle, OpcionesJson) ?? new()
            })
            .ToList();

        var mensajes = await _context.ConsultasAsistente.AsNoTracking()
            .Where(c => c.IdUsuario == id)
            .OrderBy(c => c.FechaConsulta)
            .ToListAsync();

        var conversaciones = mensajes
            .GroupBy(c => c.IdSesion)
            .Select(g => new ConversacionPerfil
            {
                IdSesion = g.Key,
                Inicio = g.First().FechaConsulta,
                Fin = g.Last().FechaConsulta,
                Mensajes = g.ToList()
            })
            .OrderByDescending(c => c.Fin)
            .ToList();

        return View(new PerfilViewModel
        {
            Nombre = usuario.NombreCompleto,
            Correo = usuario.Correo,
            Registro = usuario.FechaCreacion,
            LugaresFavoritos = lugares,
            PrestadoresFavoritos = prestadores,
            Planes = planes,
            Conversaciones = conversaciones,
            TotalMensajes = mensajes.Count,
            Seccion = seccion is "planes" or "conversaciones" or "cuenta" ? seccion : "favoritos"
        });
    }

    // POST: /Perfil/Favorito  (guarda o quita un lugar o prestador; se usa desde las fichas y el perfil)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Favorito(string tipo, int id, string? returnUrl = null)
    {
        if (SesionVisitante.IdUsuario(User) is not int idUsuario) return SinVisitante(returnUrl);

        var esPrestador = tipo == "prestador";
        var existe = esPrestador
            ? await _context.Prestadores.AnyAsync(p => p.IdPrestador == id && p.Activo)
            : await _context.Lugares.AnyAsync(l => l.IdLugar == id && l.Activo);
        if (!existe) return NotFound();

        var actual = await _context.Favoritos.FirstOrDefaultAsync(f => f.IdUsuario == idUsuario
            && (esPrestador ? f.IdPrestador == id : f.IdLugar == id));

        if (actual != null)
        {
            _context.Favoritos.Remove(actual);
            TempData["Exito"] = "Se quitó de tus favoritos.";
        }
        else
        {
            _context.Favoritos.Add(new Favorito
            {
                IdUsuario = idUsuario,
                IdLugar = esPrestador ? null : id,
                IdPrestador = esPrestador ? id : null
            });
            TempData["Exito"] = "Se guardó en tus favoritos.";
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Doble clic: el índice único ya tenía el favorito
        }

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Index));
    }

    // POST: /Perfil/GuardarPlan  { "idSesion": "..." }  (botón de la tarjeta del plan en el chat)
    // Se guarda el último plan que el servidor calculó en esa sesión, no lo que envíe el navegador.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarPlan([FromBody] SolicitudGuardarPlan? solicitud)
    {
        if (SesionVisitante.IdUsuario(User) is not int idUsuario)
            return Unauthorized(new { error = "Inicia sesión para guardar tus planes." });

        if (!Guid.TryParse(solicitud?.IdSesion, out var idSesion) || _chatbot.UltimoPresupuesto(idSesion) is not { } plan)
            return BadRequest(new { error = "Ya no tengo ese plan. Pídemelo otra vez y lo guardas." });

        _context.PlanesGuardados.Add(new PlanGuardado
        {
            IdUsuario = idUsuario,
            Origen = plan.Origen,
            Monto = plan.Monto,
            Total = plan.Total,
            Noches = plan.Noches,
            Cabe = plan.Cabe,
            Detalle = JsonSerializer.Serialize(plan.Lineas, OpcionesJson)
        });
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Plan guardado en tu perfil.", url = Url.Action(nameof(Index), new { seccion = "planes" }) });
    }

    // POST: /Perfil/EliminarPlan/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarPlan(int id)
    {
        if (SesionVisitante.IdUsuario(User) is not int idUsuario) return SinVisitante();

        await _context.PlanesGuardados.Where(p => p.IdPlan == id && p.IdUsuario == idUsuario).ExecuteDeleteAsync();
        return Volver("planes", "Plan eliminado.");
    }

    // POST: /Perfil/BorrarConversacion
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BorrarConversacion(Guid idSesion)
    {
        if (SesionVisitante.IdUsuario(User) is not int idUsuario) return SinVisitante();

        await _context.ConsultasAsistente.Where(c => c.IdUsuario == idUsuario && c.IdSesion == idSesion).ExecuteDeleteAsync();
        return Volver("conversaciones", "Conversación borrada.");
    }

    // POST: /Perfil/BorrarHistorial  (limpieza total de las conversaciones del visitante)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BorrarHistorial()
    {
        if (SesionVisitante.IdUsuario(User) is not int idUsuario) return SinVisitante();

        var borrados = await _context.ConsultasAsistente.Where(c => c.IdUsuario == idUsuario).ExecuteDeleteAsync();
        return Volver("conversaciones", borrados == 0
            ? "No tenías conversaciones guardadas."
            : $"Se borraron todas tus conversaciones ({borrados} {(borrados == 1 ? "mensaje" : "mensajes")}).");
    }

    // POST: /Perfil/EliminarCuenta  (borra la cuenta y todos sus datos: favoritos, planes y conversaciones)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("acceso")]
    public async Task<IActionResult> EliminarCuenta(string? contrasena)
    {
        if (SesionVisitante.IdUsuario(User) is not int idUsuario) return SinVisitante();

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);
        if (usuario == null) return SinVisitante();

        if (_proteccion.EstaBloqueado(usuario.Correo, out var minutos))
        {
            TempData["Error"] = $"Demasiados intentos. Espera {minutos} {(minutos == 1 ? "minuto" : "minutos")} e inténtalo de nuevo.";
            return RedirectToAction(nameof(Index), new { seccion = "cuenta" });
        }

        if (string.IsNullOrEmpty(contrasena)
            || _hasher.VerifyHashedPassword(usuario, usuario.ContrasenaHash, contrasena) == PasswordVerificationResult.Failed)
        {
            _proteccion.RegistrarFallo(usuario.Correo);
            TempData["Error"] = "La contraseña no es correcta. Tu cuenta no se eliminó.";
            return RedirectToAction(nameof(Index), new { seccion = "cuenta" });
        }

        // Las conversaciones se borran (no se dejan anónimas) porque el visitante pidió eliminar sus datos;
        // los favoritos y los planes se borran en cascada con el usuario
        await using (var transaccion = await _context.Database.BeginTransactionAsync())
        {
            await _context.ConsultasAsistente.Where(c => c.IdUsuario == idUsuario).ExecuteDeleteAsync();
            _context.Usuarios.Remove(usuario);
            await _context.SaveChangesAsync();
            await transaccion.CommitAsync();
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Exito"] = "Tu cuenta y todos tus datos se eliminaron.";
        return RedirectToAction("Index", "Sitio");
    }
}

public class SolicitudGuardarPlan
{
    public string? IdSesion { get; set; }
}
