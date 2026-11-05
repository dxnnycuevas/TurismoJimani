using Microsoft.Extensions.Caching.Memory;

namespace AppDonnyCuevas20210074.Services.Seguridad;

// Bloqueo temporal por intentos fallidos de inicio de sesión (S-02).
// Tras N contraseñas incorrectas seguidas para el mismo correo (Seguridad:IntentosFallidosMaximos, 5 por defecto),
// la cuenta queda bloqueada M minutos (Seguridad:MinutosBloqueo, 15 por defecto).
// El conteo se hace por correo exista o no la cuenta, para no revelar qué correos están registrados.
// Se complementa con el límite de peticiones por IP "acceso" definido en Program.cs.
public class ProteccionAcceso
{
    private readonly IMemoryCache _cache;
    private readonly int _intentosMaximos;
    private readonly TimeSpan _duracionBloqueo;
    private readonly object _candado = new();

    public ProteccionAcceso(IMemoryCache cache, IConfiguration configuracion)
    {
        _cache = cache;
        _intentosMaximos = Math.Max(1, configuracion.GetValue("Seguridad:IntentosFallidosMaximos", 5));
        _duracionBloqueo = TimeSpan.FromMinutes(Math.Max(1, configuracion.GetValue("Seguridad:MinutosBloqueo", 15)));
    }

    public int MinutosBloqueo => (int)_duracionBloqueo.TotalMinutes;

    // true si el correo está bloqueado; minutosRestantes indica cuánto falta
    public bool EstaBloqueado(string correo, out int minutosRestantes)
    {
        minutosRestantes = 0;
        if (_cache.TryGetValue(Clave(correo), out Registro? registro) &&
            registro?.BloqueadoHasta is DateTimeOffset hasta && hasta > DateTimeOffset.UtcNow)
        {
            minutosRestantes = Math.Max(1, (int)Math.Ceiling((hasta - DateTimeOffset.UtcNow).TotalMinutes));
            return true;
        }
        return false;
    }

    public void RegistrarFallo(string correo)
    {
        lock (_candado)
        {
            var clave = Clave(correo);
            var registro = _cache.Get<Registro>(clave) ?? new Registro();

            registro.Fallos++;
            if (registro.Fallos >= _intentosMaximos)
            {
                registro.BloqueadoHasta = DateTimeOffset.UtcNow + _duracionBloqueo;
                registro.Fallos = 0;
            }

            // El registro se olvida si pasa el tiempo de bloqueo sin nuevos fallos
            _cache.Set(clave, registro, _duracionBloqueo);
        }
    }

    // Tras un inicio de sesión correcto se reinicia el conteo
    public void Reiniciar(string correo) => _cache.Remove(Clave(correo));

    private static string Clave(string correo) => "acceso:" + (correo ?? "").Trim().ToLowerInvariant();

    private sealed class Registro
    {
        public int Fallos;
        public DateTimeOffset? BloqueadoHasta;
    }
}
