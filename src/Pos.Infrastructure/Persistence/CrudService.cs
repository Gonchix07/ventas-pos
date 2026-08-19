using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;

namespace Pos.Infrastructure.Persistence;

public class CrudService<TEntity> : ICrudService<TEntity> where TEntity : class
{
    private readonly PosDbContext _db;
    private readonly DbSet<TEntity> _set;

    public CrudService(PosDbContext db)
    {
        _db = db;
        _set = db.Set<TEntity>();
    }

    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default) =>
        await _set.AsNoTracking().ToListAsync(ct);

    public async Task<TEntity?> GetByIdAsync(object key, CancellationToken ct = default) =>
        await _set.FindAsync(new[] { key }, ct);

    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken ct = default)
    {
        _set.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        _set.Update(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(object key, CancellationToken ct = default)
    {
        var entity = await _set.FindAsync(new[] { key }, ct);
        if (entity is null) return false;
        _set.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
