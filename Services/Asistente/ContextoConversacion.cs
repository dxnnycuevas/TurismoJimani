namespace AppDonnyCuevas20210074.Services.Asistente;

public enum Dominio
{
    Atractivo,
    Alojamiento,
    Restaurante,
    Transporte,
    Guia,
    Servicio,
    Ruta
}

// Referencia a una ficha que el asistente puede describir: un lugar turístico o un prestador de servicios.
// Los Id de Lugares y Prestadores se repiten entre tablas, por eso se guarda también de cuál es.
public readonly record struct RefFicha(bool EsPrestador, int Id)
{
    public static RefFicha Lugar(int id) => new(false, id);
    public static RefFicha Prestador(int id) => new(true, id);
}

// Lo que el asistente recuerda de la conversación de una sesión (IdSesion).
// Permite responder preguntas de seguimiento como "¿cuál tiene menor precio?" o "háblame del segundo".
public class ContextoConversacion
{
    public string? UltimaIntencion { get; set; }

    // Tipo de resultados que se mostraron por última vez
    public Dominio? Dominio { get; set; }

    // Fichas de la última lista mostrada, en el mismo orden (para "el primero", "el 2", etc.)
    public List<RefFicha> UltimosLugares { get; set; } = new();

    // Rutas de la última lista mostrada
    public List<int> UltimasRutas { get; set; } = new();

    // Última ficha de la que se dio información detallada
    public RefFicha? LugarEnFoco { get; set; }

    // true si lo último que se mostró fue un detalle (no una lista)
    public bool EnfoqueReciente { get; set; }

    public void RecordarLista(Dominio dominio, IEnumerable<RefFicha> fichas)
    {
        Dominio = dominio;
        UltimosLugares = fichas.ToList();
        EnfoqueReciente = false;
        if (UltimosLugares.Count == 1)
            LugarEnFoco = UltimosLugares[0];
    }

    public void RecordarLugar(Dominio? dominio, RefFicha ficha)
    {
        if (dominio.HasValue)
            Dominio = dominio;
        LugarEnFoco = ficha;
        EnfoqueReciente = true;
    }

    public void RecordarRutas(IEnumerable<int> idsRutas)
    {
        Dominio = Asistente.Dominio.Ruta;
        UltimasRutas = idsRutas.ToList();
    }
}
