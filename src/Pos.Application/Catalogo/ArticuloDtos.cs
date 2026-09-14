namespace Pos.Application.Catalogo;

public record BarraDto(int IdBarra, string CodigoBarra, int Tipo);
/// <summary>IdBarra null/0 = código nuevo; con valor = el frontend lo trae de BarraDto tal cual se
/// cargó (ver ArticulosPage.tsx), para que ArticuloService.UpdateAsync sepa qué códigos actualizar
/// y cuáles borrar en vez de tratarlos siempre como altas nuevas.</summary>
public record BarraInput(string CodigoBarra, int Tipo, int? IdBarra = null);

public record PresentacionDto(int IdPresentacion, decimal UnidadXBulto, string? DescripcionTicket, List<BarraDto> Barras);
/// <summary>IdPresentacion null/0 = presentación nueva; con valor = ya existe y el frontend la trae
/// de PresentacionDto (ver ArticulosPage.tsx) — permite a ArticuloService.UpdateAsync diferenciar
/// "actualizar esta línea" de "el cajero la borró del formulario" en vez de ignorar el array entero.</summary>
public record PresentacionInput(decimal UnidadXBulto, string? DescripcionTicket, List<BarraInput> Barras,
    int? IdPresentacion = null);

public record ArticuloListItem(
    int IdArticulo, string CodigoInterno, string Descripcion,
    int IdSector, int IdLinea, int IdFamilia, int IdModoIva,
    string? SectorDescripcion, string? LineaDescripcion, string? FamiliaDescripcion, string? ModoIvaDescripcion,
    int UnidadMedida, decimal? ContenidoNetoUnitario,
    int CantidadPresentaciones, int CantidadBarras, string? PrimeraBarra,
    bool Activo, string ImagenUrl);

/// <summary>
/// Filtros del listado de artículos del ABM. Todos opcionales (null = sin filtrar).
/// <para><c>Max</c> acota cuántas filas devuelve (se recorta al tope del servicio): un buscador que
/// solo muestra las primeras N no necesita traerse 500 filas del catálogo.</para>
/// </summary>
public record ArticuloFiltro(string? Texto, int? IdSector, int? IdLinea, int? IdFamilia, bool? Activo,
    int? Max = null);

public record ArticuloDetail(
    int IdArticulo, string CodigoInterno, string Descripcion,
    int IdSector, int IdLinea, int IdFamilia, int IdModoIva,
    bool Activo, string ImagenUrl, int UnidadMedida, decimal? ContenidoNetoUnitario,
    decimal UnidadXBulto, bool VentaPorPeso, decimal MinimaUnidadVenta,
    List<PresentacionDto> Presentaciones);

public record ArticuloInput(
    string CodigoInterno, string Descripcion,
    int IdSector, int IdLinea, int IdFamilia, int IdModoIva,
    bool Activo, int UnidadMedida, decimal? ContenidoNetoUnitario,
    decimal UnidadXBulto, bool VentaPorPeso,
    List<PresentacionInput> Presentaciones, decimal MinimaUnidadVenta = 1m);

public interface IArticuloService
{
    Task<IReadOnlyList<ArticuloListItem>> GetAllAsync(ArticuloFiltro? filtro = null, CancellationToken ct = default);
    Task<ArticuloDetail?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(ArticuloInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, ArticuloInput input, CancellationToken ct = default);
    /// <summary>Baja lógica (Activo = false).</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
