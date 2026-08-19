using MediatR;
using Pos.Application.Abstractions;

namespace Pos.Application.Common.Behaviors;

/// <summary>Registra en la auditoría de negocio los requests marcados con IAuditableRequest.</summary>
public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IAuditLogger _audit;

    public AuditBehavior(IAuditLogger audit) => _audit = audit;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next();

        if (request is IAuditableRequest a)
            await _audit.LogAsync(a.Modulo, a.Accion, a.Entidad, a.EntidadId, ct: ct);

        return response;
    }
}
