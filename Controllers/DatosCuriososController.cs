using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

// Datos cortos que el sitio público muestra al azar al tocar las caritas taínas
[Authorize]
public class DatosCuriososController : Controller
{
    private readonly TurismoJimaniContext _context;

    public DatosCuriososController(TurismoJimaniContext context)
    {
        _context = context;
    }

    // GET: DatosCuriosos
    public async Task<IActionResult> Index(string? buscar)
    {
        var consulta = _context.DatosCuriosos.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            buscar = buscar.Trim();
            consulta = consulta.Where(x => x.Texto.Contains(buscar) || x.Categoria.Contains(buscar));
        }

        ViewBag.Buscar = buscar;
        return View(await consulta.OrderBy(x => x.Categoria).ThenBy(x => x.IdDato).ToListAsync());
    }

    // GET: DatosCuriosos/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.DatosCuriosos.AsNoTracking().FirstOrDefaultAsync(x => x.IdDato == id);
        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // GET: DatosCuriosos/Create
    public IActionResult Create()
    {
        var entidad = new DatoCurioso();
        CargarListas(entidad);
        return View(entidad);
    }

    // POST: DatosCuriosos/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Texto,Categoria,Fuente,Activo")] DatoCurioso entidad)
    {
        Validar(entidad);

        if (ModelState.IsValid)
        {
            _context.Add(entidad);

            if (await GuardarAsync())
            {
                TempData["Exito"] = "Registro creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        CargarListas(entidad);
        return View(entidad);
    }

    // GET: DatosCuriosos/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.DatosCuriosos.FindAsync(id);
        if (entidad == null) return NotFound();

        CargarListas(entidad);
        return View(entidad);
    }

    // POST: DatosCuriosos/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id)
    {
        var entidad = await _context.DatosCuriosos.FindAsync(id);
        if (entidad == null) return NotFound();

        // Solo se actualizan los campos del formulario
        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.Texto, x => x.Categoria, x => x.Fuente, x => x.Activo);

        Validar(entidad);

        if (actualizado && ModelState.IsValid)
        {
            if (await GuardarAsync())
            {
                TempData["Exito"] = "Registro actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        CargarListas(entidad);
        return View(entidad);
    }

    // GET: DatosCuriosos/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.DatosCuriosos.AsNoTracking().FirstOrDefaultAsync(x => x.IdDato == id);
        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: DatosCuriosos/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entidad = await _context.DatosCuriosos.FindAsync(id);
        if (entidad == null) return RedirectToAction(nameof(Index));

        _context.DatosCuriosos.Remove(entidad);
        await _context.SaveChangesAsync();
        TempData["Exito"] = "Registro eliminado correctamente.";

        return RedirectToAction(nameof(Index));
    }

    private void Validar(DatoCurioso entidad)
    {
        if (!DatoCurioso.Categorias.Contains(entidad.Categoria))
            ModelState.AddModelError(nameof(entidad.Categoria), "Seleccione una categoría de la lista.");
    }

    private void CargarListas(DatoCurioso? entidad = null)
    {
        ViewBag.ListaCategoria = new SelectList(DatoCurioso.Categorias, entidad?.Categoria);
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
            ModelState.AddModelError(string.Empty, "No se pudo guardar el registro.");
            return false;
        }
    }
}
