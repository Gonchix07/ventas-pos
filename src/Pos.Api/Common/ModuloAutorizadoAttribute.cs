using Microsoft.AspNetCore.Mvc.Filters;

namespace Pos.Api.Common;

/// <summary>
/// Autoriza por MÓDULO además de por rol: dado un controller con `[Authorize] [ModuloAutorizado("X",
/// "RolA,RolB")]`, deja pasar si el usuario tiene alguno de los roles fijos de siempre, O si su
/// token trae el claim "modulo"="X" — que es exactamente lo que arma <see
/// cref="Pos.Application.Abstractions.IJwtTokenGenerator.Generar"/> a partir de "Permisos por rol"
/// (ver Pos.Infrastructure.Services.PermisoAdminService / PermisoRepository.ModulosPorRolAsync).
///
/// Reemplaza a `[Authorize(Roles="...")]` en los controllers que tienen una tarjeta propia en el
/// menú principal: sin esto, tildar un módulo para un rol en "Permisos por rol" solo habilitaba la
/// tarjeta del menú (front), pero el backend seguía rechazando con 403 porque cada endpoint tenía
/// su lista de roles fija en el código — el tilde no hacía nada de verdad. La lista de roles NUNCA
/// se saca: solo se le suma el claim de módulo como vía adicional, así ningún acceso que ya
/// funcionaba por rol se pierde.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ModuloAutorizadoAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _modulos;
    private readonly string[] _roles;

    /// <param name="modulos">Uno o más módulos (separados por coma) — alcanza con tener el claim
    /// de CUALQUIERA de ellos. Sirve para endpoints compartidos por más de una pantalla (ej. el
    /// lookup de sucursales que usan tanto Reimpresión como Cupones).</param>
    public ModuloAutorizadoAttribute(string modulos, string roles)
    {
        _modulos = modulos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _roles = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Bug real (2026-08-25): un atributo de clase Y otro de método (ej. ReferenciasController,
        // con la clase en "Administracion" y el método Sucursales sobreescrito a
        // "Reimpresion,Tesoreria,Administracion") NO se reemplazan solos — ASP.NET Core mete las
        // DOS instancias en el pipeline y las corre a ambas, así que sin esto la del método pasaba
        // pero la de la clase igual rechazaba con 403 (síntoma real: un Cajero con "Reimpresion"
        // habilitado en Permisos entraba a la pantalla pero el combo de sucursal quedaba vacío).
        // Patrón estándar de "override" de filtros: solo corre la instancia MÁS ESPECÍFICA — la
        // última de este tipo en context.Filters, que ASP.NET Core arma clase-primero-método-último.
        var masEspecifico = context.Filters.OfType<ModuloAutorizadoAttribute>().Last();
        if (masEspecifico != this) return;

        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }
        if (_roles.Any(user.IsInRole)) return;
        if (user.Claims.Any(c => c.Type == "modulo" && _modulos.Contains(c.Value))) return;

        context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
    }
}
