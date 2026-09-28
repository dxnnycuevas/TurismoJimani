using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

[Authorize]
public class LugaresController : Controller
{
    private readonly TurismoJimaniContext _context;
    private readonly AlmacenImagenes _almacen;

    public LugaresController(TurismoJimaniContext context, AlmacenImagenes almacen)
    {
        _context = context;
        _almacen = almacen;
    }

    // GET: Lugares
    public async Task<IActionResult> Index(string? buscar)
    {
        var consulta = _context.Lugares.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            buscar = buscar.Trim();
            consulta = consulta.Where(x =>
                x.Nombre.Contains(buscar)
                || (x.Municipio != null && x.Municipio.Contains(buscar))
                || (x.Provincia != null && x.Provincia.Contains(buscar)));
        }

        ViewBag.Buscar = buscar;
        return View(await consulta.OrderBy(x => x.Nombre).ToListAsync());
    }

    // GET: Lugares/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Lugares
            .Include(x => x.Categorias)
            .Include(x => x.Servicios)
            .Include(x => x.Imagenes)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdLugar == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // GET: Lugares/Create
    public async Task<IActionResult> Create()
    {
        var entidad = new Lugar();
        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Lugares/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Nombre,Municipio,Provincia,DescripcionCorta,Descripcion,Direccion,Latitud,Longitud,Telefono,Correo,SitioWeb,Activo")] Lugar entidad,
        int[] categoriasSeleccionadas,
        int[] serviciosSeleccionados)
    {
        if (ModelState.IsValid)
        {
            entidad.FechaCreacion = DateTime.Now;
            await AsignarRelacionesAsync(entidad, categoriasSeleccionadas, serviciosSeleccionados);
            _context.Add(entidad);

            if (await GuardarAsync())
            {
                TempData["Exito"] = "Registro creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        await CargarListasAsync(entidad, categoriasSeleccionadas, serviciosSeleccionados);
        return View(entidad);
    }

    // GET: Lugares/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Lugares
            .Include(x => x.Categorias)
            .Include(x => x.Servicios)
            .FirstOrDefaultAsync(x => x.IdLugar == id);

        if (entidad == null) return NotFound();

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Lugares/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, int[] categoriasSeleccionadas, int[] serviciosSeleccionados)
    {
        var entidad = await _context.Lugares
            .Include(x => x.Categorias)
            .Include(x => x.Servicios)
            .FirstOrDefaultAsync(x => x.IdLugar == id);

        if (entidad == null) return NotFound();

        // Solo se actualizan los campos del formulario
        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.Nombre, x => x.Municipio, x => x.Provincia, x => x.DescripcionCorta,
            x => x.Descripcion, x => x.Direccion, x => x.Latitud, x => x.Longitud,
            x => x.Telefono, x => x.Correo, x => x.SitioWeb, x => x.Activo);

        if (actualizado && ModelState.IsValid)
        {
            entidad.FechaActualizacion = DateTime.Now;
            await AsignarRelacionesAsync(entidad, categoriasSeleccionadas, serviciosSeleccionados);

            if (await GuardarAsync())
            {
                TempData["Exito"] = "Registro actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        await CargarListasAsync(entidad, categoriasSeleccionadas, serviciosSeleccionados);
        return View(entidad);
    }

    // GET: Lugares/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Lugares.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdLugar == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: Lugares/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entidad = await _context.Lugares.Include(l => l.Imagenes).FirstOrDefaultAsync(l => l.IdLugar == id);
        if (entidad == null) return RedirectToAction(nameof(Index));

        if (await _context.Rutas.AnyAsync(r => r.IdLugarOrigen == id || r.IdLugarDestino == id))
        {
            TempData["Error"] = "No se puede eliminar el lugar porque está asignado a una o más rutas.";
            return RedirectToAction(nameof(Index));
        }

        var archivos = entidad.Imagenes.Select(i => i.UrlImagen).ToList();
        _context.Lugares.Remove(entidad);

        try
        {
            await _context.SaveChangesAsync();
            foreach (var archivo in archivos) _almacen.Eliminar(archivo);
            TempData["Exito"] = "Registro eliminado correctamente.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "No se puede eliminar el registro porque está relacionado con otros datos.";
        }

        return RedirectToAction(nameof(Index));
    }

    // Sincroniza las tablas LugarCategoria y LugarServicio con lo marcado en el formulario
    private async Task AsignarRelacionesAsync(Lugar entidad, int[] categorias, int[] servicios)
    {
        entidad.Categorias.Clear();
        foreach (var categoria in await _context.Categorias.Where(c => categorias.Contains(c.IdCategoria)).ToListAsync())
        {
            entidad.Categorias.Add(categoria);
        }

        entidad.Servicios.Clear();
        foreach (var servicio in await _context.Servicios.Where(s => servicios.Contains(s.IdServicio)).ToListAsync())
        {
            entidad.Servicios.Add(servicio);
        }
    }

    private async Task CargarListasAsync(Lugar entidad, int[]? categorias = null, int[]? servicios = null)
    {
        var seleccionCategorias = (categorias ?? entidad.Categorias.Select(c => c.IdCategoria)).ToHashSet();
        var seleccionServicios = (servicios ?? entidad.Servicios.Select(s => s.IdServicio)).ToHashSet();

        // Se muestran las activas y las que ya estaban asignadas al lugar
        ViewBag.Categorias = await _context.Categorias.AsNoTracking()
            .Where(c => c.Activo || seleccionCategorias.Contains(c.IdCategoria))
            .OrderBy(c => c.Nombre).ToListAsync();

        ViewBag.Servicios = await _context.Servicios.AsNoTracking()
            .Where(s => s.Activo || seleccionServicios.Contains(s.IdServicio))
            .OrderBy(s => s.Nombre).ToListAsync();

        ViewBag.CategoriasSeleccionadas = seleccionCategorias;
        ViewBag.ServiciosSeleccionados = seleccionServicios;
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
