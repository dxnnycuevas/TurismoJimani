using System.Security.Claims;

namespace AppDonnyCuevas20210074.Helpers;

// Datos del visitante con sesión iniciada en el sitio público (rol Visitante).
// El personal (Administrador, Editor) no tiene perfil de visitante.
public static class SesionVisitante
{
    public static int? IdUsuario(ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true
        && usuario.IsInRole(RolesSistema.Visitante)
        && int.TryParse(usuario.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
}
