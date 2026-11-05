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

    // Respuestas rápidas que el chat muestra como botones ("Más barato", "Menos noches"...)
    public List<string> Sugerencias { get; set; } = new();

    // Plan de viaje que el chat muestra como tarjeta (solo en respuestas del planificador)
    public PresupuestoChat? Presupuesto { get; set; }

    // Ficha detallada que el chat muestra como tarjeta (ruta, evento, lugar, prestador, ubicación o dato curioso)
    public DetalleChat? Detalle { get; set; }
}

// Tarjeta de detalle. Clase: ruta, evento, lugar, prestador, ubicacion o dato (decide colores e icono en el chat)
public class DetalleChat
{
    public string Clase { get; set; } = "lugar";
    public string Titulo { get; set; } = string.Empty;

    // Tipo visible: "Hotel", "Actividad cultural", "Ruta"...
    public string? Subtitulo { get; set; }
    public string? Imagen { get; set; }

    // Icono de Font Awesome de la cabecera (si no hay fecha)
    public string Icono { get; set; } = "fa-info-circle";

    // Frase destacada bajo el título
    public string? Resumen { get; set; }

    // Recorrido de una ruta
    public string? Origen { get; set; }
    public string? Destino { get; set; }

    // Mapa incrustado con el recorrido de la ruta
    public string? MapaRuta { get; set; }

    // Bloque de calendario de un evento
    public FechaDetalle? Fecha { get; set; }

    public List<DatoDetalle> Datos { get; set; } = new();
    public List<SeccionDetalle> Secciones { get; set; } = new();
    public List<AccionDetalle> Acciones { get; set; } = new();
}

// Dato corto con icono: ("fa-road", "Distancia", "40 km")
public record DatoDetalle(string Icono, string Etiqueta, string Valor);

// Párrafo de la tarjeta; sin título es parte de la descripción
public record SeccionDetalle(string? Titulo, string Texto);

// Botón de la tarjeta. Tipo: ficha, whatsapp, telefono, mapa o enlace
public record AccionDetalle(string Tipo, string Texto, string Url);

// "sáb", "10", "oct"
public record FechaDetalle(string DiaSemana, string Dia, string Mes);

// Tarjeta del planificador: cuánto cuesta cada parte del viaje y si cabe en el dinero del usuario
public class PresupuestoChat
{
    public decimal Monto { get; set; }
    public decimal Total { get; set; }
    public int Noches { get; set; }

    // Ciudad de salida; vacío si el plan no incluye pasaje
    public string Origen { get; set; } = string.Empty;

    // true si el total no pasa del monto
    public bool Cabe { get; set; }

    // true si se cambiaron las opciones habituales por otras más baratas para que el plan quepa
    public bool Ajustado { get; set; }

    // true si el usuario pidió las opciones más económicas
    public bool Economico { get; set; }

    public List<LineaPresupuestoChat> Lineas { get; set; } = new();
}

// Clase: pasaje, hospedaje, comida o transporte (decide el color y el icono en el chat)
public record LineaPresupuestoChat(string Clase, string Concepto, string Opcion, string Detalle, decimal Subtotal)
{
    // Ficha pública del prestador sugerido
    public string? Url { get; init; }
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

    // Salida y llegada (solo en las rutas)
    public string? Origen { get; init; }
    public string? Destino { get; init; }
}
