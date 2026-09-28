using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

// Imágenes de lugares y de prestadores de servicios. La foto se puede subir desde la PC
// o indicar con una dirección web. Las acciones Subir, Principal y Quitar manejan el álbum.
[Authorize]
public class ImagenesController : Controller
{
    private readonly TurismoJimaniContext _context;
    private readonly AlmacenImagenes _almacen;

    public ImagenesController(TurismoJimaniContext context, AlmacenImagenes almacen)
    {
        _context = context;
        _almacen = almacen;
    }

    // GET: Imagenes
    public async Task<IActionResult> Index(string? buscar)
    {
        var consulta = _context.Imagenes.AsNoTracking()
            .Include(x => x.Lugar)
            .Include(x => x.Prestador)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            buscar = buscar.Trim();
            consulta = consulta.Where(x =>
                (x.Lugar != null && x.Lugar.Nombre.Contains(buscar))
                || (x.Prestador != null && x.Prestador.Nombre.Contains(buscar)));
        }

        ViewBag.Buscar = buscar;
        return View(await consulta
            .OrderBy(x => x.Lugar != null ? x.Lugar.Nombre : x.Prestador!.Nombre)
            .ThenBy(x => x.OrdenVisualizacion)
            .ToListAsync());
    }

    // GET: Imagenes/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Imagenes.AsNoTracking()
            .Include(x => x.Lugar)
            .Include(x => x.Prestador)
            .FirstOrDefaultAsync(x => x.IdImagen == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // GET: Imagenes/Create?idLugar=3  o  ?idPrestador=5
    public async Task<IActionResult> Create(int? idLugar, int? idPrestador)
    {
        var entidad = new Imagen { IdLugar = idLugar, IdPrestador = idLugar.HasValue ? null : idPrestador };
        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Imagenes/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(AlmacenImagenes.TamanoMaximoSolicitud)]
    [RequestFormLimits(MultipartBodyLengthLimit = AlmacenImagenes.TamanoMaximoSolicitud)]
    public async Task<IActionResult> Create(
        [Bind("UrlImagen,IdLugar,IdPrestador,TextoAlternativo,Descripcion,OrdenVisualizacion,EsPrincipal,Autor,FuenteUrl,Licencia")] Imagen entidad,
        IFormFile? archivo)
    {
        // Con archivo subido la URL la genera el servidor
        if (archivo != null) ModelState.Remove(nameof(entidad.UrlImagen));
        Validar(entidad, archivo);

        if (ModelState.IsValid)
        {
            if (archivo != null)
                entidad.UrlImagen = await _almacen.GuardarAsync(archivo, Carpeta(entidad));

            entidad.FechaCreacion = DateTime.Now;
            if (entidad.EsPrincipal) await QuitarPrincipalAsync(entidad);
            _context.Add(entidad);

            if (await GuardarAsync())
            {
                TempData["Exito"] = "Imagen agregada correctamente.";
                return RedirectToAction(nameof(Index));
            }

            if (archivo != null) _almacen.Eliminar(entidad.UrlImagen);
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
    [RequestSizeLimit(AlmacenImagenes.TamanoMaximoSolicitud)]
    [RequestFormLimits(MultipartBodyLengthLimit = AlmacenImagenes.TamanoMaximoSolicitud)]
    public async Task<IActionResult> Edit(int id, IFormFile? archivo)
    {
        var entidad = await _context.Imagenes.FindAsync(id);
        if (entidad == null) return NotFound();

        var urlAnterior = entidad.UrlImagen;

        // Solo se actualizan los campos del formulario
        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.UrlImagen, x => x.IdLugar, x => x.IdPrestador, x => x.TextoAlternativo, x => x.Descripcion,
            x => x.OrdenVisualizacion, x => x.EsPrincipal, x => x.Autor, x => x.FuenteUrl, x => x.Licencia);

        if (archivo != null)
        {
            ModelState.Remove(nameof(entidad.UrlImagen));
            entidad.UrlImagen = urlAnterior;
        }
        Validar(entidad, archivo);

        if (actualizado && ModelState.IsValid)
        {
            if (archivo != null)
                entidad.UrlImagen = await _almacen.GuardarAsync(archivo, Carpeta(entidad));

            if (entidad.EsPrincipal) await QuitarPrincipalAsync(entidad);

            if (await GuardarAsync())
            {
                if (entidad.UrlImagen != urlAnterior) _almacen.Eliminar(urlAnterior);
                TempData["Exito"] = "Imagen actualizada correctamente.";
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

        var entidad = await _context.Imagenes.AsNoTracking()
            .Include(x => x.Lugar)
            .Include(x => x.Prestador)
            .FirstOrDefaultAsync(x => x.IdImagen == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: Imagenes/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        if (await EliminarAsync(id))
            TempData["Exito"] = "Imagen eliminada correctamente.";

        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ álbum (desde la ficha del lugar o prestador)

    // POST: Imagenes/Subir  (varias fotos a la vez)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(AlmacenImagenes.TamanoMaximoSolicitud)]
    [RequestFormLimits(MultipartBodyLengthLimit = AlmacenImagenes.TamanoMaximoSolicitud)]
    public async Task<IActionResult> Subir(int? idLugar, int? idPrestador, List<IFormFile>? fotos, string? volver)
    {
        string? nombre = null;
        if (idLugar.HasValue)
            nombre = await _context.Lugares.Where(l => l.IdLugar == idLugar).Select(l => l.Nombre).FirstOrDefaultAsync();
        else if (idPrestador.HasValue)
            nombre = await _context.Prestadores.Where(p => p.IdPrestador == idPrestador).Select(p => p.Nombre).FirstOrDefaultAsync();

        if (nombre == null) return NotFound();

        if (fotos == null || fotos.Count == 0)
        {
            TempData["Error"] = "Seleccione una o varias fotos para subir.";
            return Volver(volver);
        }

        var actuales = await _context.Imagenes
            .Where(i => idLugar.HasValue ? i.IdLugar == idLugar : i.IdPrestador == idPrestador)
            .Select(i => new { i.EsPrincipal, i.OrdenVisualizacion })
            .ToListAsync();

        var (imagenes, errores) = await _almacen.CrearImagenesAsync(fotos,
            idLugar.HasValue ? "lugares" : "prestadores", nombre,
            actuales.Any(i => i.EsPrincipal),
            actuales.Select(i => i.OrdenVisualizacion).DefaultIfEmpty(-1).Max() + 1);

        foreach (var imagen in imagenes)
        {
            imagen.IdLugar = idLugar;
            imagen.IdPrestador = idLugar.HasValue ? null : idPrestador;
            _context.Imagenes.Add(imagen);
        }

        await _context.SaveChangesAsync();

        if (imagenes.Count > 0) TempData["Exito"] = $"Se agregaron {imagenes.Count} foto(s) al álbum.";
        if (errores.Count > 0) TempData["Error"] = string.Join(" ", errores);
        return Volver(volver);
    }

    // POST: Imagenes/Principal/5  (marca la foto como portada)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Principal(int id, string? volver)
    {
        var imagen = await _context.Imagenes.FindAsync(id);
        if (imagen == null) return NotFound();

        await QuitarPrincipalAsync(imagen);
        imagen.EsPrincipal = true;
        await _context.SaveChangesAsync();

        TempData["Exito"] = "Foto de portada actualizada.";
        return Volver(volver);
    }

    // POST: Imagenes/Quitar/5  (elimina la foto del álbum)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quitar(int id, string? volver)
    {
        if (await EliminarAsync(id))
            TempData["Exito"] = "Foto eliminada del álbum.";

        return Volver(volver);
    }

    // ------------------------------------------------------------------ utilidades

    private async Task<bool> EliminarAsync(int id)
    {
        var entidad = await _context.Imagenes.FindAsync(id);
        if (entidad == null) return false;

        var eraPrincipal = entidad.EsPrincipal;
        _context.Imagenes.Remove(entidad);
        await _context.SaveChangesAsync();
        _almacen.Eliminar(entidad.UrlImagen);

        // Si se borró la portada, la siguiente foto del álbum pasa a ser la principal
        if (eraPrincipal)
        {
            var siguiente = await _context.Imagenes
                .Where(i => entidad.IdLugar != null ? i.IdLugar == entidad.IdLugar : i.IdPrestador == entidad.IdPrestador)
                .OrderBy(i => i.OrdenVisualizacion)
                .FirstOrDefaultAsync();

            if (siguiente != null)
            {
                siguiente.EsPrincipal = true;
                await _context.SaveChangesAsync();
            }
        }

        return true;
    }

    // Solo puede haber una foto principal por lugar o prestador
    private async Task QuitarPrincipalAsync(Imagen imagen)
    {
        var otras = await _context.Imagenes
            .Where(i => i.IdImagen != imagen.IdImagen && i.EsPrincipal
                        && (imagen.IdLugar != null ? i.IdLugar == imagen.IdLugar : i.IdPrestador == imagen.IdPrestador))
            .ToListAsync();

        foreach (var otra in otras) otra.EsPrincipal = false;
    }

    private IActionResult Volver(string? volver) =>
        !string.IsNullOrEmpty(volver) && Url.IsLocalUrl(volver)
            ? LocalRedirect(volver)
            : RedirectToAction(nameof(Index));

    private static string Carpeta(Imagen imagen) => imagen.IdLugar.HasValue ? "lugares" : "prestadores";

    private void Validar(Imagen entidad, IFormFile? archivo)
    {
        if (entidad.IdLugar.HasValue == entidad.IdPrestador.HasValue)
            ModelState.AddModelError(nameof(entidad.IdLugar), "Seleccione un lugar o un prestador de servicios (solo uno).");

        if (archivo != null)
        {
            if (AlmacenImagenes.Validar(archivo) is { } error)
                ModelState.AddModelError("archivo", error);
        }
        else if (string.IsNullOrWhiteSpace(entidad.UrlImagen))
        {
            ModelState.AddModelError(nameof(entidad.UrlImagen), "Suba una foto desde su PC o escriba la dirección de la imagen.");
        }
        else if (SitioPublico.UrlSegura(entidad.UrlImagen) == null)
        {
            // El sitio público solo muestra direcciones http(s) o fotos subidas al servidor
            ModelState.AddModelError(nameof(entidad.UrlImagen), "Escriba una dirección que empiece con http:// o https://.");
        }

        if (!string.IsNullOrWhiteSpace(entidad.FuenteUrl) && SitioPublico.UrlSegura(entidad.FuenteUrl) == null)
            ModelState.AddModelError(nameof(entidad.FuenteUrl), "Escriba una dirección que empiece con http:// o https://.");
    }

    private async Task CargarListasAsync(Imagen? entidad = null)
    {
        ViewBag.ListaIdLugar = new SelectList(
            await _context.Lugares.AsNoTracking().OrderBy(l => l.Nombre).ToListAsync(),
            "IdLugar", "Nombre", entidad?.IdLugar);

        ViewBag.ListaIdPrestador = new SelectList(
            await _context.Prestadores.AsNoTracking().OrderBy(p => p.Nombre).ToListAsync(),
            "IdPrestador", "Nombre", entidad?.IdPrestador);
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
