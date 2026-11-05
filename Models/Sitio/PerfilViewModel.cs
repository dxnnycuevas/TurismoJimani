using AppDonnyCuevas20210074.Models.Asistente;

namespace AppDonnyCuevas20210074.Models.Sitio;

// Datos de la página Mi perfil del visitante (PerfilController)
public class PerfilViewModel
{
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public DateTime Registro { get; set; }

    public List<Lugar> LugaresFavoritos { get; set; } = new();
    public List<Prestador> PrestadoresFavoritos { get; set; } = new();
    public List<PlanPerfil> Planes { get; set; } = new();
    public List<ConversacionPerfil> Conversaciones { get; set; } = new();
    public int TotalMensajes { get; set; }

    // Sección abierta al cargar la página (favoritos, planes, conversaciones o cuenta)
    public string Seccion { get; set; } = "favoritos";
}

public class PlanPerfil
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Origen { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public decimal Total { get; set; }
    public int Noches { get; set; }
    public bool Cabe { get; set; }
    public List<LineaPresupuestoChat> Lineas { get; set; } = new();
}

// Mensajes de una misma sesión del chat (IdSesion), del más antiguo al más reciente
public class ConversacionPerfil
{
    public Guid IdSesion { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public List<ConsultaAsistente> Mensajes { get; set; } = new();
}
