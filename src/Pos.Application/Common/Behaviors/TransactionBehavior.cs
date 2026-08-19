using MediatR;
using Pos.Application.Abstractions;

namespace Pos.Application.Common.Behaviors;

/// <summary>
/// Envuelve los requests marcados con ITransactionalRequest en una transacción de BD:
/// o se persiste todo, o nada (integridad ACID para operaciones con dinero/numeración).
/// </summary>
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _uow;

    public TransactionBehavior(IUnitOfWork uow) => _uow = uow;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not ITransactionalRequest)
            return await next();

        await using var _ = await _uow.BeginTransactionAsync(ct);
        try
        {
            var response = await next();
            await _uow.CommitAsync(ct);
            return response;
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }
    }
}
