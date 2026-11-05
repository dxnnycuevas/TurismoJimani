using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Models;
using AppDonnyCuevas20210074.Models.Cuenta;
using AppDonnyCuevas20210074.Services.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AppDonnyCuevas20210074.Controllers
{
    // Acceso de administración (Login/Logout, en /admin) y cuentas de visitantes del sitio público
    // (Registro/Ingresar/Salir). Ambos usan la misma cookie; el rol decide si se puede entrar al panel.
    // Controles de seguridad: límite de peticiones por IP ("acceso"), bloqueo temporal por intentos
    // fallidos (ProteccionAcceso), política de contraseñas (ValidadorContrasenas) y cambio obligatorio
    // de contraseña (CambiarContrasena).
    [PermitirConCambioPendiente]
    public class CuentaController : Controller
    {
        private const string NombreCookie = "TurismoJimani.Auth";

        private readonly TurismoJimaniContext _context;
        private readonly ProteccionAcceso _proteccion;
        private readonly ValidadorContrasenas _validador;
        private readonly IConfiguration _configuracion;
        private readonly PasswordHasher<Usuario> _hasher = new();

        // Hash de una contraseña aleatoria: se verifica cuando el correo no existe para que la respuesta
        // tarde lo mismo y no revele qué correos están registrados
        private static readonly Lazy<string> HashFicticio = new(() =>
            new PasswordHasher<Usuario>().HashPassword(new Usuario(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));

        public CuentaController(
            TurismoJimaniContext context,
            ProteccionAcceso proteccion,
            ValidadorContrasenas validador,
            IConfiguration configuracion)
        {
            _context = context;
            _proteccion = proteccion;
            _validador = validador;
            _configuracion = configuracion;
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
        [EnableRateLimiting("acceso")]
        public async Task<IActionResult> Login(string correo, string contrasena, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            if (string.IsNullOrWhiteSpace(correo) ||
                string.IsNullOrWhiteSpace(contrasena))
            {
                ViewBag.Error = "Debe completar todos los campos.";
                return View();
            }

            if (_proteccion.EstaBloqueado(correo, out var minutos))
            {
                ViewBag.Error = MensajeBloqueo(minutos);
                return View();
            }

            var usuario = await BuscarUsuarioAsync(correo, contrasena);

            // Solo el personal (Administrador o Editor) entra al panel. Mismo mensaje en ambos
            // casos para no revelar si el correo existe.
            if (usuario == null || !RolesSistema.EsPersonal(usuario.Rol?.Nombre))
            {
                _proteccion.RegistrarFallo(correo);
                ViewBag.Error = "Correo o contraseña incorrectos, o la cuenta no tiene acceso a la administración.";
                return View();
            }

            return await CompletarInicioSesionAsync(usuario, contrasena, returnUrl, alPanel: true);
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

        private async Task ValidarRegistroUnicoAsync(RegistroViewModel modelo, string numeroDocumento)
        {
            if (await _context.Usuarios.AnyAsync(u => u.Correo == modelo.Correo))
                ModelState.AddModelError(nameof(modelo.Correo), "Ya existe una cuenta con este correo.");

            if (await _context.Usuarios.AnyAsync(u => u.TipoDocumento == modelo.TipoDocumento && u.NumeroDocumento == numeroDocumento))
                ModelState.AddModelError(nameof(modelo.NumeroDocumento), "Ya existe una cuenta con este documento.");
        }

        private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

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
        [EnableRateLimiting("acceso")]
        public async Task<IActionResult> Registro(RegistroViewModel modelo, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            modelo.NombreCompleto = modelo.NombreCompleto?.Trim() ?? string.Empty;
            modelo.Correo = modelo.Correo?.Trim() ?? string.Empty;
            var numeroDocumento = DocumentoIdentidad.Normalizar(modelo.NumeroDocumento);

            if (ModelState.IsValid)
            {
                foreach (var error in await _validador.ValidarAsync(modelo.Contrasena, modelo.Correo, HttpContext.RequestAborted))
                    ModelState.AddModelError(nameof(modelo.Contrasena), error);
            }

            if (ModelState.IsValid)
                await ValidarRegistroUnicoAsync(modelo, numeroDocumento);

            if (!ModelState.IsValid)
            {
                return View(modelo);
            }

            var rolVisitante = await _context.Roles.FirstAsync(r => r.Nombre == RolesSistema.Visitante);
            var esEmpresa = modelo.TipoCuenta == DocumentoIdentidad.CuentaEmpresa;

            var usuario = new Usuario
            {
                IdRol = rolVisitante.IdRol,
                NombreCompleto = modelo.NombreCompleto,
                Correo = modelo.Correo,
                TipoCuenta = modelo.TipoCuenta,
                TipoDocumento = modelo.TipoDocumento,
                NumeroDocumento = numeroDocumento,
                NombreEmpresa = esEmpresa ? modelo.NombreEmpresa?.Trim() : null,
                Telefono = modelo.Telefono.Trim(),
                FechaNacimiento = esEmpresa ? null : modelo.FechaNacimiento?.Date,
                Nacionalidad = esEmpresa ? null : modelo.Nacionalidad?.Trim(),
                Pais = Limpio(modelo.Pais),
                Ciudad = Limpio(modelo.Ciudad),
                Direccion = Limpio(modelo.Direccion),
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
                // Otro registro con el mismo correo o documento se guardó al mismo tiempo (índices únicos)
                _context.ChangeTracker.Clear();
                await ValidarRegistroUnicoAsync(modelo, numeroDocumento);
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
        [EnableRateLimiting("acceso")]
        public async Task<IActionResult> Ingresar(string correo, string contrasena, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Correo = correo;

            if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(contrasena))
            {
                ViewBag.Error = "Escribe tu correo y tu contraseña.";
                return View();
            }

            if (_proteccion.EstaBloqueado(correo, out var minutos))
            {
                ViewBag.Error = MensajeBloqueo(minutos);
                return View();
            }

            var usuario = await BuscarUsuarioAsync(correo, contrasena);
            if (usuario == null)
            {
                _proteccion.RegistrarFallo(correo);
                ViewBag.Error = "Correo o contraseña incorrectos.";
                return View();
            }

            return await CompletarInicioSesionAsync(usuario, contrasena, returnUrl, alPanel: false);
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

        // ================= Cambio de contraseña =================

        // GET: /Cuenta/CambiarContrasena
        // Disponible para cualquier usuario conectado (personal o visitante). Es obligatorio cuando la cookie
        // trae el claim de cambio pendiente (ver Helpers/CambioContrasenaObligatorio.cs).
        [AllowAnonymous]
        [HttpGet]
        public IActionResult CambiarContrasena(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated != true)
                return RedirectToAction(nameof(Ingresar), new { returnUrl = Url.Action(nameof(CambiarContrasena)) });

            ViewBag.ReturnUrl = returnUrl;
            return View(new CambiarContrasenaViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("acceso")]
        public async Task<IActionResult> CambiarContrasena(CambiarContrasenaViewModel modelo, string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated != true)
                return RedirectToAction(nameof(Ingresar));

            ViewBag.ReturnUrl = returnUrl;

            var id = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var numero) ? numero : 0;
            var usuario = await _context.Usuarios
                .Include(u => u.Rol)
                .FirstOrDefaultAsync(u => u.IdUsuario == id && u.Activo);

            if (usuario == null)
            {
                await CerrarSesionAsync();
                return RedirectToAction(nameof(Ingresar));
            }

            if (_proteccion.EstaBloqueado(usuario.Correo, out var minutos))
            {
                ModelState.AddModelError(string.Empty, MensajeBloqueo(minutos));
                return View(modelo);
            }

            if (ModelState.IsValid)
            {
                if (_hasher.VerifyHashedPassword(usuario, usuario.ContrasenaHash, modelo.ContrasenaActual) == PasswordVerificationResult.Failed)
                {
                    _proteccion.RegistrarFallo(usuario.Correo);
                    ModelState.AddModelError(nameof(modelo.ContrasenaActual), "La contraseña actual no es correcta.");
                }
                else if (modelo.NuevaContrasena == modelo.ContrasenaActual || EsContrasenaInicial(modelo.NuevaContrasena))
                {
                    ModelState.AddModelError(nameof(modelo.NuevaContrasena), "La nueva contraseña debe ser distinta de la actual y de la inicial.");
                }
                else
                {
                    foreach (var error in await _validador.ValidarAsync(modelo.NuevaContrasena, usuario.Correo, HttpContext.RequestAborted))
                        ModelState.AddModelError(nameof(modelo.NuevaContrasena), error);
                }
            }

            if (!ModelState.IsValid)
                return View(modelo);

            usuario.ContrasenaHash = _hasher.HashPassword(usuario, modelo.NuevaContrasena);
            usuario.FechaActualizacion = DateTime.Now;
            await _context.SaveChangesAsync();

            _proteccion.Reiniciar(usuario.Correo);

            // Nueva cookie, ya sin el aviso de cambio pendiente
            await IniciarSesionAsync(usuario);
            TempData["Exito"] = "La contraseña se cambió correctamente.";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RolesSistema.EsPersonal(usuario.Rol?.Nombre)
                ? RedirectToAction("Index", "Home")
                : RedirectToAction("Index", "Sitio");
        }

        // ================= Comunes =================

        // Usuario activo cuyo correo y contraseña coinciden, o null
        private async Task<Usuario?> BuscarUsuarioAsync(string correo, string contrasena)
        {
            var usuario = await _context.Usuarios
                .Include(u => u.Rol)
                .FirstOrDefaultAsync(u => u.Correo == correo.Trim() && u.Activo);

            if (usuario == null)
            {
                // Misma demora que una verificación real (evita enumerar correos por el tiempo de respuesta)
                _hasher.VerifyHashedPassword(new Usuario(), HashFicticio.Value, contrasena);
                return null;
            }

            if (_hasher.VerifyHashedPassword(usuario, usuario.ContrasenaHash, contrasena) == PasswordVerificationResult.Failed)
            {
                return null;
            }

            return usuario;
        }

        // Inicia la sesión y, si la contraseña usada es la inicial o ya no cumple la política, obliga a cambiarla
        private async Task<IActionResult> CompletarInicioSesionAsync(Usuario usuario, string contrasena, string? returnUrl, bool alPanel)
        {
            _proteccion.Reiniciar(usuario.Correo);

            var debeCambiar = EsContrasenaInicial(contrasena) || !PoliticaContrasena.Cumple(contrasena, usuario.Correo);
            await IniciarSesionAsync(usuario, debeCambiar);

            if (debeCambiar)
            {
                TempData["Aviso"] = "Por seguridad, debe cambiar su contraseña antes de continuar.";
                var destino = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
                return RedirectToAction(nameof(CambiarContrasena), new { returnUrl = destino });
            }

            if (alPanel)
            {
                return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                    ? LocalRedirect(returnUrl)
                    : RedirectToAction("Index", "Home");
            }

            TempData["Exito"] = $"Hola de nuevo, {usuario.NombreCompleto}.";
            return RedirigirAlSitio(returnUrl);
        }

        // true si coincide con la contraseña inicial del administrador definida en la configuración
        // (AdminInicial:Contrasena, en user-secrets o en una variable de entorno)
        private bool EsContrasenaInicial(string contrasena)
        {
            var inicial = _configuracion["AdminInicial:Contrasena"];
            if (string.IsNullOrEmpty(inicial) || string.IsNullOrEmpty(contrasena))
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(contrasena), Encoding.UTF8.GetBytes(inicial));
        }

        private string MensajeBloqueo(int minutos) =>
            $"Demasiados intentos fallidos. Por seguridad, el acceso con este correo quedó bloqueado; " +
            $"inténtelo de nuevo en {minutos} {(minutos == 1 ? "minuto" : "minutos")}.";

        private async Task IniciarSesionAsync(Usuario usuario, bool cambioPendiente = false)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
                new Claim(ClaimTypes.Name, usuario.NombreCompleto),
                new Claim(ClaimTypes.Email, usuario.Correo),
                // Permite usar [Authorize(Roles = "Administrador")] o [Authorize(Roles = "Editor")]
                new Claim(ClaimTypes.Role, usuario.Rol?.Nombre ?? string.Empty)
            };

            if (cambioPendiente)
                claims.Add(new Claim(CambioContrasena.ClaimPendiente, "1"));

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
