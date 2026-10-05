using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

// Precios de pasaje hasta Jimaní que usa el planificador de presupuesto del asistente
[Authorize]
public class TarifasViajeController : Controller
{
    private readonly TurismoJimaniContext _context;

    public TarifasViajeController(TurismoJimaniContext context)
    {
        _context = context;
    }

    // GET: TarifasViaje
    public async Task<IActionResult> Index()
    {
        return View(await _context.TarifasViaje.AsNoTracking()
            .OrderBy(t => t.Origen).ThenBy(t => t.PrecioIda)
            .ToListAsync());
    }

    // GET: TarifasViaje/Create
    public IActionResult Create()
    {
        return View(new TarifaViaje());
    }

    // POST: TarifasViaje/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Origen,Medio,PrecioIda,HorasViaje,Nota,Activo")] TarifaViaje entidad)
    {
        Normalizar(entidad);

        if (ModelState.IsValid)
        {
            _context.Add(entidad);
            await _context.SaveChangesAsync();
            TempData["Exito"] = "Tarifa creada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        return View(entidad);
    }

    // GET: TarifasViaje/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.TarifasViaje.FindAsync(id);
        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: TarifasViaje/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id)
    {
        var entidad = await _context.TarifasViaje.FindAsync(id);
        if (entidad == null) return NotFound();

        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.Origen, x => x.Medio, x => x.PrecioIda, x => x.HorasViaje, x => x.Nota, x => x.Activo);
        Normalizar(entidad);

        if (actualizado && ModelState.IsValid)
        {
            await _context.SaveChangesAsync();
            TempData["Exito"] = "Tarifa actualizada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        return View(entidad);
    }

    // GET: TarifasViaje/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.TarifasViaje.AsNoTracking().FirstOrDefaultAsync(t => t.IdTarifa == id);
        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: TarifasViaje/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entidad = await _context.TarifasViaje.FindAsync(id);
        if (entidad != null)
        {
            _context.TarifasViaje.Remove(entidad);
            await _context.SaveChangesAsync();
            TempData["Exito"] = "Tarifa eliminada correctamente.";
        }

        return RedirectToAction(nameof(Index));
    }

    // El asistente compara el origen por nombre: se guardan sin espacios sobrantes
    private static void Normalizar(TarifaViaje entidad)
    {
        entidad.Origen = entidad.Origen?.Trim() ?? "";
        entidad.Medio = entidad.Medio?.Trim() ?? "";
    }
}
