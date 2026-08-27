namespace Pos.Application.VerificarPrecios;

/// <summary>Precio del artículo en una lista puntual (AZUL/ROJA) — null si esa lista no tiene precio
/// cargado para esta presentación.</summary>
public record PrecioListaResumen(string CodigoLista, decimal? Precio);

public record OfertaResumen(int IdOferta, string Descripcion);

/// <summary>
/// Resultado de escanear un producto en el kiosco de autoconsulta de precios (módulo
/// "VerificarPrecios"). A diferencia de Caja — que resuelve UN precio final ya ganador para el
/// cliente identificado (ver <c>ICajaService.BuscarArticuloAsync</c>) — acá se muestran
/// explícitamente los precios de las listas AZUL y ROJA en paralelo (independiente de cuál
/// "ganaría" en una venta real), más las dos señales que dan lugar al sticker de distinción:
/// si está en una Lista Folder vigente y si tiene alguna oferta aplicable.
/// </summary>
public record ConsultaPrecioResult(string CodigoInterno, string Descripcion, string ImagenUrl,
    IReadOnlyList<PrecioListaResumen> Precios, bool EsListaFolder, IReadOnlyList<OfertaResumen> Ofertas);

public interface IVerificarPreciosService
{
    /// <summary>Null si el código no matchea ningún artículo activo (mismo criterio de búsqueda que
    /// Caja: barra exacta, etiqueta de balanza, código interno).</summary>
    Task<ConsultaPrecioResult?> ConsultarAsync(int idSucursal, string codigo, CancellationToken ct = default);
}
