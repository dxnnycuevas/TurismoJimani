using System.Globalization;
using AppDonnyCuevas20210074.Models;

namespace AppDonnyCuevas20210074.Helpers;

// Utilidades de presentación para el sitio público
public static class SitioPublico
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-DO");

    // Filtros del listado de destinos: (clave en la URL, nombre visible, icono de Font Awesome)
    public static readonly (string Clave, string Nombre, string Icono)[] TiposLugar =
    {
        ("atractivos", "Atractivos", "fa-mountain"),
        ("alojamientos", "Alojamientos", "fa-hotel"),
        ("restaurantes", "Restaurantes", "fa-utensils"),
        ("transporte", "Transporte", "fa-bus")
    };

    // ---------- Imágenes y créditos ----------

    // Solo imágenes con dirección web válida (evita enlaces "javascript:" escritos en la administración)
    public static IEnumerable<Imagen> ImagenesOrdenadas(Lugar lugar) => lugar.Imagenes
        .Where(i => UrlSegura(i.UrlImagen) != null)
        .OrderByDescending(i => i.EsPrincipal)
        .ThenBy(i => i.OrdenVisualizacion);

    // Imagen principal del lugar, o null si no tiene (entonces se muestra una ilustración)
    public static Imagen? ImagenPrincipal(Lugar lugar) => ImagenesOrdenadas(lugar).FirstOrDefault();

    // "Foto: Autor · Licencia" (vacío si la imagen no tiene crédito)
    public static string Credito(string? autor, string? licencia = null)
    {
        var partes = new[] { autor, licencia }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray();
        return partes.Length == 0 ? "" : "Foto: " + string.Join(" · ", partes);
    }

    public static string Credito(Imagen imagen) => Credito(imagen.Autor, imagen.Licencia);

    // Tipo de ilustración que sustituye a la foto cuando no hay imágenes registradas
    public static string Ilustracion(Lugar lugar) =>
        lugar.Atractivo != null ? "atractivo"
        : lugar.Alojamiento != null ? "alojamiento"
        : lugar.Restaurante != null ? "restaurante"
        : lugar.Transporte != null ? "transporte"
        : "lugar";

    // Devuelve la URL solo si es http(s) o una ruta del propio sitio; si no, null
    public static string? UrlSegura(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.StartsWith('/') && !url.StartsWith("//")) return url;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;
    }

    // ---------- Lugares ----------

    // Tipo visible del lugar según la tabla de detalle que tenga asociada
    public static string Tipo(Lugar lugar) =>
        lugar.Atractivo != null ? lugar.Atractivo.TipoAtractivo ?? "Atractivo"
        : lugar.Alojamiento != null ? lugar.Alojamiento.TipoAlojamiento ?? "Alojamiento"
        : lugar.Restaurante != null ? lugar.Restaurante.TipoComida ?? "Restaurante"
        : lugar.Transporte != null ? lugar.Transporte.TipoTransporte
        : "Lugar de interés";

    public static string Icono(Lugar lugar) =>
        lugar.Atractivo != null ? "fa-mountain"
        : lugar.Alojamiento != null ? "fa-hotel"
        : lugar.Restaurante != null ? "fa-utensils"
        : lugar.Transporte != null ? "fa-bus"
        : "fa-map-marker-alt";

    public static string Ubicacion(Lugar lugar) =>
        string.Join(", ", new[] { lugar.Municipio, lugar.Provincia }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static string Duracion(int? minutos)
    {
        if (minutos is not > 0) return "";
        var horas = minutos.Value / 60;
        var resto = minutos.Value % 60;
        if (horas == 0) return $"{resto} min";
        return resto == 0 ? $"{horas} h" : $"{horas} h {resto} min";
    }

    public static string Precio(decimal? valor) =>
        valor.HasValue ? "RD$ " + valor.Value.ToString("#,##0", Cultura) : "";

    // ---------- Eventos ----------

    public static string? ImagenEvento(Evento evento) => UrlSegura(evento.ImagenUrl);

    public static string LugarEvento(Evento evento) => evento.Lugar?.Nombre ?? evento.LugarTexto ?? "";

    // Igual que en SitioController: sigue en la agenda hasta el día en que termina
    public static bool EsProximo(Evento evento) =>
        (evento.FechaFin ?? evento.FechaInicio) >= DateTime.Today;

    public static string DiaEvento(Evento evento) => evento.FechaInicio.Day.ToString(Cultura);

    public static string MesEvento(Evento evento) =>
        evento.FechaInicio.ToString("MMM", Cultura).TrimEnd('.').ToUpper(Cultura);

    // "sábado 12 de octubre de 2026, 7:00 p. m." o un rango si dura varios días
    public static string FechaEvento(Evento evento)
    {
        var inicio = evento.FechaInicio;
        var texto = inicio.ToString("dddd d 'de' MMMM 'de' yyyy", Cultura);
        if (inicio.TimeOfDay != TimeSpan.Zero)
            texto += ", " + inicio.ToString("h:mm tt", Cultura);

        if (evento.FechaFin is DateTime fin && fin.Date != inicio.Date)
            texto += " al " + fin.ToString("dddd d 'de' MMMM", Cultura);

        return char.ToUpper(texto[0], Cultura) + texto[1..];
    }

    public static string IconoEvento(string tipo) => tipo switch
    {
        "Fiesta patronal" => "fa-church",
        "Festival" => "fa-music",
        "Actividad cultural" => "fa-theater-masks",
        "Religioso" => "fa-praying-hands",
        "Deportivo" => "fa-running",
        "Gastronómico" => "fa-utensils",
        "Promoción" => "fa-tags",
        _ => "fa-calendar-alt"
    };

    // ---------- Mapas ----------

    // Mapa incrustado de Google Maps (no requiere clave de API)
    public static string? UrlMapa(decimal? latitud, decimal? longitud, int zoom = 13)
    {
        if (latitud is null || longitud is null) return null;
        var coordenadas = string.Create(CultureInfo.InvariantCulture, $"{latitud},{longitud}");
        return $"https://maps.google.com/maps?q={coordenadas}&z={zoom}&output=embed";
    }

    public static string UrlMapa(string consulta, int zoom = 12) =>
        $"https://maps.google.com/maps?q={Uri.EscapeDataString(consulta)}&z={zoom}&output=embed";

    public static string? UrlComoLlegar(decimal? latitud, decimal? longitud)
    {
        if (latitud is null || longitud is null) return null;
        var coordenadas = string.Create(CultureInfo.InvariantCulture, $"{latitud},{longitud}");
        return $"https://www.google.com/maps/dir/?api=1&destination={coordenadas}";
    }
}
