using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Services.Asistente;
using AppDonnyCuevas20210074.Services.Seguridad;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Fuera de desarrollo las cookies solo viajan por HTTPS (S-04). En desarrollo se permite el perfil
// http://localhost para poder probar sin certificado.
var cookieSegura = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

// Base de datos. La cadena de conexión no se guarda en appsettings.json (S-04): en desarrollo está en
// appsettings.Development.json o en user-secrets (ninguno se sube a GitHub) y en el servidor se define con
// la variable de entorno ConnectionStrings__TurismoJimani.
var cadenaConexion = builder.Configuration.GetConnectionString("TurismoJimani");
if (string.IsNullOrWhiteSpace(cadenaConexion))
{
    throw new InvalidOperationException(
        "Falta la cadena de conexión 'TurismoJimani'. Defínala en appsettings.Development.json, con " +
        "dotnet user-secrets set \"ConnectionStrings:TurismoJimani\" \"...\" o con la variable de entorno " +
        "ConnectionStrings__TurismoJimani.");
}

builder.Services.AddDbContext<TurismoJimaniContext>(options =>
    options.UseSqlServer(cadenaConexion));

// Autenticación mediante cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Cuenta/Login";
        options.LogoutPath = "/Cuenta/Logout";
        options.AccessDeniedPath = "/Cuenta/AccesoDenegado";

        options.Cookie.Name = "TurismoJimani.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSegura;
        options.Cookie.SameSite = SameSiteMode.Lax;

        // La sesión se cierra tras N minutos sin actividad (Sesion:MinutosInactividad, 20 por defecto).
        // Cada petición renueva la cookie, así el plazo cuenta desde la última actividad.
        options.ExpireTimeSpan = TimeSpan.FromMinutes(
            builder.Configuration.GetValue("Sesion:MinutosInactividad", 20));
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = contexto =>
        {
            contexto.ShouldRenew = true;
            return Task.CompletedTask;
        };
    });

// Las demás cookies del servidor (antifalsificación, TempData) también son HttpOnly y seguras
builder.Services.Configure<CookiePolicyOptions>(opciones =>
{
    opciones.Secure = cookieSegura;
    opciones.HttpOnly = HttpOnlyPolicy.Always;
    opciones.MinimumSameSitePolicy = SameSiteMode.Lax;
});

builder.Services.AddAuthorization();

// Todas las acciones requieren personal (Administrador o Editor), salvo las marcadas con [AllowAnonymous].
// Los visitantes registrados en el sitio público no pueden entrar al panel.
builder.Services.AddControllersWithViews(options =>
{
    var politica = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(RolesSistema.Personal)
        .Build();

    options.Filters.Add(new AuthorizeFilter(politica));

    // Si la contraseña debe cambiarse (inicial o que ya no cumple la política), solo se permite cambiarla (S-03)
    options.Filters.Add<ExigirCambioContrasenaFilter>();

    // Mensajes de validación en español
    options.ModelMetadataDetailsProviders.Add(new MensajesValidacionProvider());

    var mensajes = options.ModelBindingMessageProvider;
    mensajes.SetValueMustNotBeNullAccessor(_ => "Este campo es obligatorio.");
    mensajes.SetMissingBindRequiredValueAccessor(_ => "Este campo es obligatorio.");
    mensajes.SetMissingKeyOrValueAccessor(() => "Este campo es obligatorio.");
    mensajes.SetAttemptedValueIsInvalidAccessor((valor, _) => $"El valor '{valor}' no es válido.");
    mensajes.SetUnknownValueIsInvalidAccessor(_ => "El valor no es válido.");
    mensajes.SetValueIsInvalidAccessor(valor => $"El valor '{valor}' no es válido.");
    mensajes.SetValueMustBeANumberAccessor(_ => "Debe ser un número.");
    mensajes.SetNonPropertyAttemptedValueIsInvalidAccessor(valor => $"El valor '{valor}' no es válido.");
    mensajes.SetNonPropertyUnknownValueIsInvalidAccessor(() => "El valor no es válido.");
    mensajes.SetNonPropertyValueMustBeANumberAccessor(() => "Debe ser un número.");
});

// Asistente turístico: cliente HTTP del servicio BERT (Python) y servicio del chatbot
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<IClasificadorIntenciones, ClasificadorBert>(cliente =>
{
    var url = builder.Configuration["ServicioBert:Url"] ?? "http://127.0.0.1:8000/";
    cliente.BaseAddress = new Uri(url.TrimEnd('/') + "/");
    cliente.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("ServicioBert:TimeoutSegundos", 15));
});

builder.Services.AddScoped<ChatbotService>();

