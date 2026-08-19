using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Pos.Application.Abstractions;

namespace Pos.Api.Common;

/// <summary>
/// Auditoría genérica de escrituras (POST/PUT/PATCH/DELETE) para los endpoints que NO pasan por
/// el pipeline de MediatR (que hoy solo cubre Login vía <see cref="Pos.Application.Common.Behaviors.AuditBehavior{TRequest,TResponse}"/>).
/// Caja, Cierres, Facturación y los ~13 controllers de ABM administrativo llaman directo a sus
/// servicios sin pasar por MediatR — sin este filtro, esas operaciones (incluyendo el Cierre Z,
/// la emisión de comprobantes y toda alta/baja/modificación de datos maestros) no dejaban ningún
/// rastro en MovimientoAuditoria.
///
/// Se resuelve acá, como filtro MVC transversal, en vez de migrar cada endpoint a un comando
/// MediatR: mismo resultado (auditoría real) con un cambio acotado, sin reescribir ~20
/// controllers ya verificados e2e. Registrado como filtro global en Program.cs.
/// </summary>
public class AuditoriaActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> MetodosAuditables =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    private readonly IAuditLogger _audit;
    public AuditoriaActionFilter(IAuditLogger audit) => _audit = audit;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        // La acción tiró una excepción (validación, DomainException, error inesperado): no hubo
        // escritura efectiva, no se audita. La excepción sigue su curso normal hacia el
        // ExceptionMiddleware — este filtro no la toca.
        if (executed.Exception is not null)
            return;

        if (!MetodosAuditables.Contains(context.HttpContext.Request.Method))
            return;

        if (context.ActionDescriptor is not ControllerActionDescriptor cad)
            return;

        // Login/Me ya tienen su propia auditoría (Login vía MediatR/AuditBehavior; Me es de solo
        // lectura) — no duplicar.
        if (cad.ControllerName == "Auth")
            return;

        // Solo se audita una respuesta 2xx: un 400/404/409 de negocio no representa una
        // escritura real.
        var statusCode = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? StatusCodes.Status200OK;
        if (statusCode is < 200 or >= 300)
            return;

        var entidadId = context.RouteData.Values
            .Where(kv => kv.Key.Contains("id", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value?.ToString())
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));

        await _audit.LogAsync(cad.ControllerName, $"{context.HttpContext.Request.Method}:{cad.ActionName}",
            entidad: cad.ControllerName, entidadId: entidadId, ct: context.HttpContext.RequestAborted);
    }
}
