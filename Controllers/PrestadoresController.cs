using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDonnyCuevas20210074.Controllers;

// Prestadores de servicios: personas (chofer, guía, barbero...) y establecimientos (hotel, restaurante, pyme...)
[Authorize]
public class PrestadoresController : Controller
{
    private const string CarpetaFotos = "prestadores";

    private readonly TurismoJimaniContext _context;
    private readonly AlmacenImagenes _almacen;

    public PrestadoresController(TurismoJimaniContext context, AlmacenImagenes almacen)
    {
        _context = context;
        _almacen = almacen;
    }

    // GET: Prestadores?clase=Persona&tipo=3&buscar=raul
    public async Task<IActionResult> Index(string? buscar, string? clase, int? tipo)
    {
        var consulta = _context.Prestadores.AsNoTracking()
            .Include(x => x.TipoPrestador)
            .Include(x => x.Imagenes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            buscar = buscar.Trim();
            consulta = consulta.Where(x =>
                x.Nombre.Contains(buscar)
                || x.TipoPrestador!.Nombre.Contains(buscar)
                || (x.Especialidad != null && x.Especialidad.Contains(buscar)));
        }

        if (ClasesPrestador.Todas.Contains(clase))
            consulta = consulta.Where(x => x.TipoPrestador!.Clase == clase);
        else
            clase = null;

        if (tipo.HasValue)
            consulta = consulta.Where(x => x.IdTipoPrestador == tipo);

        ViewBag.Buscar = buscar;
        ViewBag.Clase = clase;
        ViewBag.Tipo = tipo;
        ViewBag.Tipos = await _context.TiposPrestador.AsNoTracking()
            .Where(t => clase == null || t.Clase == clase)
            .OrderBy(t => t.Nombre).ToListAsync();

        return View(await consulta.OrderBy(x => x.Nombre).ToListAsync());
    }

    // GET: Prestadores/Details/5  (incluye el álbum de fotos)
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Prestadores.AsNoTracking()
            .Include(x => x.TipoPrestador)
            .Include(x => x.Imagenes)
            .Include(x => x.Servicios)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.IdPrestador == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // GET: Prestadores/Create?clase=Persona
    public async Task<IActionResult> Create(string? clase, int? tipo)
    {
        var entidad = new Prestador();
        if (tipo.HasValue) entidad.IdTipoPrestador = tipo.Value;

        await CargarListasAsync(entidad, clase: clase);
        return View(entidad);
    }

    // POST: Prestadores/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(AlmacenImagenes.TamanoMaximoSolicitud)]
    [RequestFormLimits(MultipartBodyLengthLimit = AlmacenImagenes.TamanoMaximoSolicitud)]
    public async Task<IActionResult> Create(
        [Bind(CamposFormulario)] Prestador entidad,
        int[] serviciosSeleccionados,
        List<IFormFile>? fotos)
    {
        await ValidarAsync(entidad, fotos);

        if (ModelState.IsValid)
        {
            entidad.FechaCreacion = DateTime.Now;
            await AsignarServiciosAsync(entidad, serviciosSeleccionados);

            var (imagenes, _) = await _almacen.CrearImagenesAsync(fotos, CarpetaFotos, entidad.Nombre, false, 0);
            foreach (var imagen in imagenes) entidad.Imagenes.Add(imagen);

            _context.Add(entidad);

            if (await GuardarAsync())
            {
                TempData["Exito"] = imagenes.Count > 0
                    ? $"Prestador registrado con {imagenes.Count} foto(s)."
                    : "Prestador registrado. Puede agregar fotos a su álbum cuando quiera.";
                return RedirectToAction(nameof(Details), new { id = entidad.IdPrestador });
            }

            foreach (var imagen in imagenes) _almacen.Eliminar(imagen.UrlImagen);
        }

        await CargarListasAsync(entidad, serviciosSeleccionados);
        return View(entidad);
    }