// Fotos subidas desde el panel (wwwroot/uploads)
builder.Services.AddSingleton<AlmacenImagenes>();

// Seguridad del acceso: bloqueo por intentos fallidos (S-02) y validación de contraseñas (S-01)
builder.Services.AddSingleton<ProteccionAcceso>();
builder.Services.AddHttpClient<ValidadorContrasenas>(cliente =>
{
    cliente.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
    cliente.Timeout = TimeSpan.FromSeconds(4);
    cliente.DefaultRequestHeaders.UserAgent.ParseAdd("TurismoJimani/1.0");
    cliente.DefaultRequestHeaders.Add("Add-Padding", "true");
});

// Límites de peticiones por dirección IP (la IP real del visitante, ver UseForwardedHeaders):
// - "chat": 20 mensajes por minuto al asistente (el chat es público);
// - "acceso": 10 envíos por minuto a los formularios de inicio de sesión, registro y cambio de contraseña (S-02).
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    opciones.AddPolicy("chat", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));

    opciones.AddPolicy("acceso", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Seguridad:EnviosAccesoPorMinuto", 10),
            Window = TimeSpan.FromMinutes(1)
        }));

    // En los formularios de acceso se muestra un mensaje en lugar de una página vacía
    opciones.OnRejected = async (contexto, cancelacion) =>
    {
        var ruta = contexto.HttpContext.Request.Path;
        if (ruta.StartsWithSegments("/Cuenta") || ruta.StartsWithSegments("/admin"))
        {
            contexto.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
            await contexto.HttpContext.Response.WriteAsync(
                "Demasiados intentos en poco tiempo. Espere un minuto y vuelva a intentarlo.", cancelacion);
        }
    };
});

// Proxy inverso (Nginx) - S-05. Detrás del proxy todas las peticiones llegan desde 127.0.0.1; con estos
// encabezados la aplicación usa la IP real del visitante (para el límite de peticiones y los registros) y
// sabe que la conexión original fue HTTPS (para la redirección HTTPS, HSTS y las cookies seguras).
// Solo se confía en los encabezados que envía un proxy conocido (Proxy:IpsConocidas).
builder.Services.Configure<ForwardedHeadersOptions>(opciones =>
{
    opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opciones.ForwardLimit = 1;

    var ipsConocidas = builder.Configuration.GetSection("Proxy:IpsConocidas").Get<string[]>()
        ?? new[] { "127.0.0.1", "::1" };

    foreach (var texto in ipsConocidas)
    {
        if (IPAddress.TryParse(texto, out var ip) && !opciones.KnownProxies.Contains(ip))
            opciones.KnownProxies.Add(ip);
    }
});

// HSTS de un año fuera de desarrollo
builder.Services.AddHsts(opciones =>
{
    opciones.MaxAge = TimeSpan.FromDays(365);
});

// Cultura de República Dominicana (punto decimal, fechas dd/MM/yyyy)
var cultura = new CultureInfo("es-DO");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

var app = builder.Build();

// Debe ir primero: el resto del pipeline (HTTPS, HSTS, límites por IP) usa la IP y el esquema reales
app.UseForwardedHeaders();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(cultura),
    SupportedCultures = new[] { cultura },
    SupportedUICultures = new[] { cultura }
});

// Roles (Administrador, Editor, Visitante) y usuario administrador inicial
DbInitializer.Inicializar(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Encabezados de seguridad y política CSP con nonce (S-07)
app.UseEncabezadosSeguridad(app.Environment, app.Configuration);

// Página propia para los errores sin contenido (por ejemplo, un lugar que no existe) en lugar
// de la página genérica del navegador (PF-03). Solo en peticiones GET: las respuestas del chat
// (400, 429) siguen siendo JSON o vacías para que chat.js las interprete.
app.UseWhen(contexto => HttpMethods.IsGet(contexto.Request.Method),
    rama => rama.UseStatusCodePagesWithReExecute("/Sitio/Estado/{0}"));

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseCookiePolicy();

app.UseRouting();
app.UseRateLimiter();

// IMPORTANTE: primero autenticación y luego autorización
app.UseAuthentication();
app.UseAuthorization();

// Evita que el navegador guarde en caché las páginas (tras el Logout no se
// podrá volver a ver una página protegida con el botón "Atrás")
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        return Task.CompletedTask;
    });

    await next();
});

// La aplicación inicia en el sitio público (SitioController). El panel de administración
// está en /Home y su acceso en /admin (o /Cuenta/Login); no se enlaza desde el sitio público.
app.MapControllerRoute(
    name: "admin",
    pattern: "admin",
    defaults: new { controller = "Cuenta", action = "Login" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Sitio}/{action=Index}/{id?}");

app.Run();
