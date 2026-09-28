using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

// Catálogo de tipos de servicio (chofer, guía, barbero, hotel, restaurante, pyme...)
[Authorize]
public class TiposPrestadorController : Controller
{
    private readonly TurismoJimaniContext _context;

    public TiposPrestadorController(TurismoJimaniContext context)
    {
        _context = context;
    }

    // GET: TiposPrestador
    public async Task<IActionResult> Index(string? buscar, string? clase)
    {
        var consulta = _context.TiposPrestador.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            buscar = buscar.Trim();
            consulta = consulta.Where(x => x.Nombre.Contains(buscar));
        }

        if (ClasesPrestador.Todas.Contains(clase))
            consulta = consulta.Where(x => x.Clase == clase);
        else
            clase = null;

        ViewBag.Buscar = buscar;
        ViewBag.Clase = clase;
        ViewBag.Totales = await _context.Prestadores.AsNoTracking()
            .GroupBy(p => p.IdTipoPrestador)
            .Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Total);

        return View(await consulta.OrderBy(x => x.Clase).ThenBy(x => x.Nombre).ToListAsync());
    }

    // GET: TiposPrestador/Create
    public IActionResult Create(string? clase)
    {
        return View(new TipoPrestador
        {
            Clase = ClasesPrestador.Todas.Contains(clase) ? clase! : ClasesPrestador.Persona
        });
    }

    // POST: TiposPrestador/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Nombre,Clase,Grupo,Descripcion,Icono,Activo")] TipoPrestador entidad)
    {
        await ValidarAsync(entidad);

        if (ModelState.IsValid)
        {
            _context.Add(entidad);
            if (await GuardarAsync())
            {
                TempData["Exito"] = "Tipo de servicio creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        return View(entidad);
    }

    // GET: TiposPrestador/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.TiposPrestador.FindAsync(id);
        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: TiposPrestador/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id)
    {
        var entidad = await _context.TiposPrestador.FindAsync(id);
        if (entidad == null) return NotFound();

        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.Nombre, x => x.Clase, x => x.Grupo, x => x.Descripcion, x => x.Icono, x => x.Activo);

        await ValidarAsync(entidad);

        if (actualizado && ModelState.IsValid && await GuardarAsync())
        {
            TempData["Exito"] = "Tipo de servicio actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        return View(entidad);
    }

    // GET: TiposPrestador/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.TiposPrestador.AsNoTracking().FirstOrDefaultAsync(x => x.IdTipoPrestador == id);
        if (entidad == null) return NotFound();

        ViewBag.TotalPrestadores = await _context.Prestadores.CountAsync(p => p.IdTipoPrestador == id);
        return View(entidad);
    }

    // POST: TiposPrestador/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entidad = await _context.TiposPrestador.FindAsync(id);
        if (entidad == null) return RedirectToAction(nameof(Index));

        if (await _context.Prestadores.AnyAsync(p => p.IdTipoPrestador == id))
        {
            TempData["Error"] = "No se puede eliminar el tipo porque tiene prestadores registrados. Puede desactivarlo.";
            return RedirectToAction(nameof(Index));
        }

        _context.TiposPrestador.Remove(entidad);
        await _context.SaveChangesAsync();
        TempData["Exito"] = "Tipo de servicio eliminado correctamente.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidarAsync(TipoPrestador entidad)
    {
        if (!ClasesPrestador.Todas.Contains(entidad.Clase))
            ModelState.AddModelError(nameof(entidad.Clase), "Seleccione si es una persona o un establecimiento.");

        if (!GruposPrestador.Todos.Any(g => g.Clave == entidad.Grupo))
            ModelState.AddModelError(nameof(entidad.Grupo), "Seleccione el grupo.");

        if (!string.IsNullOrWhiteSpace(entidad.Icono))
        {
            entidad.Icono = entidad.Icono.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(entidad.Icono, "^fa-[a-z0-9-]+$"))
                ModelState.AddModelError(nameof(entidad.Icono), "Escriba el nombre de un icono de Font Awesome, por ejemplo fa-car.");
        }

        var nombre = entidad.Nombre?.Trim() ?? "";
        if (await _context.TiposPrestador.AnyAsync(t => t.Nombre == nombre && t.IdTipoPrestador != entidad.IdTipoPrestador))
            ModelState.AddModelError(nameof(entidad.Nombre), "Ya existe un tipo de servicio con ese nombre.");
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
            ModelState.AddModelError(string.Empty, "No se pudo guardar. Verifique que no exista otro tipo con el mismo nombre.");
            return false;
        }
    }
}
