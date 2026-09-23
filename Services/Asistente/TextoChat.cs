using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AppDonnyCuevas20210074.Services.Asistente;

// Utilidades de texto del asistente. BERT decide la intención; aquí solo se ubican
// nombres de lugares de la base de datos y referencias como "el segundo" o "ese".
public static partial class TextoChat
{
    private static readonly HashSet<string> PalabrasVacias = new()
    {
        "el", "la", "los", "las", "un", "una", "unos", "unas", "de", "del", "al", "a", "en", "y", "o", "u",
        "que", "por", "para", "con", "sin", "se", "me", "mi", "mis", "tu", "tus", "su", "sus", "es", "son",
        "hay", "lo", "le", "les", "como", "donde", "cual", "cuales", "quiero", "puedo", "puede", "algun",
        "alguna", "algo", "mas", "muy", "este", "esta", "estos", "estas", "ese", "esa", "esos", "esas"
    };

    // Palabras comunes en nombres de lugares que por sí solas no identifican un lugar
    private static readonly HashSet<string> PalabrasGenericasLugar = new()
    {
        "hotel", "hostal", "restaurante", "comedor", "cafeteria", "zona", "natural", "naturales", "parque",
        "nacional", "municipal", "municipio", "centro", "jimani", "lugar", "lugares", "sitio", "ruta", "rutas",
        "republica", "dominicana", "provincia", "independencia"
    };

    // Palabras de la pregunta que no sirven para filtrar por tipo o categoría
    private static readonly HashSet<string> PalabrasDeConsulta = new()
    {
        "lugar", "lugares", "atractivo", "atractivos", "turistico", "turisticos", "turistica", "turisticas",
        "visitar", "conocer", "sitio", "sitios", "ver", "ir", "recomiendas", "recomiendame", "busco", "buscar",
        "jimani", "hotel", "hoteles", "restaurante", "restaurantes", "comer", "transporte", "tienen", "registrados",
        "cerca", "cercanos", "cercanas", "muestrame", "dame", "lista", "opciones", "quisiera", "necesito"
    };

    private static readonly Dictionary<string, int> Ordinales = new()
    {
        ["primero"] = 0, ["primera"] = 0, ["primer"] = 0, ["segundo"] = 1, ["segunda"] = 1,
        ["tercero"] = 2, ["tercera"] = 2, ["tercer"] = 2, ["cuarto"] = 3, ["cuarta"] = 3,
        ["quinto"] = 4, ["quinta"] = 4, ["sexto"] = 5, ["sexta"] = 5, ["septimo"] = 6, ["septima"] = 6,
        ["octavo"] = 7, ["octava"] = 7
    };

