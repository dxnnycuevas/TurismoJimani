namespace AppDonnyCuevas20210074.Models.Asistente;

// Datos que envía el chat: { "mensaje": "...", "idSesion": "..." }
public class SolicitudChat
{
    public string? Mensaje { get; set; }
    public string? IdSesion { get; set; }
}

// Respuesta del asistente: { "respuesta": "...", "intencion": "BuscarAlojamiento", ... }
public class RespuestaChat
{
    // Texto completo (es el que se guarda en ConsultasAsistente)
    public string Respuesta { get; set; } = string.Empty;

    // Texto breve para mostrar junto a las fichas cuando la respuesta es una lista
    public string? TextoCorto { get; set; }

    public string? Intencion { get; set; }
    public double Confianza { get; set; }
    public string TipoRespuesta { get; set; } = string.Empty;
    public Guid IdSesion { get; set; }
    public long? IdConsulta { get; set; }
    public List<ResultadoChat> Resultados { get; set; } = new();
}

// Registro de SQL Server mencionado en la respuesta. Los que tienen Url se muestran en el chat
// como fichas: foto, nombre, tipo, estado y botones para ver el perfil y contactar.
public record ResultadoChat(int Id, string Nombre, string Tipo, string? Detalle)
{
    // Número en la lista (para responder «el 2»)
    public int? Numero { get; init; }

    // Tipo visible: "Chofer / Taxista", "Hotel", "Atractivo natural"...
    public string? Subtitulo { get; init; }

    // Foto de perfil o portada (null: se muestra un icono)
    public string? Imagen { get; init; }

    // Icono de Font Awesome para cuando no hay foto
    public string? Icono { get; init; }

    // Ficha pública en el sitio
    public string? Url { get; init; }

    // Enlaces de contacto ya armados (wa.me y tel:)
    public string? WhatsApp { get; init; }
    public string? Telefono { get; init; }

    public bool Activo { get; init; } = true;
}
