namespace AppDonnyCuevas20210074.Helpers;

// Guarda en wwwroot/uploads las fotos que se suben desde el panel y devuelve su ruta pública
// ("/uploads/prestadores/abc.jpg"), que es la que se guarda en Imagenes.UrlImagen.
public class AlmacenImagenes
{
    public const long TamanoMaximoBytes = 8 * 1024 * 1024;           // 8 MB por foto
    public const long TamanoMaximoSolicitud = 120L * 1024 * 1024;    // varias fotos a la vez

    private static readonly Dictionary<string, string[]> TiposPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = new[] { "image/jpeg", "image/pjpeg" },
        [".jpeg"] = new[] { "image/jpeg", "image/pjpeg" },
        [".png"] = new[] { "image/png" },
        [".webp"] = new[] { "image/webp" },
        [".gif"] = new[] { "image/gif" }
    };

    public const string ExtensionesAceptadas = ".jpg,.jpeg,.png,.webp,.gif";

    private const string CarpetaPublica = "uploads";

    private readonly IWebHostEnvironment _entorno;

    public AlmacenImagenes(IWebHostEnvironment entorno)
    {
        _entorno = entorno;
    }

    // Devuelve el mensaje de error si el archivo no es una imagen válida; null si está bien
    public static string? Validar(IFormFile archivo)
    {
        if (archivo.Length == 0)
            return $"El archivo «{archivo.FileName}» está vacío.";

        if (archivo.Length > TamanoMaximoBytes)
            return $"La foto «{archivo.FileName}» pesa más de {TamanoMaximoBytes / (1024 * 1024)} MB.";

        var extension = Path.GetExtension(archivo.FileName);
        if (!TiposPermitidos.TryGetValue(extension, out var tipos) || !tipos.Contains(archivo.ContentType, StringComparer.OrdinalIgnoreCase))
            return $"«{archivo.FileName}» no es una imagen válida. Use JPG, PNG, WEBP o GIF.";

        return null;
    }

    // carpeta: "lugares" o "prestadores"
    public async Task<string> GuardarAsync(IFormFile archivo, string carpeta)
    {
        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        var destino = Path.Combine(_entorno.WebRootPath, CarpetaPublica, carpeta);
        Directory.CreateDirectory(destino);

        var nombre = $"{Guid.NewGuid():N}{extension}";
        await using (var flujo = File.Create(Path.Combine(destino, nombre)))
        {
            await archivo.CopyToAsync(flujo);
        }

        return $"/{CarpetaPublica}/{carpeta}/{nombre}";
    }

    // Valida y guarda varias fotos a la vez; devuelve los registros Imagen listos para agregar
    // (sin propietario asignado) y los errores de las fotos que no se pudieron aceptar.
    // Si ninguna es principal todavía, la primera nueva queda como principal.
    public async Task<(List<Models.Imagen> Imagenes, List<string> Errores)> CrearImagenesAsync(
        IEnumerable<IFormFile>? fotos, string carpeta, string textoAlternativo, bool yaHayPrincipal, int ordenInicial)
    {
        var imagenes = new List<Models.Imagen>();
        var errores = new List<string>();
        var orden = ordenInicial;

        foreach (var foto in fotos ?? Enumerable.Empty<IFormFile>())
        {
            var error = Validar(foto);
            if (error != null)
            {
                errores.Add(error);
                continue;
            }

            imagenes.Add(new Models.Imagen
            {
                UrlImagen = await GuardarAsync(foto, carpeta),
                TextoAlternativo = textoAlternativo,
                EsPrincipal = !yaHayPrincipal && imagenes.Count == 0,
                OrdenVisualizacion = orden++,
                FechaCreacion = DateTime.Now
            });
        }

        return (imagenes, errores);
    }

    // Borra el archivo físico si la imagen se subió al servidor (las URL externas no se tocan)
    public void Eliminar(string? urlImagen)
    {
        if (string.IsNullOrWhiteSpace(urlImagen) || !urlImagen.StartsWith($"/{CarpetaPublica}/", StringComparison.OrdinalIgnoreCase))
            return;

        var raiz = Path.GetFullPath(Path.Combine(_entorno.WebRootPath, CarpetaPublica));
        var ruta = Path.GetFullPath(Path.Combine(_entorno.WebRootPath, urlImagen.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

        // Evita borrar algo fuera de wwwroot/uploads
        if (!ruta.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (File.Exists(ruta))
                File.Delete(ruta);
        }
        catch (IOException)
        {
            // Si el archivo está en uso no se interrumpe el borrado del registro
        }
    }
}
