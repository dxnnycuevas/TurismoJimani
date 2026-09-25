using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

[Authorize]
public class ImagenesController : Controller
{
    private readonly TurismoJimaniContext _context;

    public ImagenesController(TurismoJimaniContext context)
    {
        _context = context;
    }

    // GET: Imagenes
    public async Task<IActionResult> Index(string? buscar)
    {
        var consulta = _context.Imagenes.Include(x => x.Lugar).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            buscar = buscar.Trim();
            consulta = consulta.Where(x =>
                x.Lugar!.Nombre.Contains(buscar));
        }

        ViewBag.Buscar = buscar;
        return View(await consulta.OrderBy(x => x.Lugar!.Nombre).ThenBy(x => x.OrdenVisualizacion).ToListAsync());
    }

    // GET: Imagenes/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Imagenes.Include(x => x.Lugar).AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdImagen == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // GET: Imagenes/Create
    public async Task<IActionResult> Create()
    {
        var entidad = new Imagen();
        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Imagenes/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("UrlImagen,IdLugar,TextoAlternativo,Descripcion,OrdenVisualizacion,EsPrincipal,Autor,FuenteUrl,Licencia")] Imagen entidad)
    {
        Validar(entidad);

        if (ModelState.IsValid)
        {
            entidad.FechaCreacion = DateTime.Now;
            _context.Add(entidad);

            if (await GuardarAsync())
            {
                TempData["Exito"] = "Registro creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // GET: Imagenes/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Imagenes.FindAsync(id);
        if (entidad == null) return NotFound();

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Imagenes/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id)
    {
        var entidad = await _context.Imagenes.FindAsync(id);
        if (entidad == null) return NotFound();

        // Solo se actualizan los campos del formulario
        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.UrlImagen, x => x.IdLugar, x => x.TextoAlternativo, x => x.Descripcion, x => x.OrdenVisualizacion, x => x.EsPrincipal,
            x => x.Autor, x => x.FuenteUrl, x => x.Licencia);

        Validar(entidad);

        if (actualizado && ModelState.IsValid)
        {
            if (await GuardarAsync())
            {
                TempData["Exito"] = "Registro actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // GET: Imagenes/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Imagenes.Include(x => x.Lugar).AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdImagen == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: Imagenes/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entidad = await _context.Imagenes.FindAsync(id);
        if (entidad == null) return RedirectToAction(nameof(Index));

        _context.Imagenes.Remove(entidad);

        try
        {
            await _context.SaveChangesAsync();
            TempData["Exito"] = "Registro eliminado correctamente.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "No se puede eliminar el registro porque está relacionado con otros datos.";
        }

        return RedirectToAction(nameof(Index));
    }

    private void Validar(Imagen entidad)
    {
        // El sitio público solo muestra direcciones http(s)
        if (!string.IsNullOrWhiteSpace(entidad.UrlImagen) && SitioPublico.UrlSegura(entidad.UrlImagen) == null)
            ModelState.AddModelError(nameof(entidad.UrlImagen), "Escriba una dirección que empiece con http:// o https://.");

        if (!string.IsNullOrWhiteSpace(entidad.FuenteUrl) && SitioPublico.UrlSegura(entidad.FuenteUrl) == null)
            ModelState.AddModelError(nameof(entidad.FuenteUrl), "Escriba una dirección que empiece con http:// o https://.");
    }

    private async Task CargarListasAsync(Imagen? entidad = null)
    {
        ViewBag.ListaIdLugar = new SelectList(
            await _context.Lugares.AsNoTracking().OrderBy(l => l.Nombre).ToListAsync(),
            "IdLugar", "Nombre", entidad?.IdLugar);
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
