using System.Security.Claims;

namespace AppDonnyCuevas20210074.Helpers;

// Roles de la aplicación. Administrador y Editor forman el "personal" que entra al panel;
// Visitante es el rol de las cuentas creadas desde el registro público.
public static class RolesSistema
{
    public const string Administrador = "Administrador";
    public const string Editor = "Editor";
    public const string Visitante = "Visitante";

    public static readonly string[] Personal = { Administrador, Editor };

    public static bool EsPersonal(ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true && Personal.Any(usuario.IsInRole);

    public static bool EsPersonal(string? nombreRol) =>
        nombreRol != null && Personal.Contains(nombreRol);
}
