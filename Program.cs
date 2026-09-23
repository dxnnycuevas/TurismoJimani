using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using AppDonnyCuevas20210074.Data;
using AppDonnyCuevas20210074.Helpers;
using AppDonnyCuevas20210074.Services.Asistente;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Base de datos
builder.Services.AddDbContext<TurismoJimaniContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("TurismoJimani")));

// Autenticación mediante cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Cuenta/Login";
        options.LogoutPath = "/Cuenta/Logout";
        options.AccessDeniedPath = "/Cuenta/AccesoDenegado";

        options.Cookie.Name = "TurismoJimani.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// Todas las acciones requieren usuario autenticado, salvo las marcadas con [AllowAnonymous]
builder.Services.AddControllersWithViews(options =>
{
    var politica = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new AuthorizeFilter(politica));

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

// Límite de mensajes al chat por dirección IP (el chat es público)
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.AddPolicy("chat", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

// Cultura de República Dominicana (punto decimal, fechas dd/MM/yyyy)
var cultura = new CultureInfo("es-DO");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

var app = builder.Build();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(cultura),
    SupportedCultures = new[] { cultura },
    SupportedUICultures = new[] { cultura }
});

// Roles (Administrador, Editor) y usuario administrador inicial
DbInitializer.Inicializar(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

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

// La aplicación inicia en /Cuenta/Login (si ya inició sesión, va al menú)
app.MapGet("/", context =>
{
    var destino = context.User.Identity?.IsAuthenticated == true
        ? "/Home/Index"
        : "/Cuenta/Login";

    context.Response.Redirect(destino);
    return Task.CompletedTask;
}).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
