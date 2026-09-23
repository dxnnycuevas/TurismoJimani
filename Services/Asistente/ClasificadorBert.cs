using System.Text.Json;

namespace AppDonnyCuevas20210074.Services.Asistente;

public record ResultadoIntencion(string Intencion, double Confianza);

public interface IClasificadorIntenciones
{
    Task<ResultadoIntencion> ClasificarAsync(string texto, CancellationToken ct = default);
    Task<bool> EstaDisponibleAsync(CancellationToken ct = default);
}

// Error al comunicarse con el servicio BERT (caído, lento o respuesta no válida)
public class ServicioIaException : Exception
{
    public bool EsTimeout { get; }

    public ServicioIaException(string mensaje, bool esTimeout, Exception? interna = null)
        : base(mensaje, interna)
    {
        EsTimeout = esTimeout;
    }
}

// Cliente HTTP del servicio Python (ServicioBert/servicio.py) que ejecuta BERT
public class ClasificadorBert : IClasificadorIntenciones
{
    private readonly HttpClient _http;

    public ClasificadorBert(HttpClient http)
    {
        _http = http;
    }

    public async Task<ResultadoIntencion> ClasificarAsync(string texto, CancellationToken ct = default)
    {
        try
        {
            using var respuesta = await _http.PostAsJsonAsync("predecir", new { texto }, ct);

            if (!respuesta.IsSuccessStatusCode)
                throw new ServicioIaException($"El servicio BERT respondió con el código {(int)respuesta.StatusCode}.", esTimeout: false);

            var prediccion = await respuesta.Content.ReadFromJsonAsync<PrediccionBert>(ct);

            if (prediccion is null || string.IsNullOrWhiteSpace(prediccion.Intencion))
                throw new ServicioIaException("El servicio BERT devolvió una respuesta vacía.", esTimeout: false);

            return new ResultadoIntencion(prediccion.Intencion, prediccion.Confianza);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ServicioIaException("El servicio BERT no respondió a tiempo.", esTimeout: true, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ServicioIaException("No se pudo conectar con el servicio BERT.", esTimeout: false, ex);
        }
        catch (JsonException ex)
        {
            throw new ServicioIaException("El servicio BERT devolvió una respuesta no válida.", esTimeout: false, ex);
        }
    }

    public async Task<bool> EstaDisponibleAsync(CancellationToken ct = default)
    {
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(3));
            using var respuesta = await _http.GetAsync("salud", limite.Token);
            return respuesta.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private sealed record PrediccionBert(string Intencion, double Confianza);
}