    // GET: Prestadores/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Prestadores
            .Include(x => x.Servicios)
            .Include(x => x.Imagenes)
            .FirstOrDefaultAsync(x => x.IdPrestador == id);

        if (entidad == null) return NotFound();

        await CargarListasAsync(entidad);
        return View(entidad);
    }

    // POST: Prestadores/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(AlmacenImagenes.TamanoMaximoSolicitud)]
    [RequestFormLimits(MultipartBodyLengthLimit = AlmacenImagenes.TamanoMaximoSolicitud)]
    public async Task<IActionResult> Edit(int id, int[] serviciosSeleccionados, List<IFormFile>? fotos)
    {
        var entidad = await _context.Prestadores
            .Include(x => x.Servicios)
            .Include(x => x.Imagenes)
            .FirstOrDefaultAsync(x => x.IdPrestador == id);

        if (entidad == null) return NotFound();

        // Solo se actualizan los campos del formulario
        var actualizado = await TryUpdateModelAsync(entidad, string.Empty,
            x => x.IdTipoPrestador, x => x.Nombre, x => x.DescripcionCorta, x => x.Descripcion,
            x => x.Telefono, x => x.WhatsApp, x => x.Correo, x => x.SitioWeb, x => x.RedSocial,
            x => x.Direccion, x => x.Municipio, x => x.Provincia, x => x.Latitud, x => x.Longitud,
            x => x.ZonaCobertura, x => x.HorarioAtencion, x => x.PrecioDesde, x => x.PrecioHasta,
            x => x.InformacionPrecio, x => x.Especialidad, x => x.Capacidad, x => x.RequiereReserva,
            x => x.ServicioDomicilio, x => x.EnlaceExterno, x => x.Destacado, x => x.Activo);

        await ValidarAsync(entidad, fotos);

        if (actualizado && ModelState.IsValid)
        {
            entidad.FechaActualizacion = DateTime.Now;
            await AsignarServiciosAsync(entidad, serviciosSeleccionados);

            var (imagenes, _) = await _almacen.CrearImagenesAsync(fotos, CarpetaFotos, entidad.Nombre,
                entidad.Imagenes.Any(i => i.EsPrincipal), SiguienteOrden(entidad.Imagenes));
            foreach (var imagen in imagenes) entidad.Imagenes.Add(imagen);

            if (await GuardarAsync())
            {
                TempData["Exito"] = imagenes.Count > 0
                    ? $"Prestador actualizado. Se agregaron {imagenes.Count} foto(s) al álbum."
                    : "Prestador actualizado correctamente.";
                return RedirectToAction(nameof(Details), new { id = entidad.IdPrestador });
            }

            foreach (var imagen in imagenes) _almacen.Eliminar(imagen.UrlImagen);
        }

        await CargarListasAsync(entidad, serviciosSeleccionados);
        return View(entidad);
    }

    // GET: Prestadores/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var entidad = await _context.Prestadores.AsNoTracking()
            .Include(x => x.TipoPrestador)
            .Include(x => x.Imagenes)
            .FirstOrDefaultAsync(x => x.IdPrestador == id);

        if (entidad == null) return NotFound();

        return View(entidad);
    }

    // POST: Prestadores/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entidad = await _context.Prestadores
            .Include(x => x.Imagenes)
            .FirstOrDefaultAsync(x => x.IdPrestador == id);

        if (entidad == null) return RedirectToAction(nameof(Index));

        var archivos = entidad.Imagenes.Select(i => i.UrlImagen).ToList();
        _context.Prestadores.Remove(entidad);

        try
        {
            await _context.SaveChangesAsync();
            foreach (var archivo in archivos) _almacen.Eliminar(archivo);
            TempData["Exito"] = "Prestador eliminado correctamente.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "No se puede eliminar el prestador porque está relacionado con otros datos.";
        }

        return RedirectToAction(nameof(Index));
    }

    private const string CamposFormulario =
        "IdTipoPrestador,Nombre,DescripcionCorta,Descripcion,Telefono,WhatsApp,Correo,SitioWeb,RedSocial," +
        "Direccion,Municipio,Provincia,Latitud,Longitud,ZonaCobertura,HorarioAtencion,PrecioDesde,PrecioHasta," +
        "InformacionPrecio,Especialidad,Capacidad,RequiereReserva,ServicioDomicilio,EnlaceExterno,Destacado,Activo";

    private async Task ValidarAsync(Prestador entidad, List<IFormFile>? fotos)
    {
        if (!await _context.TiposPrestador.AnyAsync(t => t.IdTipoPrestador == entidad.IdTipoPrestador))
            ModelState.AddModelError(nameof(entidad.IdTipoPrestador), "Seleccione el tipo de servicio.");

        if (entidad.PrecioDesde < 0 || entidad.PrecioHasta < 0)
            ModelState.AddModelError(nameof(entidad.PrecioDesde), "El precio no puede ser negativo.");
        else if (entidad.PrecioDesde > entidad.PrecioHasta)
            ModelState.AddModelError(nameof(entidad.PrecioHasta), "El precio máximo debe ser mayor o igual que el mínimo.");

        foreach (var (campo, valor) in new[]
                 {
                     (nameof(entidad.SitioWeb), entidad.SitioWeb),
                     (nameof(entidad.RedSocial), entidad.RedSocial),
                     (nameof(entidad.EnlaceExterno), entidad.EnlaceExterno)
                 })
        {
            if (!string.IsNullOrWhiteSpace(valor) && SitioPublico.UrlSegura(valor) == null)
                ModelState.AddModelError(campo, "Escriba una dirección que empiece con http:// o https://.");
        }

        if ((entidad.Latitud == null) != (entidad.Longitud == null))
            ModelState.AddModelError(nameof(entidad.Longitud), "Escriba la latitud y la longitud, o deje las dos vacías.");

        foreach (var foto in fotos ?? new List<IFormFile>())
        {
            if (AlmacenImagenes.Validar(foto) is { } error)
                ModelState.AddModelError("fotos", error);
        }
    }

    // Sincroniza la tabla PrestadorServicio (comodidades) con lo marcado en el formulario
    private async Task AsignarServiciosAsync(Prestador entidad, int[] servicios)
    {
        entidad.Servicios.Clear();
        foreach (var servicio in await _context.Servicios.Where(s => servicios.Contains(s.IdServicio)).ToListAsync())
        {
            entidad.Servicios.Add(servicio);
        }
    }

    private async Task CargarListasAsync(Prestador entidad, int[]? servicios = null, string? clase = null)
    {
        var seleccion = (servicios ?? entidad.Servicios.Select(s => s.IdServicio)).ToHashSet();

        // Tipos activos (y el actual aunque esté inactivo), con su clase y grupo para el formulario
        ViewBag.Tipos = await _context.TiposPrestador.AsNoTracking()
            .Where(t => t.Activo || t.IdTipoPrestador == entidad.IdTipoPrestador)
            .OrderBy(t => t.Clase).ThenBy(t => t.Nombre)
            .ToListAsync();

        ViewBag.Servicios = await _context.Servicios.AsNoTracking()
            .Where(s => s.Activo || seleccion.Contains(s.IdServicio))
            .OrderBy(s => s.Nombre).ToListAsync();

        ViewBag.ServiciosSeleccionados = seleccion;
        ViewBag.ClaseInicial = ClasesPrestador.Todas.Contains(clase) ? clase : null;
    }

    private static int SiguienteOrden(IEnumerable<Imagen> imagenes) =>
        imagenes.Select(i => i.OrdenVisualizacion).DefaultIfEmpty(-1).Max() + 1;

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
                "No se pudo guardar. Verifique que los datos sean correctos.");
            return false;
        }
    }
}
