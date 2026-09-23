namespace AppDonnyCuevas20210074.Services.Asistente;

public enum Dominio
{
    Atractivo,
    Alojamiento,
    Restaurante,
    Transporte,
    Ruta
}

// Lo que el asistente recuerda de la conversación de una sesión (IdSesion).
// Permite responder preguntas de seguimiento como "¿cuál tiene menor precio?" o "háblame del segundo".
public class ContextoConversacion
{
    public string? UltimaIntencion { get; set; }

    // Tipo de resultados que se mostraron por última vez
    public Dominio? Dominio { get; set; }

    // Lugares de la última lista mostrada, en el mismo orden (para "el primero", "el 2", etc.)
    public List<int> UltimosLugares { get; set; } = new();

    // Rutas de la última lista mostrada
    public List<int> UltimasRutas { get; set; } = new();

    // Último lugar del que se dio información detallada
    public int? LugarEnFoco { get; set; }

    // true si lo último que se mostró fue un detalle (no una lista)
    public bool EnfoqueReciente { get; set; }

    public void RecordarLista(Dominio dominio, IEnumerable<int> idsLugares)
    {
        Dominio = dominio;
        UltimosLugares = idsLugares.ToList();
        EnfoqueReciente = false;
        if (UltimosLugares.Count == 1)
            LugarEnFoco = UltimosLugares[0];
    }

    public void RecordarLugar(Dominio? dominio, int idLugar)
    {
        if (dominio.HasValue)
            Dominio = dominio;
        LugarEnFoco = idLugar;
        EnfoqueReciente = true;
    }

    public void RecordarRutas(IEnumerable<int> idsRutas)
    {
        Dominio = Asistente.Dominio.Ruta;
        UltimasRutas = idsRutas.ToList();
    }
}
