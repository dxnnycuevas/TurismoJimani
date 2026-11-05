using System.Globalization;
using System.Text;

namespace AppDonnyCuevas20210074.Helpers;

// Política de contraseñas de la plataforma (S-01). Sigue NIST SP 800-63B-4 (sección 3.1.1.2):
// - como la contraseña es el único factor de autenticación, la longitud mínima es de 15 caracteres;
//   la máxima es de 128 (NIST pide admitir al menos 64) y se aceptan frases y cualquier carácter;
// - no se exigen combinaciones obligatorias de mayúsculas, números o símbolos;
// - la contraseña completa (y su forma base, sin números ni símbolos) se compara con una lista de
//   contraseñas comunes o esperables, incluido el correo del usuario.
// La comprobación contra contraseñas filtradas se hace en ValidadorContrasenas.
public static class PoliticaContrasena
{
    public const int LongitudMinima = 15;
    public const int LongitudMaxima = 128;

    public static string TextoAyuda =>
        $"Use al menos {LongitudMinima} caracteres. Puede ser una frase fácil de recordar; " +
        "no se aceptan contraseñas comunes ni filtradas.";

    // Errores de la política local (sin consultar servicios externos). Lista vacía si cumple.
    public static List<string> Validar(string? contrasena, string? correo = null)
    {
        var errores = new List<string>();

        if (string.IsNullOrEmpty(contrasena))
        {
            errores.Add("La contraseña es obligatoria.");
            return errores;
        }

        if (contrasena.Length < LongitudMinima)
            errores.Add($"La contraseña debe tener al menos {LongitudMinima} caracteres.");

        if (contrasena.Length > LongitudMaxima)
            errores.Add($"La contraseña no puede tener más de {LongitudMaxima} caracteres.");

        if (errores.Count > 0)
            return errores;

        if (EsComun(contrasena) || EsRepetitiva(contrasena))
            errores.Add("Es una contraseña común o fácil de adivinar. Elija otra.");
        else if (EsElCorreo(contrasena, correo))
            errores.Add("La contraseña no puede ser su correo electrónico ni una variante de él.");

        return errores;
    }

    public static bool Cumple(string? contrasena, string? correo = null) => Validar(contrasena, correo).Count == 0;

    // Común si coincide con la lista, o si sus letras forman una palabra de la lista (o esa palabra repetida)
    // acompañada solo de números y símbolos ("Admin123*Admin1", "Jimani2026Jimani2026").
    private static bool EsComun(string contrasena)
    {
        var minusculas = SinTildes(contrasena.Trim().ToLowerInvariant());
        if (Comunes.Contains(minusculas))
            return true;

        var letras = new string(minusculas.Where(char.IsLetter).ToArray());
        return letras.Length > 0 && (Comunes.Contains(letras) || Comunes.Contains(Periodo(letras)));
    }

    // Palabra que se repite para formar el texto ("jimanijimani" -> "jimani"); el mismo texto si no se repite
    private static string Periodo(string texto)
    {
        for (var k = 1; k <= texto.Length / 2; k++)
        {
            if (texto.Length % k == 0 && string.Concat(Enumerable.Repeat(texto[..k], texto.Length / k)) == texto)
                return texto[..k];
        }
        return texto;
    }

    // Pocos caracteres distintos ("aaaaaaaaaaaa", "121212121212") o una secuencia ("abcdefghijkl", "123456789012")
    private static bool EsRepetitiva(string contrasena)
    {
        if (contrasena.Distinct().Count() < 4)
            return true;

        var paso = contrasena[1] - contrasena[0];
        if (Math.Abs(paso) <= 1)
        {
            var esSecuencia = true;
            for (var i = 2; i < contrasena.Length && esSecuencia; i++)
            {
                var diferencia = contrasena[i] - contrasena[i - 1];
                // Se permite el salto 9→0 de los teclados ("1234567890")
                esSecuencia = diferencia == paso || (contrasena[i - 1] == '9' && contrasena[i] == '0');
            }
            if (esSecuencia) return true;
        }

        return false;
    }

    // La contraseña (o su forma base) es el correo o la parte anterior a la arroba ("juanperez2026!!")
    private static bool EsElCorreo(string contrasena, string? correo)
    {
        if (string.IsNullOrWhiteSpace(correo)) return false;

        var minusculas = contrasena.Trim().ToLowerInvariant();
        var correoMinusculas = correo.Trim().ToLowerInvariant();
        var usuario = correoMinusculas.Split('@')[0];

        static string Letras(string texto) => new(texto.Where(char.IsLetter).ToArray());

        return minusculas == correoMinusculas ||
               minusculas == usuario ||
               (Letras(usuario).Length >= 3 && Letras(minusculas) == Letras(usuario));
    }

    private static string SinTildes(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Contraseñas y palabras base muy usadas (en inglés y en español) y términos del propio sitio.
    // Las letras se comparan sin tildes y en minúsculas.
    private static readonly HashSet<string> Comunes = new(StringComparer.Ordinal)
    {
        // Genéricas
        "password", "passw", "passwd", "pass", "contrasena", "contrasenas", "clave", "claves", "secreto", "secret",
        "admin", "administrator", "administrador", "administracion", "root", "toor", "user", "usuario", "usuarios",
        "login", "acceso", "entrar", "welcome", "bienvenido", "bienvenida", "letmein", "changeme", "cambiame",
        "default", "test", "testing", "prueba", "pruebas", "demo", "guest", "invitado", "temporal", "nueva",
        "qwerty", "qwertyuiop", "asdfgh", "asdfghjkl", "zxcvbn", "zxcvbnm", "azerty", "qazwsx", "qwe", "asd",
        "abc", "abcd", "abcdef", "abcdefg", "abcdefgh", "iloveyou", "teamo", "tequiero", "amor", "miamor",
        "monkey", "dragon", "football", "futbol", "baseball", "beisbol", "basketball", "baloncesto", "soccer",
        "princess", "princesa", "sunshine", "master", "superman", "batman", "spiderman", "pokemon", "starwars",
        "shadow", "whatever", "trustno", "freedom", "libertad", "michael", "jordan", "jesus", "dios", "diosesamor",
        "familia", "family", "flower", "flores", "hello", "hola", "holamundo", "helloworld", "computer",
        "computadora", "internet", "samsung", "iphone", "google", "facebook", "instagram", "whatsapp", "microsoft",
        "windows", "linux", "android", "chocolate", "cookie", "banana", "manzana", "mariposa", "estrella",
        // Contraseñas completas frecuentes
        "123456789012345", "1234567890123456", "123456123456123", "111111111111111", "000000000000000",
        "123123123123123", "987654321098765", "1q2w3e4r5t6y7u8", "1qaz2wsx3edc4rf", "qwertyuiopasdfg",
        "qwertyuiopasdfghjkl", "asdfghjklzxcvbnm", "qazwsxedcrfvtgb", "iloveyouiloveyou",
        // Del propio sitio y de la región
        "jimani", "jimanitur", "turismo", "turismojimani", "jimaniturismo", "turismojimanisitio", "independencia",
        "provinciaindependencia", "barahona", "ucateba", "republicadominicana", "dominicana", "dominicano",
        "santodomingo", "quisqueya", "lagoenriquillo", "enriquillo", "ellimon", "bocadecachon",
        "larimar", "taino", "tainos", "merengue", "bachata"
    };
}
