namespace Pos.Application.Catalogo;

/// <summary>
/// Familia de artículos. Se mantiene el campo <c>Id</c> (y no IdFamilia) para que el DTO siga siendo
/// un superconjunto del LookupDto genérico y las pantallas que ya consumían /admin/familias como
/// lookup simple no se rompan.
/// </summary>
public record FamiliaDto(int Id, string Descripcion, int? IdSector, string? SectorDescripcion);

public record FamiliaInput(string Descripcion, int? IdSector);

public interface IFamiliaService
{
    /// <param name="idSector">Si viene, devuelve solo las familias de ese sector.</param>
    Task<IReadOnlyList<FamiliaDto>> GetAllAsync(int? idSector = null, CancellationToken ct = default);
    Task<FamiliaDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<FamiliaDto> CreateAsync(FamiliaInput input, CancellationToken ct = default);
    Task<FamiliaDto?> UpdateAsync(int id, FamiliaInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
