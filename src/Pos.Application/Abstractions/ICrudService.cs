namespace Pos.Application.Abstractions;

/// <summary>Servicio CRUD genérico sobre una entidad. Para ABM de datos maestros.</summary>
public interface ICrudService<TEntity> where TEntity : class
{
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default);
    Task<TEntity?> GetByIdAsync(object key, CancellationToken ct = default);
    Task<TEntity> AddAsync(TEntity entity, CancellationToken ct = default);
    Task UpdateAsync(TEntity entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(object key, CancellationToken ct = default);
}
