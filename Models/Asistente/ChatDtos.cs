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
    public string Respuesta { get; set; } = string.Empty;
    public string? Intencion { get; set; }
    public double Confianza { get; set; }
    public string TipoRespuesta { get; set; } = string.Empty;
    public Guid IdSesion { get; set; }
    public long? IdConsulta { get; set; }
    public List<ResultadoChat> Resultados { get; set; } = new();
}

// Registro de SQL Server mencionado en la respuesta (para uso futuro en la interfaz)
public record ResultadoChat(int Id, string Nombre, string Tipo, string? Detalle);
