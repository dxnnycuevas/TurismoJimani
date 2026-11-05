using System.Security.Cryptography;
using System.Text;
using AppDonnyCuevas20210074.Helpers;

namespace AppDonnyCuevas20210074.Services.Seguridad;

// Valida una contraseña nueva (registro, alta de usuarios y cambio de contraseña) - S-01:
// 1) la política local (PoliticaContrasena);
// 2) que no aparezca en filtraciones conocidas, con el servicio Pwned Passwords de Have I Been Pwned.
//    Se usa k-anonimato: solo se envían los 5 primeros caracteres del hash SHA-1 y la comparación se hace
//    aquí, de modo que la contraseña ni su hash completo salen del servidor.
// Si el servicio externo no responde, se aplica solo la política local (no impide registrarse).
public class ValidadorContrasenas
{
    private readonly HttpClient _http;
    private readonly ILogger<ValidadorContrasenas> _logger;
    private readonly bool _consultarFiltradas;

    public ValidadorContrasenas(HttpClient http, IConfiguration configuracion, ILogger<ValidadorContrasenas> logger)
    {
        _http = http;
        _logger = logger;
        _consultarFiltradas = configuracion.GetValue("Seguridad:ConsultarContrasenasFiltradas", true);
    }

    public async Task<List<string>> ValidarAsync(string? contrasena, string? correo, CancellationToken cancelacion = default)
    {
        var errores = PoliticaContrasena.Validar(contrasena, correo);

        if (errores.Count == 0 && await EstaFiltradaAsync(contrasena!, cancelacion))
            errores.Add("Esta contraseña aparece en filtraciones de datos conocidas. Elija otra.");

        return errores;
    }

    // true si la contraseña aparece en la base de Pwned Passwords; false si no aparece o si no hubo respuesta
    public async Task<bool> EstaFiltradaAsync(string contrasena, CancellationToken cancelacion = default)
    {
        if (!_consultarFiltradas) return false;

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(contrasena)));
        var prefijo = hash[..5];
        var sufijo = hash[5..];

        try
        {
            using var respuesta = await _http.GetAsync($"range/{prefijo}", cancelacion);
            if (!respuesta.IsSuccessStatusCode)
            {
                _logger.LogWarning("Pwned Passwords respondió {Estado}; se aplica solo la política local.", (int)respuesta.StatusCode);
                return false;
            }

            var cuerpo = await respuesta.Content.ReadAsStringAsync(cancelacion);
            foreach (var linea in cuerpo.Split('\n'))
            {
                var partes = linea.Trim().Split(':');
                // Con el encabezado Add-Padding la respuesta incluye líneas de relleno con conteo 0
                if (partes.Length == 2 &&
                    partes[0].Equals(sufijo, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(partes[1], out var veces) && veces > 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("No se pudo consultar Pwned Passwords ({Mensaje}); se aplica solo la política local.", ex.Message);
            return false;
        }
    }
}
