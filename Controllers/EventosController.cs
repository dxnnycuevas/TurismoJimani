using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

// Eventos y promociones que se publican en el sitio (fiestas patronales, festivales, ofertas...)
[Authorize]
public class EventosController : Controller
{
    private readonly TurismoJimaniContext _context;

    public EventosController(TurismoJimaniContext context)
    {
        _context = context;
    }

    // GET: Eventos
    public async Task<IActionResult> Index(string? buscar)
    {
        var consulta = _context.Eventos.Include(x => x.Lugar).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            buscar = buscar.Trim();
            consulta = consulta.Where(x =>
                x.Titulo.Contains(buscar)
                || x.Tipo.Contains(buscar)
                || (x.LugarTexto != null && x.LugarTexto.Contains(buscar))
                || (x.Lugar != null && x.Lugar.Nombre.Contains(buscar)));
        }

        ViewBag.Buscar = buscar;
        return View(await consulta.OrderByDescending(x => x.FechaInicio).ToListAsync());
    }

    // GET: Eventos/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Eventos.Include(x => x.Lugar).AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdEvento == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // GET: Eventos/Create
    public async Task<IActionResult> Create()
    {
        var entidad = new Evento { FechaInicio = DateTime.Today.AddHours(9) };
        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Eventos/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Titulo,Tipo,Resumen,Contenido,FechaInicio,FechaFin,IdLugar,LugarTexto,Organizador,EnlaceExterno,ImagenUrl,ImagenAutor,ImagenFuenteUrl,Destacado,Publicado")] Evento entidad)
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

    // GET: Eventos/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Eventos.FindAsync(id);
        if (entidad == null) return NotFound();

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Eventos/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id)
    {
        var entidad = await _context.Eventos.FindAsync(id);
        if (entidad == null) return NotFound();

        // Solo se actualizan los campos del formulario
        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.Titulo, x => x.Tipo, x => x.Resumen, x => x.Contenido, x => x.FechaInicio, x => x.FechaFin,
            x => x.IdLugar, x => x.LugarTexto, x => x.Organizador, x => x.EnlaceExterno,
            x => x.ImagenUrl, x => x.ImagenAutor, x => x.ImagenFuenteUrl, x => x.Destacado, x => x.Publicado);

        Validar(entidad);

        if (actualizado && ModelState.IsValid)
        {
            entidad.FechaActualizacion = DateTime.Now;
            if (await GuardarAsync())
            {
                TempData["Exito"] = "Registro actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // GET: Eventos/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Eventos.Include(x => x.Lugar).AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdEvento == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: Eventos/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entidad = await _context.Eventos.FindAsync(id);
        if (entidad == null) return RedirectToAction(nameof(Index));

        _context.Eventos.Remove(entidad);

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

    private void Validar(Evento entidad)
    {
        if (entidad.FechaFin.HasValue && entidad.FechaFin < entidad.FechaInicio)
            ModelState.AddModelError(nameof(entidad.FechaFin), "La fecha de fin no puede ser anterior a la de inicio.");

        if (!Evento.Tipos.Contains(entidad.Tipo))
            ModelState.AddModelError(nameof(entidad.Tipo), "Seleccione un tipo de la lista.");

        foreach (var (campo, valor) in new[]
                 {
                     (nameof(entidad.ImagenUrl), entidad.ImagenUrl),
                     (nameof(entidad.ImagenFuenteUrl), entidad.ImagenFuenteUrl),
                     (nameof(entidad.EnlaceExterno), entidad.EnlaceExterno)
                 })
        {
            if (!string.IsNullOrWhiteSpace(valor) && SitioPublico.UrlSegura(valor) == null)
                ModelState.AddModelError(campo, "Escriba una dirección que empiece con http:// o https://.");
        }
    }

    private async Task CargarListasAsync(Evento? entidad = null)
    {
        ViewBag.ListaIdLugar = new SelectList(
            await _context.Lugares.AsNoTracking().OrderBy(l => l.Nombre).ToListAsync(),
            "IdLugar", "Nombre", entidad?.IdLugar);
        ViewBag.ListaTipo = new SelectList(Evento.Tipos, entidad?.Tipo);
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
