using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using AppDonnyCuevas20210074.Models.Cuenta;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AppDonnyCuevas20210074.Controllers
{
    // Acceso de administración (Login/Logout, en /admin) y cuentas de visitantes del sitio público
    // (Registro/Ingresar/Salir). Ambos usan la misma cookie; el rol decide si se puede entrar al panel.
    public class CuentaController : Controller
    {
        private const string NombreCookie = "TurismoJimani.Auth";

        private readonly TurismoJimaniContext _context;
        private readonly PasswordHasher<Usuario> _hasher = new();

        public CuentaController(TurismoJimaniContext context)
        {
            _context = context;
        }

        // ================= Administración =================

        // GET: /admin  (o /Cuenta/Login). "expirada" llega desde el aviso de inactividad del panel.
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null, bool expirada = false)
        {
            if (expirada)
            {
                // Se redirige para que la página de acceso ya se cargue sin la sesión anterior
                await CerrarSesionAsync();
                TempData["Aviso"] = "Tu sesión se cerró por inactividad. Vuelve a iniciar sesión.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            if (RolesSistema.EsPersonal(User))
            {
                // Si ya inició sesión como personal, va directo al panel
                return RedirectToAction("Index", "Home");
            }

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string correo, string contrasena, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            if (string.IsNullOrWhiteSpace(correo) ||
                string.IsNullOrWhiteSpace(contrasena))
            {
                ViewBag.Error = "Debe completar todos los campos.";
                return View();
            }

            var usuario = await BuscarUsuarioAsync(correo, contrasena);

            // Solo el personal (Administrador o Editor) entra al panel. Mismo mensaje en ambos
            // casos para no revelar si el correo existe.
            if (usuario == null || !RolesSistema.EsPersonal(usuario.Rol?.Nombre))
            {
                ViewBag.Error = "Correo o contraseña incorrectos, o la cuenta no tiene acceso a la administración.";
                return View();
            }

            await IniciarSesionAsync(usuario);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await CerrarSesionAsync();
            return RedirectToAction(nameof(Login));
        }

        [AllowAnonymous]
        public IActionResult AccesoDenegado()
        {
            return View();
        }

        // ================= Visitantes (sitio público) =================

        // GET: /Cuenta/Registro
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Registro(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Sitio");

            ViewBag.ReturnUrl = returnUrl;
            return View(new RegistroViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Registro(RegistroViewModel modelo, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            modelo.NombreCompleto = modelo.NombreCompleto?.Trim() ?? string.Empty;
            modelo.Correo = modelo.Correo?.Trim() ?? string.Empty;

            if (ModelState.IsValid && await _context.Usuarios.AnyAsync(u => u.Correo == modelo.Correo))
            {
                ModelState.AddModelError(nameof(modelo.Correo), "Ya existe una cuenta con este correo.");
            }

            if (!ModelState.IsValid)
            {
                return View(modelo);
            }

            var rolVisitante = await _context.Roles.FirstAsync(r => r.Nombre == RolesSistema.Visitante);

            var usuario = new Usuario
            {
                IdRol = rolVisitante.IdRol,
                NombreCompleto = modelo.NombreCompleto,
                Correo = modelo.Correo,
                Activo = true,
                FechaCreacion = DateTime.Now
            };
            usuario.ContrasenaHash = _hasher.HashPassword(usuario, modelo.Contrasena);

            _context.Usuarios.Add(usuario);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Otro registro con el mismo correo se guardó al mismo tiempo (índice único)
                ModelState.AddModelError(nameof(modelo.Correo), "Ya existe una cuenta con este correo.");
                return View(modelo);
            }

            usuario.Rol = rolVisitante;
            await IniciarSesionAsync(usuario);

            TempData["Exito"] = $"¡Bienvenido, {usuario.NombreCompleto}! Tu cuenta fue creada.";
            return RedirigirAlSitio(returnUrl);
        }

        // GET: /Cuenta/Ingresar
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Ingresar(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Sitio");

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ingresar(string correo, string contrasena, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Correo = correo;

            if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(contrasena))
            {
                ViewBag.Error = "Escribe tu correo y tu contraseña.";
                return View();
            }

            var usuario = await BuscarUsuarioAsync(correo, contrasena);
            if (usuario == null)
            {
                ViewBag.Error = "Correo o contraseña incorrectos.";
                return View();
            }

            await IniciarSesionAsync(usuario);

            TempData["Exito"] = $"Hola de nuevo, {usuario.NombreCompleto}.";
            return RedirigirAlSitio(returnUrl);
        }

        // POST: /Cuenta/Salir  (cierre de sesión desde el sitio público)
        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Salir()
        {
            await CerrarSesionAsync();
            return RedirectToAction("Index", "Sitio");
        }

        // ================= Comunes =================

        // Usuario activo cuyo correo y contraseña coinciden, o null
        private async Task<Usuario?> BuscarUsuarioAsync(string correo, string contrasena)
        {
            var usuario = await _context.Usuarios
                .Include(u => u.Rol)
                .FirstOrDefaultAsync(u => u.Correo == correo.Trim() && u.Activo);

            if (usuario == null ||
                _hasher.VerifyHashedPassword(usuario, usuario.ContrasenaHash, contrasena) == PasswordVerificationResult.Failed)
            {
                return null;
            }

            return usuario;
        }

        private async Task IniciarSesionAsync(Usuario usuario)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
                new Claim(ClaimTypes.Name, usuario.NombreCompleto),
                new Claim(ClaimTypes.Email, usuario.Correo),
                // Permite usar [Authorize(Roles = "Administrador")] o [Authorize(Roles = "Editor")]
                new Claim(ClaimTypes.Role, usuario.Rol?.Nombre ?? string.Empty)
            };

            var identidad = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identidad));
        }

        private async Task CerrarSesionAsync()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            // Elimina la cookie de autenticación
            Response.Cookies.Delete(NombreCookie);

            // Evita que el navegador conserve la página protegida
            Response.Headers["Cache-Control"] =
                "no-cache, no-store, must-revalidate";

            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";
        }

        private IActionResult RedirigirAlSitio(string? returnUrl) =>
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction("Index", "Sitio");
    }
}
