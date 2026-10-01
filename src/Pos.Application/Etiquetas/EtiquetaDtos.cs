namespace Pos.Application.Etiquetas;

public record ArticuloParaEtiquetaDto(
    int IdArticulo, int IdPresentacion, string CodigoInterno, string Descripcion, string? DescripcionTicket);

/// <summary>Artículo con cambio de precio programado para mañana. Si EsPrecioUnico es false viene de
/// LISTAS_PROG.DBF (PrecioNuevo = PFINAL, el Azul nuevo; el Rojo se simula). Si es true viene de
/// PREC_PROG.DBF (PrecioNuevo = PRECIO, un solo precio para Azul y Rojo) y ImpuestoInterno es el IMP_INT.</summary>
public record ArticuloCambioPrecioDto(
    int IdArticulo, int IdPresentacion, string CodigoInterno, string Descripcion, string? DescripcionTicket,
    decimal PrecioNuevo, bool EsPrecioUnico = false, decimal ImpuestoInterno = 0m);

/// <summary>Precio único programado (PREC_PROG.DBF) para una presentación: Azul y Rojo valen lo mismo.</summary>
public record PrecioUnicoNuevoDto(decimal Precio, decimal ImpuestoInterno);

/// <param name="Detectados">Artículos distintos que cumplen el filtro en los DBF (cambios + precios únicos).</param>
/// <param name="SinMatch">Códigos del DBF que no existen como artículo activo con presentación unitaria.</param>
public record CambioPreciosDto(List<ArticuloCambioPrecioDto> Items, int Detectados, List<string> SinMatch);

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
    /// <summary>Coincidencia EXACTA por código de barras o código interno del artículo (a diferencia
    /// de <see cref="BuscarAsync"/>, que también admite texto parcial sobre la descripción) — pensado
    /// para el escaneo numérico en la pantalla angosta de celular (ver EtiquetasPage.tsx). Null si no
    /// hay ningún match exacto.</summary>
    Task<ArticuloParaEtiquetaDto?> BuscarExactoAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<ArticuloParaEtiquetaDto>> PorClasificacionAsync(
        int? idSector, int? idLinea, int? idFamilia, CancellationToken ct = default);
    /// <summary>Artículos con cambio de precio programado para mañana, lista 2068: LISTAS_PROG.DBF
    /// (HECHO vacío, Azul nuevo en PFINAL) y PREC_PROG.DBF (F_DESDE = mañana, precio único en PRECIO).
    /// Si un artículo está en los dos gana el precio único. Listos para sumar a la lista de etiquetas.</summary>
    Task<CambioPreciosDto> CambioDePreciosAsync(CancellationToken ct = default);
    /// <param name="preciosAzulNuevos">Solo para "Cambio de Precios": idPresentacion → Azul nuevo (PFINAL).
    /// Para esas presentaciones la etiqueta sale con ese Azul y con el Rojo simulado (Azul nuevo + el
    /// diferencial de la lista Rojo); el resto de las presentaciones no se toca.</param>
    /// <param name="preciosUnicosNuevos">Solo para "Cambio de Precios": idPresentacion → precio único
    /// (PREC_PROG.DBF). La etiqueta sale con un solo precio ("Precio Único"), sin importar las listas.</param>
    Task<IReadOnlyList<EtiquetaDto>> GenerarAsync(int idSucursal, List<int> idsPresentacion,
        IReadOnlyDictionary<int, decimal>? preciosAzulNuevos = null,
        IReadOnlyDictionary<int, PrecioUnicoNuevoDto>? preciosUnicosNuevos = null, CancellationToken ct = default);
    Task<ClasificacionesDto> GetClasificacionesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupSimpleDto>> GetSucursalesAsync(CancellationToken ct = default);
}
