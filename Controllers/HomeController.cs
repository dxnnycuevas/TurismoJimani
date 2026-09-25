using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Models;

namespace AppDonnyCuevas20210074.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly TurismoJimaniContext _context;

    public HomeController(TurismoJimaniContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        // Cantidad de registros por módulo: (título, controlador, total, solo administrador)
        var modulos = new List<(string Titulo, string Controlador, int Total, bool SoloAdmin)>
        {
            ("Lugares", "Lugares", await _context.Lugares.CountAsync(), false),
            ("Atractivos", "Atractivos", await _context.Atractivos.CountAsync(), false),
            ("Alojamientos", "Alojamientos", await _context.Alojamientos.CountAsync(), false),
            ("Restaurantes", "Restaurantes", await _context.Restaurantes.CountAsync(), false),
            ("Transportes", "Transportes", await _context.Transportes.CountAsync(), false),
            ("Rutas", "Rutas", await _context.Rutas.CountAsync(), false),
            ("Categorías", "Categorias", await _context.Categorias.CountAsync(), false),
            ("Servicios", "Servicios", await _context.Servicios.CountAsync(), false),
            ("Eventos y promociones", "Eventos", await _context.Eventos.CountAsync(), false),
            ("Datos curiosos", "DatosCuriosos", await _context.DatosCuriosos.CountAsync(), false),
            ("Imágenes", "Imagenes", await _context.Imagenes.CountAsync(), false),
            ("Horarios", "Horarios", await _context.Horarios.CountAsync(), false),
            ("Contactos", "Contactos", await _context.Contactos.CountAsync(), false),
            ("Intenciones", "Intenciones", await _context.Intenciones.CountAsync(), false),
            ("Ejemplos de intención", "EjemplosIntencion", await _context.EjemplosIntencion.CountAsync(), false),
            ("Documentos de conocimiento", "DocumentosConocimiento", await _context.DocumentosConocimiento.CountAsync(), false),
            ("Consultas al asistente", "ConsultasAsistente", await _context.ConsultasAsistente.CountAsync(), false),
            ("Usuarios", "Usuarios", await _context.Usuarios.CountAsync(), true),
            ("Roles", "Roles", await _context.Roles.CountAsync(), true),
        };

        ViewBag.Modulos = modulos
            .Where(m => !m.SoloAdmin || User.IsInRole("Administrador"))
            .ToList();

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
