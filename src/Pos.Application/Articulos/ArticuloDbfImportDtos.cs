namespace Pos.Application.Articulos;

/// <summary>
/// Artículo de articulo.dbf (app legacy VFP "Mayorista") que todavía no existe en SQL por
/// CodigoInterno — fila del comparador de diferencias antes de importar. <paramref name="Motivo"/>
/// viene solo si NO se puede importar (qué clasificación o IVA no tiene equivalente).
/// </summary>
public record ArticuloNuevoDto(
    string CodigoInterno, string Descripcion, string? Sector, string? Linea, string? Familia,
    string? ModoIva, decimal UnidadXBulto, bool Activo, int CantidadBarras, string? Motivo);

/// <param name="Total">Artículos del DBF que no están en SQL.</param>
/// <param name="Importables">Los que se pueden dar de alta (línea e IVA resueltos).</param>
/// <param name="Items">Muestra para la vista previa: primero los que no se pueden importar, tope fijo.</param>
public record ComparadorArticulosDto(int Total, int Importables, IReadOnlyList<ArticuloNuevoDto> Items);

public record ImportacionArticulosResultado(int Articulos, int Presentaciones, int Barras, int NoImportables);

public interface IArticuloDbfImportService
{
    /// <summary>Solo lectura: compara articulo.dbf contra Articulos por CodigoInterno, sin tocar SQL.</summary>
    Task<ComparadorArticulosDto> ObtenerNuevosAsync(CancellationToken ct = default);

    /// <summary>Crea los artículos de articulo.dbf que todavía no existen (nunca actualiza ni borra
    /// los ya cargados) con sus presentaciones y los códigos de barra de cbarras.dbf.</summary>
    Task<ImportacionArticulosResultado> ImportarNuevosAsync(CancellationToken ct = default);
}
