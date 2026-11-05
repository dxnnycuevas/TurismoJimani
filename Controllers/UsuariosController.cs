using System.Security.Claims;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Models;
using AppDonnyCuevas20210074.Services.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

[Authorize(Roles = "Administrador")]
public class UsuariosController : Controller
{
    private readonly TurismoJimaniContext _context;
    private readonly ValidadorContrasenas _validador;
    private readonly PasswordHasher<Usuario> _hasher = new();

    public UsuariosController(TurismoJimaniContext context, ValidadorContrasenas validador)
    {
        _context = context;
        _validador = validador;
    }

    // GET: Usuarios
    public async Task<IActionResult> Index(string? buscar)
    {
        var consulta = _context.Usuarios.Include(x => x.Rol).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            buscar = buscar.Trim();
            consulta = consulta.Where(x =>
                x.NombreCompleto.Contains(buscar) || x.Correo.Contains(buscar));
        }

        ViewBag.Buscar = buscar;
        return View(await consulta.OrderBy(x => x.NombreCompleto).ToListAsync());
    }

    // GET: Usuarios/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Usuarios.Include(x => x.Rol).AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // GET: Usuarios/Create
    public async Task<IActionResult> Create()
    {
        var entidad = new Usuario();
        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Usuarios/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("NombreCompleto,Correo,IdRol,Activo")] Usuario entidad,
        string? contrasena,
        string? confirmarContrasena)
    {
        // El hash se calcula aquí, no viene del formulario
        ModelState.Remove(nameof(Usuario.ContrasenaHash));

        entidad.Correo = entidad.Correo?.Trim() ?? string.Empty;
        await ValidarCorreoAsync(entidad);
        await ValidarContrasenaAsync(contrasena, confirmarContrasena, entidad.Correo, obligatoria: true);

        if (ModelState.IsValid)
        {
            entidad.ContrasenaHash = _hasher.HashPassword(entidad, contrasena!);
            entidad.FechaCreacion = DateTime.Now;
            _context.Add(entidad);

            if (await GuardarAsync())
            {
                TempData["Exito"] = "Usuario creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // GET: Usuarios/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Usuarios.FindAsync(id);
        if (entidad == null) return NotFound();

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Usuarios/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string? contrasena, string? confirmarContrasena)
    {
        var entidad = await _context.Usuarios.FindAsync(id);
        if (entidad == null) return NotFound();

        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.NombreCompleto, x => x.Correo, x => x.IdRol, x => x.Activo);

        entidad.Correo = entidad.Correo?.Trim() ?? string.Empty;
        await ValidarCorreoAsync(entidad);
        await ValidarContrasenaAsync(contrasena, confirmarContrasena, entidad.Correo, obligatoria: false);

        // El usuario conectado no puede desactivarse ni quitarse el rol de administrador
        if (EsUsuarioActual(id))
        {
            if (!entidad.Activo)
                ModelState.AddModelError(nameof(entidad.Activo), "No puede desactivar su propio usuario.");

            var rol = await _context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.IdRol == entidad.IdRol);
            if (rol?.Nombre != "Administrador")
                ModelState.AddModelError(nameof(entidad.IdRol), "No puede quitarse el rol de Administrador.");
        }

        if (actualizado && ModelState.IsValid)
        {
            if (!string.IsNullOrEmpty(contrasena))
            {
                entidad.ContrasenaHash = _hasher.HashPassword(entidad, contrasena);
            }

            entidad.FechaActualizacion = DateTime.Now;

            if (await GuardarAsync())
            {
                TempData["Exito"] = "Usuario actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // GET: Usuarios/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Usuarios.Include(x => x.Rol).AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: Usuarios/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        if (EsUsuarioActual(id))
        {
            TempData["Error"] = "No puede eliminar su propio usuario.";
            return RedirectToAction(nameof(Index));
        }

        var entidad = await _context.Usuarios.FindAsync(id);
        if (entidad == null) return RedirectToAction(nameof(Index));

        _context.Usuarios.Remove(entidad);

        try
        {
            await _context.SaveChangesAsync();
            TempData["Exito"] = "Usuario eliminado correctamente.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "No se puede eliminar el registro porque está relacionado con otros datos.";
        }

        return RedirectToAction(nameof(Index));
    }

    private bool EsUsuarioActual(int id) =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString();

    private async Task ValidarCorreoAsync(Usuario entidad)
    {
        if (!string.IsNullOrEmpty(entidad.Correo) &&
            await _context.Usuarios.AnyAsync(u => u.Correo == entidad.Correo && u.IdUsuario != entidad.IdUsuario))
        {
            ModelState.AddModelError(nameof(entidad.Correo), "Ya existe un usuario con ese correo.");
        }
    }

    // Política de contraseñas (S-01): 15 caracteres como mínimo, sin contraseñas comunes ni filtradas
    private async Task ValidarContrasenaAsync(string? contrasena, string? confirmacion, string? correo, bool obligatoria)
    {
        if (string.IsNullOrEmpty(contrasena))
        {
            if (obligatoria)
                ModelState.AddModelError("Contrasena", "La contraseña es obligatoria.");
            return;
        }

        foreach (var error in await _validador.ValidarAsync(contrasena, correo, HttpContext.RequestAborted))
            ModelState.AddModelError("Contrasena", error);

        if (contrasena != confirmacion)
            ModelState.AddModelError("ConfirmarContrasena", "Las contraseñas no coinciden.");
    }

    private async Task CargarListasAsync(Usuario? entidad = null)
    {
        ViewBag.ListaIdRol = new SelectList(
            await _context.Roles.AsNoTracking().OrderBy(r => r.Nombre).ToListAsync(),
            "IdRol", "Nombre", entidad?.IdRol);
    }

    private async Task<bool> GuardarAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty,
                "No se pudo guardar. Verifique que no exista otro registro con los mismos datos.");
            return false;
        }
    }
}
