namespace Pos.Application.Abm;

public record AlcanceDto(int? IdCluster, int? IdLinea, int? IdSector, int? IdFamilia, int? IdArticulo, bool EsExcepcion,
    string? ArticuloDescripcion = null);

/// <summary>
/// Renglón de una Mix Canasta (solo ese tipo). <paramref name="Rol"/> = <c>RolItemCanasta</c>:
/// 1 la canasta que activa la oferta, 2 la que se bonifica. La descripción es de solo lectura.
/// </summary>
public record ItemCanastaDto(int IdArticulo, decimal Cantidad, int Rol, string? ArticuloDescripcion = null);

public record AccionDto(int IdTipoOferta, int? IdPresentacion, decimal? Porcentaje, decimal? MontoFijo,
    decimal? CantidadMin, decimal? CantidadBonif, List<ItemCanastaDto>? Items = null);

/// <summary>Tipo de oferta ofrecido en el ABM. <paramref name="Codigo"/> = <c>TipoOfertaEnum</c>: define qué campos pide.</summary>
public record TipoOfertaDto(int Id, string Descripcion, int Codigo);

public record OfertaListItem(int IdSucursal, int IdOferta, string Descripcion,
    DateTime FechaInicio, DateTime FechaFin, bool Acumula, bool PermiteConvenio,
    int CantAlcances, int CantAcciones);

public record OfertaDetail(int IdSucursal, int IdOferta, string Descripcion,
    DateTime FechaInicio, DateTime FechaFin, bool Acumula, bool PermiteConvenio,
    List<AlcanceDto> Alcances, List<AccionDto> Acciones);

public record OfertaInput(string Descripcion, DateTime FechaInicio, DateTime FechaFin,
    bool Acumula, bool PermiteConvenio, List<AlcanceDto> Alcances, List<AccionDto> Acciones);

public interface IOfertaAdminService
{
    Task<IReadOnlyList<OfertaListItem>> GetAllAsync(int idSucursal, CancellationToken ct = default);
    Task<OfertaDetail?> GetByIdAsync(int idSucursal, int idOferta, CancellationToken ct = default);
    Task<int> CreateAsync(int idSucursal, OfertaInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int idSucursal, int idOferta, OfertaInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int idSucursal, int idOferta, CancellationToken ct = default);
}
