namespace Pos.Application.Etiquetas;

public record ArticuloParaEtiquetaDto(
    int IdArticulo, int IdPresentacion, string CodigoInterno, string Descripcion, string? DescripcionTicket);

public record TipoTarjetaPrecioDto(
    string NombreTarjeta, decimal Precio, decimal? PrecioPorUnidadMedida, decimal PrecioSinImpuestos);

/// <param name="AclaracionPrecio">
/// "Precio Único" cuando la etiqueta colapsó a un solo precio por folder vigente o porque las
/// tarjetas (Rojo/Azul) coinciden — ver <see cref="Pos.Infrastructure.Services.EtiquetaService"/>.
/// Null en el caso de siempre: artículo sin listas de tarjeta configuradas, un solo precio liso.
/// </param>
public record EtiquetaDto(
    int IdPresentacion, string CodigoInterno, string Descripcion, string? DescripcionTicket,
    string? CodigoBarra, decimal PrecioBase, decimal? PrecioBasePorUnidadMedida, decimal PrecioBaseSinImpuestos,
    List<TipoTarjetaPrecioDto> PreciosTarjeta, decimal CompraMinima, string UnidadMedidaTexto,
    string? AclaracionPrecio = null);

public record LookupSimpleDto(int Id, string Descripcion);
/// <summary>La familia lleva su sector para que el combo de familias se filtre por el sector elegido.</summary>
public record FamiliaLookupDto(int Id, string Descripcion, int? IdSector);
public record ClasificacionesDto(List<LookupSimpleDto> Sectores, List<LookupSimpleDto> Lineas, List<FamiliaLookupDto> Familias);

public interface IEtiquetaService
{
    Task<IReadOnlyList<ArticuloParaEtiquetaDto>> BuscarAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<ArticuloParaEtiquetaDto>> PorClasificacionAsync(
        int? idSector, int? idLinea, int? idFamilia, CancellationToken ct = default);
    Task<IReadOnlyList<EtiquetaDto>> GenerarAsync(int idSucursal, List<int> idsPresentacion, CancellationToken ct = default);
    Task<ClasificacionesDto> GetClasificacionesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupSimpleDto>> GetSucursalesAsync(CancellationToken ct = default);
}
