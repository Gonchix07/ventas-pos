namespace Pos.Application.Precios;

public record ListaPrecioDto(
    int IdListaPrecio, int IdSucursal, string? SucursalDescripcion,
    string CodigoInterno, int Tipo, string TipoDescripcion,
    int Prioridad, DateTime? FechaInicio, DateTime? FechaFin, int CantidadPrecios,
    // Solo con valor cuando Tipo=Enlazada (4).
    int? IdListaBase, string? ListaBaseCodigoInterno, int CantidadDiferenciales);

public record ListaPrecioInput(
    int IdSucursal, string CodigoInterno, int Tipo, int Prioridad,
    DateTime? FechaInicio, DateTime? FechaFin,
    // Obligatorio cuando Tipo=Enlazada (4); ignorado para el resto.
    int? IdListaBase = null);

public record PrecioDto(
    int IdPresentacion, int IdArticulo, string CodigoInterno, string ArticuloDescripcion,
    string? DescripcionTicket, decimal UnidadXBulto, decimal PrecioFinal, decimal ImpuestoInterno);

public record PrecioInput(decimal PrecioFinal, decimal ImpuestoInterno);

/// <summary>
/// Precio de la unidad suelta. Cada presentación del artículo se valoriza multiplicando por sus
/// unidades por bulto (ver <see cref="Pos.Domain.Services.PrecioPorBulto"/>).
/// </summary>
public record PrecioArticuloInput(decimal PrecioUnitario, decimal ImpuestoInternoUnitario);

/// <summary>Lo que quedó cargado en cada presentación, para poder mostrarlo tras guardar.</summary>
public record PrecioAplicadoDto(int IdPresentacion, string? DescripcionTicket, decimal UnidadXBulto,
    decimal PrecioFinal, decimal ImpuestoInterno);

public interface IListaPrecioService
{
    Task<IReadOnlyList<ListaPrecioDto>> GetAllAsync(CancellationToken ct = default);
    Task<ListaPrecioDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(ListaPrecioInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, ListaPrecioInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Precios cargados en la lista. Devuelve como máximo 50 filas (una lista real tiene más de
    /// 16.000): para encontrar uno puntual está <paramref name="texto"/>, que busca por código o
    /// descripción del artículo. Con <paramref name="idsArticulos"/> se piden precios puntuales de
    /// artículos concretos (sin tope, porque el llamador ya acotó la cantidad).
    /// </summary>
    Task<IReadOnlyList<PrecioDto>> GetPreciosAsync(int idListaPrecio, string? texto = null,
        IReadOnlyList<int>? idsArticulos = null, CancellationToken ct = default);
    /// <summary>Alta o modificación del precio de una presentación en la lista.</summary>
    Task<bool> UpsertPrecioAsync(int idListaPrecio, int idPresentacion, PrecioInput input, CancellationToken ct = default);

    /// <summary>
    /// Carga el precio de TODAS las presentaciones del artículo a partir de un único precio
    /// unitario (× unidades por bulto de cada una). Devuelve null si no existe la lista.
    /// </summary>
    Task<IReadOnlyList<PrecioAplicadoDto>?> UpsertPrecioArticuloAsync(
        int idListaPrecio, int idArticulo, PrecioArticuloInput input, CancellationToken ct = default);
    Task<bool> DeletePrecioAsync(int idListaPrecio, int idPresentacion, CancellationToken ct = default);

    // --- Diferenciales de una lista Enlazada (recargo % sobre el precio de su ListaBase) ---
    Task<IReadOnlyList<DiferencialListaPrecioDto>> GetDiferencialesAsync(int idListaPrecio, CancellationToken ct = default);
    Task<int?> CreateDiferencialAsync(int idListaPrecio, DiferencialListaPrecioInput input, CancellationToken ct = default);
    Task<bool> UpdateDiferencialAsync(int idListaPrecio, int idDiferencial, DiferencialListaPrecioInput input, CancellationToken ct = default);
    Task<bool> DeleteDiferencialAsync(int idListaPrecio, int idDiferencial, CancellationToken ct = default);

    /// <summary>Relee descxtipocli_art.dbf (TIPO_TARJE='03', solo vigentes por DESDE/HASTA) y
    /// reemplaza los diferenciales de esta lista. Devuelve la cantidad importada, o null si la lista
    /// no existe o no es Tipo=Enlazada.</summary>
    Task<int?> ImportarDiferencialesAsync(int idListaPrecio, CancellationToken ct = default);

    /// <summary>Tipo=Base: relee listas.dbf (NUMERO=2068) y carga/actualiza los precios. Tipo=Folder:
    /// borra los precios actuales y carga los de prec_prog.dbf (lista 2068) vigentes hoy. Devuelve
    /// null si la lista no existe o es de otro tipo.</summary>
    Task<ImportacionPreciosResultado?> ImportarPreciosAsync(int idListaPrecio, CancellationToken ct = default);
}

/// <param name="Eliminados">Precios borrados antes de cargar (solo listas Folder, que se reemplazan enteras).</param>
/// <param name="Creados">Precios nuevos (presentaciones que no tenían precio en la lista).</param>
/// <param name="Actualizados">Precios que ya existían y se pisaron con el del DBF.</param>
/// <param name="SinMatch">Códigos del DBF que no existen como artículo en el sistema.</param>
/// <param name="SinPresentaciones">Artículos encontrados pero sin presentaciones a las que asignarles precio.</param>
public record ImportacionPreciosResultado(int Creados, int Actualizados, int SinMatch, int SinPresentaciones, int Eliminados = 0);

public record DiferencialListaPrecioDto(
    int IdDiferencial, int IdListaPrecio, int? IdLinea, string? LineaDescripcion,
    int? IdArticulo, string? ArticuloCodigoInterno, string? ArticuloDescripcion, decimal Porcentaje);

/// <summary>Exactamente uno de IdLinea/IdArticulo debe venir cargado (nunca ambos, nunca ninguno).</summary>
public record DiferencialListaPrecioInput(int? IdLinea, int? IdArticulo, decimal Porcentaje);
