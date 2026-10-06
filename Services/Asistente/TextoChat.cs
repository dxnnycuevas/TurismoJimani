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
        "republica", "dominicana", "provincia", "independencia", "colmado", "tienda", "farmacia", "barberia",
        "salon", "taller", "agencia", "cabana", "cabanas", "casa", "huespedes"
    };

    // Palabras de la pregunta que no sirven para filtrar por tipo o categoría
    private static readonly HashSet<string> PalabrasDeConsulta = new()
    {
        "lugar", "lugares", "atractivo", "atractivos", "turistico", "turisticos", "turistica", "turisticas",
        "visitar", "conocer", "sitio", "sitios", "ver", "ir", "recomiendas", "recomiendame", "busco", "buscar",
        "jimani", "hotel", "hoteles", "restaurante", "restaurantes", "comer", "transporte", "tienen", "registrados",
        "cerca", "cercanos", "cercanas", "muestrame", "dame", "lista", "opciones", "quisiera", "necesito",
        "servicio", "servicios", "alguien", "persona", "personas", "contratar", "negocio", "negocios",
        "disponible", "disponibles", "ofrece", "ofrecen", "pueblo", "aqui", "zona", "local", "locales",
        "prestador", "prestadores", "transportista", "transportistas", "llevar", "lleve", "lleva", "viaje", "viajes"
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

    // Compara dos palabras por su raíz: "naturales" ~ "natural", "rios" ~ "rio".
    // En palabras largas también acepta un error de escritura: "berbero" ~ "barbero" ~ "barberia".
    public static bool CoincideRaiz(string a, string b)
    {
        if (a.Length <= 4 || b.Length <= 4)
            return a == b || a + "s" == b || b + "s" == a || a + "es" == b || b + "es" == a;

        if (string.CompareOrdinal(a, 0, b, 0, 5) == 0 || DistanciaEdicion(a, b, 1) <= 1)
            return true;

        // Raíz de 6 letras con una letra distinta (solo palabras de 6 o más para evitar "playa" ~ "plaza")
        return a.Length >= 6 && b.Length >= 6 && DistanciaEdicion(a[..6], b[..6], 1) <= 1;
    }

    // Distancia de Levenshtein (letras cambiadas, sobrantes o faltantes); se detiene al pasar el máximo
    private static int DistanciaEdicion(string a, string b, int maximo)
    {
        if (Math.Abs(a.Length - b.Length) > maximo) return maximo + 1;

        var anterior = new int[b.Length + 1];
        var actual = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) anterior[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            actual[0] = i;
            var minimoFila = actual[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var costo = a[i - 1] == b[j - 1] ? 0 : 1;
                actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, anterior[j] + 1), anterior[j - 1] + costo);
                minimoFila = Math.Min(minimoFila, actual[j]);
            }
            if (minimoFila > maximo) return maximo + 1;
            (anterior, actual) = (actual, anterior);
        }

        return anterior[b.Length];
    }

    // Busca en el mensaje el nombre de un registro de la base de datos (lugar o ruta).
    // Prioridad: nombre completo > todas sus palabras distintivas > al menos la mitad de ellas.
    public static T? BuscarPorNombre<T>(string mensajeNormalizado, IEnumerable<T> elementos, Func<T, string> nombre)
        where T : class =>
        BuscarConPuntaje(mensajeNormalizado, elementos, nombre).Elemento;

    // Igual que BuscarPorNombre, pero devuelve también el puntaje (0 si no se nombra ninguno)
    // para comparar fichas, rutas y eventos que comparten palabras ("Isla Cabritos" y "Excursión a Isla Cabritos")
    public static (T? Elemento, int Puntaje) BuscarConPuntaje<T>(string mensajeNormalizado, IEnumerable<T> elementos, Func<T, string> nombre)
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

        return (mejor, mejorPuntaje);
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

    // "¿Dónde queda el lago?", "¿dónde está…?", "ubicación de…": piden el lugar, no una ruta
    public static bool PideUbicacion(string normalizado) =>
        normalizado.Contains("donde queda") || normalizado.Contains("donde esta") || normalizado.Contains("donde se encuentra")
        || normalizado.Contains("ubicacion") || normalizado.Contains("ubicad") || normalizado.Contains("direccion");

    // "¿Cómo llego a…?": rutas que terminan en el lugar
    public static bool PideComoLlegar(string normalizado) =>
        normalizado.Contains("como llego") || normalizado.Contains("como llegar") || normalizado.Contains("como voy")
        || normalizado.Contains("como ir") || normalizado.Contains("llegar a") || normalizado.Contains("camino a");

    // "¿Qué rutas salen del parque?": rutas que empiezan en el lugar
    public static bool PideSalida(string normalizado) =>
        Tokens(normalizado).Any(t => t is "desde" or "salen" or "sale" or "salir" or "parten" or "empiezan" or "inician");

    // ---------- Eventos y datos curiosos ----------

    // Palabras que piden eventos aunque BERT no lo detecte: "¿qué eventos hay?", "¿hay alguna feria?", "promociones"
    private static readonly string[] RaicesEvento =
        { "evento", "feria", "festival", "fiesta", "concierto", "promocion", "descuento", "oferta", "carrera", "maraton", "patronal", "celebr" };

    public static bool PideEventos(string normalizado) =>
        Tokens(normalizado).Any(t => RaicesEvento.Any(t.StartsWith));

    // Tipo de evento que nombra el mensaje (los mismos de Evento.Tipos)
    private static readonly (string Raiz, string Tipo)[] TiposEvento =
    {
        ("promocion", "Promoción"), ("descuento", "Promoción"), ("oferta", "Promoción"),
        ("patronal", "Fiesta patronal"), ("festival", "Festival"), ("cultural", "Actividad cultural"),
        ("religios", "Religioso"), ("procesion", "Religioso"), ("deport", "Deportivo"), ("carrera", "Deportivo"),
        ("maraton", "Deportivo"), ("gastronom", "Gastronómico"), ("comida", "Gastronómico")
    };

    public static string? TipoEventoPedido(string normalizado)
    {
        var tokens = Tokens(normalizado);
        return TiposEvento.FirstOrDefault(x => tokens.Any(t => t.StartsWith(x.Raiz))).Tipo;
    }

    private static readonly string[] Meses =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    };

    // Periodo que pide el mensaje: "hoy", "este fin de semana", "esta semana", "este mes", "el mes que viene", "en noviembre"
    public static (DateTime Desde, DateTime Hasta, string Texto)? PeriodoPedido(string normalizado, DateTime hoy)
    {
        hoy = hoy.Date;
        var tokens = Tokens(normalizado);

        if (tokens.Contains("hoy"))
            return (hoy, hoy, "hoy");

        if (normalizado.Contains("fin de semana"))
        {
            // Sábado y domingo de esta semana (si ya es domingo, solo hoy)
            var sabado = hoy.AddDays(((int)DayOfWeek.Saturday - (int)hoy.DayOfWeek + 7) % 7);
            return hoy.DayOfWeek == DayOfWeek.Sunday ? (hoy, hoy, "este fin de semana") : (sabado, sabado.AddDays(1), "este fin de semana");
        }

        if (normalizado.Contains("esta semana"))
            return (hoy, hoy.AddDays((7 - (int)hoy.DayOfWeek) % 7), "esta semana");

        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        if (normalizado.Contains("este mes"))
            return (hoy, inicioMes.AddMonths(1).AddDays(-1), "este mes");
        if (normalizado.Contains("proximo mes") || normalizado.Contains("mes que viene") || normalizado.Contains("siguiente mes"))
            return (inicioMes.AddMonths(1), inicioMes.AddMonths(2).AddDays(-1), "el mes que viene");

        for (var i = 0; i < Meses.Length; i++)
        {
            if (!tokens.Contains(Meses[i]) && !(i == 8 && tokens.Contains("setiembre")))
                continue;

            // Un mes que ya pasó este año se refiere al del año que viene
            var inicio = new DateTime(hoy.Year, i + 1, 1);
            if (inicio.AddMonths(1) <= hoy)
                inicio = inicio.AddYears(1);
            return (inicio < hoy ? hoy : inicio, inicio.AddMonths(1).AddDays(-1), "en " + Meses[i]);
        }

        return null;
    }

    // "Cuéntame un dato curioso", "¿sabías que…?", "otra curiosidad"
    public static bool PideDatoCurioso(string normalizado) =>
        normalizado.Contains("dato curioso") || normalizado.Contains("datos curiosos") || normalizado.Contains("curiosidad")
        || normalizado.Contains("sabias que") || normalizado.Contains("algo interesante") || normalizado.Contains("dato interesante")
        || normalizado.Contains("otro dato");

    // Palabras de la pregunta que no dicen de qué tema se quiere el dato
    private static readonly HashSet<string> PalabrasDeDato = new()
    {
        "dato", "datos", "curioso", "curiosos", "curiosa", "curiosidad", "curiosidades", "cuentame", "dime", "hablame",
        "sabias", "interesante", "interesantes", "otro", "otra", "saber", "sobre", "acerca", "informacion", "porque",
        "cuando", "quien", "tiene", "tienen", "puedes"
    };

    // Tema de la pregunta para elegir un dato: "dato curioso del lago" -> ["lago"]
    public static List<string> PalabrasTema(string normalizado) =>
        PalabrasClave(normalizado).Where(t => !PalabrasDeDato.Contains(t)).ToList();

    // Solo las palabras que distinguen el tema ("¿qué es el larimar?" -> ["larimar"]), sin las de cualquier consulta
    public static List<string> PalabrasDistintivas(string normalizado) =>
        PalabrasFiltro(normalizado).Where(t => !PalabrasDeDato.Contains(t)).ToList();

    // Cuántas palabras del tema aparecen, por su raíz, en un texto: "salado" ~ "saladas", "tainos" ~ "taino"
    public static int Coincidencias(IReadOnlyCollection<string> palabras, string texto)
    {
        var tokens = Tokens(Normalizar(texto)).Where(t => t.Length >= 3).Distinct().ToList();
        return palabras.Count(p => tokens.Any(t => CoincideRaiz(p, t)));
    }

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

    // ---------- Planificador de viaje ----------

    private static readonly Dictionary<string, int> NumerosEnLetras = new()
    {
        ["un"] = 1, ["una"] = 1, ["uno"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5,
        ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10, ["once"] = 11,
        ["doce"] = 12, ["quince"] = 15, ["veinte"] = 20, ["treinta"] = 30, ["cuarenta"] = 40,
        ["cincuenta"] = 50, ["cien"] = 100
    };

    private const string Numeros = "un|una|uno|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|quince|veinte|treinta|cuarenta|cincuenta|cien";

    // "5 días", "tres noches", "una semana" (con límites de palabra: "algún día" no cuenta)
    [GeneratedRegex(@"\b(\d{1,3}|" + Numeros + @")\s*(dias?|noches?|semanas?)\b")]
    private static partial Regex Duracion();

    // "10,000", "10000", "1.5 mil", "10 mil", "8k" (el grupo 2 son decimales; el 3, la unidad)
    [GeneratedRegex(@"(?<!\d|\d[.,])(\d{1,3}(?:[.,]\d{3})+|\d+)(?:[.,](\d{1,2}))?(?!\d|[.,]\d)\s*(mil\b|k\b|lucas?\b)?")]
    private static partial Regex Cantidad();

    // "diez mil pesos", "quince mil"
    [GeneratedRegex(@"\b(" + Numeros + @")\s+mil\b")]
    private static partial Regex CantidadEnLetras();

    // Minúsculas y sin tildes, pero conservando cifras, comas, puntos y signos ("RD$ 10,000" -> "rd$ 10,000")
    private static string SinTildes(string texto)
    {
        var descompuesto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(descompuesto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    // Lee del mensaje el dinero disponible y la duración del viaje (se toma 1 día = 1 noche):
    // "tengo 10000 pesos para pasar 5 días" -> (10000, 5); "diez mil para una semana" -> (10000, 7)
    public static (decimal? Monto, int? Noches) DatosDeViaje(string mensaje)
    {
        var texto = SinTildes(mensaje);

        int? noches = null;
        var duracion = Duracion().Match(texto);
        if (duracion.Success)
        {
            var valor = int.TryParse(duracion.Groups[1].Value, out var n) ? n : NumerosEnLetras[duracion.Groups[1].Value];
            if (duracion.Groups[2].Value.StartsWith("semana")) valor *= 7;
            if (valor is >= 1 and <= 60) noches = valor;
        }
        else if (texto.Contains("fin de semana"))
        {
            noches = 2;
        }

        decimal? monto = null;
        void Considerar(decimal valor)
        {
            if (valor >= 100 && (monto is null || valor > monto)) monto = valor;
        }

        foreach (Match m in Cantidad().Matches(texto))
        {
            // El número de la duración no es dinero
            if (duracion.Success && m.Index >= duracion.Index && m.Index < duracion.Index + duracion.Length)
                continue;

            var entero = Regex.Replace(m.Groups[1].Value, "[.,]", "");
            if (!decimal.TryParse(entero, NumberStyles.None, CultureInfo.InvariantCulture, out var valor))
                continue;

            if (m.Groups[3].Success)
            {
                // "1.5 mil" -> 1500
                if (m.Groups[2].Success)
                    valor += decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / (decimal)Math.Pow(10, m.Groups[2].Value.Length);
                valor *= 1000;
            }
            else if (EsUnAnio(texto, m, valor))
            {
                continue;   // "eventos en 2026" no es dinero
            }

            Considerar(valor);
        }

        foreach (Match m in CantidadEnLetras().Matches(texto))
            Considerar(NumerosEnLetras[m.Groups[1].Value] * 1000m);

        return (monto, noches);
    }

    // Un número de 4 cifras entre 1900 y 2100, sin separadores ni "pesos"/"RD$" cerca, se toma como año
    private static bool EsUnAnio(string texto, Match m, decimal valor)
    {
        if (valor < 1900 || valor > 2100 || m.Groups[1].Value.Length != 4 || m.Groups[2].Success)
            return false;

        var antes = texto[Math.Max(0, m.Index - 4)..m.Index];
        var despues = texto[(m.Index + m.Length)..].TrimStart();
        return !antes.Contains('$') && !antes.Contains("rd") && !despues.StartsWith("peso") && !despues.StartsWith("rd");
    }

    // "Tengo 10 mil pesos para 5 días", "¿qué me alcanza con 8000?". Un monto solo no basta:
    // "hoteles de menos de 2000 pesos" es una búsqueda de hoteles, no un plan.
    public static bool EsPresupuesto(string mensaje)
    {
        var (monto, noches) = DatosDeViaje(mensaje);
        if (monto is null) return false;

        var texto = Normalizar(mensaje);
        return noches is not null || texto.Contains("presupuesto") || texto.Contains("gastar") || texto.Contains("alcanza")
               || texto.Contains("planific") || Tokens(texto).Contains("tengo") || Tokens(texto).Contains("viaje");
    }

    public static bool PideMenosNoches(string normalizado) =>
        normalizado.Contains("menos noche") || normalizado.Contains("menos dia") || normalizado.Contains("menos tiempo")
        || normalizado.Contains("reducir") || normalizado.Contains("acortar");

    // Respuesta a "¿desde dónde viajas?": "salgo de Higüey", "vengo desde La Romana" o solo el nombre ("Higüey")
    public static bool PareceUnOrigen(string mensaje, string normalizado)
    {
        var tokens = Tokens(normalizado);
        if (tokens.Any(t => t is "salgo" or "sale" or "desde" or "vengo" or "vivo" or "voy") || normalizado.Contains("soy de"))
            return true;

        return tokens.Length is > 0 and <= 3 && !mensaje.Contains('?');
    }

    // "Ya estoy en Jimaní", "sin pasaje", "voy en mi carro": el presupuesto se calcula sin pasaje
    public static bool SinPasaje(string normalizado) =>
        normalizado.Contains("sin pasaje") || normalizado.Contains("estoy en jimani") || normalizado.Contains("vivo en jimani")
        || normalizado.Contains("ya estoy") || normalizado.Contains("carro propio") || normalizado.Contains("mi carro")
        || normalizado.Contains("mi vehiculo") || normalizado.Contains("no necesito pasaje");
}