    private static readonly HashSet<string> PalabrasReferencia = new()
    {
        "ese", "esa", "eso", "este", "esta", "ahi", "alli", "alla", "aquel", "aquella", "mismo", "misma"
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    [GeneratedRegex(@"\b(?:numero|opcion|nro|el|la)\s+(\d{1,2})\b")]
    private static partial Regex NumeroReferido();

    // Minúsculas, sin tildes ni signos: "¿Dónde está el Lago?" -> "donde esta el lago"
    public static string Normalizar(string texto)
    {
        var descompuesto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesto.Length);

        foreach (var c in descompuesto)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria == UnicodeCategory.NonSpacingMark)
                continue;
            resultado.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return Espacios().Replace(resultado.ToString(), " ").Trim();
    }

    public static string[] Tokens(string normalizado) =>
        normalizado.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Palabras útiles de un texto normalizado (sin artículos, preposiciones, etc.)
    public static List<string> PalabrasClave(string normalizado) =>
        Tokens(normalizado).Where(t => t.Length >= 3 && !PalabrasVacias.Contains(t)).Distinct().ToList();

    // Palabras para filtrar por tipo/categoría ("naturales", "arqueologicos", "criolla"...)
    public static List<string> PalabrasFiltro(string normalizado) =>
        PalabrasClave(normalizado).Where(t => !PalabrasDeConsulta.Contains(t)).ToList();

    // Compara dos palabras por su raíz: "naturales" ~ "natural", "rios" ~ "rio"
    public static bool CoincideRaiz(string a, string b)
    {
        if (a.Length <= 4 || b.Length <= 4)
            return a == b || a + "s" == b || b + "s" == a || a + "es" == b || b + "es" == a;

        return string.CompareOrdinal(a, 0, b, 0, 5) == 0;
    }

    // Busca en el mensaje el nombre de un registro de la base de datos (lugar o ruta).
    // Prioridad: nombre completo > todas sus palabras distintivas > al menos la mitad de ellas.
    public static T? BuscarPorNombre<T>(string mensajeNormalizado, IEnumerable<T> elementos, Func<T, string> nombre)
        where T : class
    {
        var mensaje = " " + mensajeNormalizado + " ";
        var palabrasMensaje = Tokens(mensajeNormalizado).ToHashSet();

        T? mejor = null;
        var mejorPuntaje = 0;
        var mejorLongitud = int.MaxValue;

        foreach (var elemento in elementos)
        {
            var nombreNormalizado = Normalizar(nombre(elemento));
            if (nombreNormalizado.Length == 0)
                continue;

            int puntaje;
            if (mensaje.Contains(" " + nombreNormalizado + " "))
            {
                puntaje = 1000 + nombreNormalizado.Length;
            }
            else
            {
                var distintivas = Tokens(nombreNormalizado)
                    .Where(t => t.Length >= 3 && !PalabrasVacias.Contains(t) && !PalabrasGenericasLugar.Contains(t))
                    .Distinct()
                    .ToList();

                if (distintivas.Count == 0)
                    continue;

                var coincidencias = distintivas.Count(palabrasMensaje.Contains);
                if (coincidencias == 0 || coincidencias * 2 < distintivas.Count)
                    continue;

                puntaje = coincidencias * 100 + (coincidencias == distintivas.Count ? 50 : 0);
            }

            // A igual puntaje se prefiere el nombre más corto (más específico)
            if (puntaje > mejorPuntaje || (puntaje == mejorPuntaje && nombreNormalizado.Length < mejorLongitud))
            {
                mejor = elemento;
                mejorPuntaje = puntaje;
                mejorLongitud = nombreNormalizado.Length;
            }
        }

        return mejor;
    }

    // "el primero", "la segunda", "el 3", "numero 2", "el ultimo" -> índice (base 0)
    public static int? ObtenerOrdinal(string normalizado, int total)
    {
        if (total == 0)
            return null;

        var tokens = Tokens(normalizado);

        if (tokens.Any(t => t is "ultimo" or "ultima"))
            return total - 1;

        foreach (var token in tokens)
        {
            if (Ordinales.TryGetValue(token, out var indice))
                return indice < total ? indice : null;
        }

        var numero = NumeroReferido().Match(normalizado);
        if (numero.Success || (tokens.Length == 1 && int.TryParse(tokens[0], out _)))
        {
            var valor = int.Parse(numero.Success ? numero.Groups[1].Value : tokens[0]);
            return valor >= 1 && valor <= total ? valor - 1 : null;
        }

        return null;
    }

    // Mensajes que solo señalan un elemento de la lista anterior: "2", "el segundo", "la ultima"
    public static bool EsSoloReferencia(string normalizado)
    {
        var relleno = new HashSet<string> { "el", "la", "numero", "opcion", "nro", "y", "de", "del", "quiero", "dame", "ver", "mismo", "misma" };
        var tokens = Tokens(normalizado).Where(t => !relleno.Contains(t)).ToList();

        return tokens.Count is > 0 and <= 2
               && tokens.All(t => Ordinales.ContainsKey(t) || t is "ultimo" or "ultima" || int.TryParse(t, out _));
    }

    public static bool TieneReferencia(string normalizado) =>
        Tokens(normalizado).Any(PalabrasReferencia.Contains);

    public static bool PideCercania(string normalizado) =>
        Tokens(normalizado).Any(t => t.StartsWith("cerca") || t.StartsWith("proxim") || t == "alrededor" || t == "alrededores");

    public static bool PideMasBarato(string normalizado) =>
        normalizado.Contains("barat") || normalizado.Contains("economic") || normalizado.Contains("menor precio")
        || normalizado.Contains("precio mas bajo") || normalizado.Contains("mas bajo") || normalizado.Contains("menos cuesta")
        || normalizado.Contains("mas accesible");

    public static bool PideMasCaro(string normalizado) =>
        normalizado.Contains("mas caro") || normalizado.Contains("mayor precio") || normalizado.Contains("precio mas alto")
        || normalizado.Contains("mas costoso") || normalizado.Contains("de lujo");

    public static bool PideDomicilio(string normalizado) =>
        normalizado.Contains("domicilio") || normalizado.Contains("delivery");

    public static bool PideDestacados(string normalizado) =>
        normalizado.Contains("destacad") || normalizado.Contains("principales") || normalizado.Contains("mas bonit")
        || normalizado.Contains("mejores");

    public static string Recortar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        texto = texto.Trim();
        if (texto.Length <= maximo)
            return texto;

        var corte = texto.LastIndexOf(' ', maximo);
        return texto[..(corte > maximo / 2 ? corte : maximo)] + "…";
    }
}
