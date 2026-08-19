using Microsoft.EntityFrameworkCore.Storage;
using Pos.Application.Abstractions;

namespace Pos.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly PosDbContext _db;
    private IDbContextTransaction? _tx;

    public UnitOfWork(PosDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken ct = default)
    {
        _tx = await _db.Database.BeginTransactionAsync(ct);
        return _tx;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
        if (_tx is not null)
            await _tx.CommitAsync(ct);
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_tx is not null)
            await _tx.RollbackAsync(ct);
    }
}
