using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AppDonnyCuevas20210074.Helpers;

// Cambio de contraseña obligatorio (S-03).
// Al iniciar sesión se marca la cookie con el claim "cambio_contrasena_pendiente" cuando la contraseña usada:
// - es la contraseña inicial del administrador (AdminInicial:Contrasena), o
// - ya no cumple la política vigente (por ejemplo, la antigua "Admin123*" o claves de 6 caracteres).
// Mientras el claim exista, el filtro envía al usuario a /Cuenta/CambiarContrasena y no deja usar el resto
// de la aplicación. La cookie va cifrada y firmada, por lo que el claim no se puede quitar desde el navegador.
public static class CambioContrasena
{
    public const string ClaimPendiente = "cambio_contrasena_pendiente";

    public static bool EstaPendiente(System.Security.Claims.ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true && usuario.HasClaim(ClaimPendiente, "1");
}

// Marca las acciones que se pueden usar con el cambio pendiente (iniciar o cerrar sesión, cambiar la contraseña)
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PermitirConCambioPendienteAttribute : Attribute
{
}

public class ExigirCambioContrasenaFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext contexto)
    {
        if (!CambioContrasena.EstaPendiente(contexto.HttpContext.User))
            return;

        if (contexto.ActionDescriptor.EndpointMetadata.OfType<PermitirConCambioPendienteAttribute>().Any())
            return;

        var peticion = contexto.HttpContext.Request;
        if (HttpMethods.IsGet(peticion.Method) || HttpMethods.IsHead(peticion.Method))
        {
            var volver = $"{peticion.PathBase}{peticion.Path}{peticion.QueryString}";
            contexto.Result = new RedirectToActionResult("CambiarContrasena", "Cuenta", new { returnUrl = volver });
        }
        else
        {
            contexto.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }

    public void OnActionExecuted(ActionExecutedContext contexto)
    {
    }
}
