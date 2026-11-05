using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AppDonnyCuevas20210074.Helpers;

// Encabezados de seguridad HTTP (S-07).
// - Content-Security-Policy con un nonce aleatorio por petición: solo se ejecutan los <script> que lleven ese
//   nonce (NonceScriptTagHelper lo agrega a todos los de las vistas) y los que estos carguen ('strict-dynamic'),
//   como el traductor de Google. Un script inyectado por un atacante no tendría el nonce y no se ejecutaría.
// - X-Content-Type-Options, X-Frame-Options / frame-ancestors (solo el propio sitio puede enmarcar sus páginas,
//   lo que necesita el traductor de Google), Referrer-Policy y Permissions-Policy.
// Con Seguridad:CspSoloReporte = true la política se envía en modo de solo reporte (no bloquea), útil para
// probar cambios sin afectar el sitio.
public static class EncabezadosSeguridad
{
    public const string ClaveNonce = "csp-nonce";

    public static IApplicationBuilder UseEncabezadosSeguridad(this IApplicationBuilder app, IWebHostEnvironment entorno, IConfiguration configuracion)
    {
        var soloReporte = configuracion.GetValue("Seguridad:CspSoloReporte", false);
        var encabezadoCsp = soloReporte ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
        var esDesarrollo = entorno.IsDevelopment();

        return app.Use(async (contexto, siguiente) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            contexto.Items[ClaveNonce] = nonce;

            contexto.Response.OnStarting(() =>
            {
                var encabezados = contexto.Response.Headers;
                encabezados["X-Content-Type-Options"] = "nosniff";
                encabezados["X-Frame-Options"] = "SAMEORIGIN";
                encabezados["Referrer-Policy"] = "strict-origin-when-cross-origin";
                encabezados["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                encabezados[encabezadoCsp] = Politica(nonce, esDesarrollo);
                return Task.CompletedTask;
            });

            await siguiente();
        });
    }

    // Orígenes externos que usa el sitio:
    // Google Fonts (fuentes), Font Awesome (iconos), jsDelivr (Chart.js en el panel),
    // traductor web de Google (selector de idioma) y Google Maps (mapas incrustados).
    private static string Politica(string nonce, bool esDesarrollo)
    {
        var directivas = new List<string>
        {
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}' 'strict-dynamic' https://cdn.jsdelivr.net https://translate.google.com https://translate.googleapis.com https://www.gstatic.com",
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://use.fontawesome.com https://www.gstatic.com https://translate.googleapis.com",
            "font-src 'self' data: https://fonts.gstatic.com https://use.fontawesome.com",
            "img-src 'self' data: blob: http: https:",
            "connect-src 'self' https://translate.googleapis.com https://translate-pa.googleapis.com https://translate.google.com" + (esDesarrollo ? " ws: wss:" : ""),
            "frame-src 'self' https://maps.google.com https://www.google.com https://translate.google.com https://translate.googleapis.com",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'self'"
        };

        if (!esDesarrollo)
            directivas.Add("upgrade-insecure-requests");

        return string.Join("; ", directivas);
    }
}

// Agrega el nonce de la CSP a todas las etiquetas <script> de las vistas
[HtmlTargetElement("script")]
public class NonceScriptTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    // Se ejecuta después de los demás tag helpers de <script> (por ejemplo, asp-append-version)
    public override int Order => 1000;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (ViewContext.HttpContext.Items[EncabezadosSeguridad.ClaveNonce] is string nonce &&
            !output.Attributes.ContainsName("nonce"))
        {
            output.Attributes.SetAttribute("nonce", nonce);
        }
    }
}
