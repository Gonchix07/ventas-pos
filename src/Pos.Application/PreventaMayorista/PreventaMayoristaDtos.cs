namespace Pos.Application.PreventaMayorista;

/// <summary>
/// Una línea de artículo dentro de una precarga (pedido) del sistema legacy VFP "Mayorista", leída
/// de pedidos.dbf. "Cantidad" es la columna CANTIDAD del DBF tal cual (misma codificación que usa
/// la propia app legacy al exportar a MySQL: parte entera = bultos, parte decimal = unidades sueltas
/// de un bulto abierto — no confundir con la cantidad originalmente pedida).
/// </summary>
public record PreventaLineaDto(
    string Reparto,
    string PVentista,
    string CodigoArticulo,
    string? DescripcionArticulo,
    // Articulo.UnidadXBulto (ficha del artículo), para la columna "UxB" del detalle.
    decimal UnidadXBulto,
    decimal Cantidad,
    // CANT_ORIG del DBF: lo originalmente pedido, para la columna "RSV / ORIG" (ver
    // PreventaClienteDto.CantidadOriginal, mismo concepto a nivel línea).
    decimal CantidadOriginal,
    decimal Importe,
    decimal Descuento,
    string Precarga,
    DateOnly? FechaPedido,
    string Estado,
    // Solo tiene sentido cuando Descuento > 0 (null en el resto): si el descuento está autorizado
    // por un convenio vigente (CONVENIO=1 y CODCONV cargado en el DBF) o no.
    bool? Autorizado,
    // "Entrega"/"Retiro"/"Fox", derivado de NUMERO_PED — ver PreventaMayoristaService.Logis.
    string Logis);

/// <summary>
/// Cabecera de pedidos pendientes de un cliente: agrupa todas las líneas de sus precargas con
/// REPARTO en (PEDIPEND, PEDIAPP) — los únicos dos estados "vigentes" en pedidos.dbf, el resto ya
/// está descartado o asignado a un reparto real.
/// </summary>
public record PreventaClienteDto(
    string CodigoCliente,
    string? NombreCliente,
    // Null en ambos si el cliente no se encontró en el padrón (código del DBF sin match).
    string? CondicionIva,
    bool? AdmitePresupuesto,
    // Columna "RESERVA/ORIG": suma de CANTIDAD (lo ya armado/reservado) y de CANT_ORIG (lo
    // originalmente pedido) de todas las líneas — dos números aparte, no un neto.
    decimal CantidadReserva,
    decimal CantidadOriginal,
    decimal ImporteFinal,
    // Recargo logístico: % de RecargoLogisticaDto (según en qué tramo cae la suma de líneas con
    // Logis="Entrega") aplicado sobre esa misma suma. 0 si no hay líneas de Entrega o no matchea
    // ningún tramo — ver PreventaMayoristaService.CalcularRecargoLogistica.
    decimal RecargoLogistica,
    int CantidadPrecargas,
    IReadOnlyList<PreventaLineaDto> Lineas);

public interface IPreventaMayoristaService
{
    /// <param name="forzarRefresco">true = ignora el caché y vuelve a leer el DBF.</param>
    Task<IReadOnlyList<PreventaClienteDto>> ObtenerPedidosPendientesAsync(bool forzarRefresco = false,
        CancellationToken ct = default);
}

/// <summary>
/// Un tramo de recargo logístico (ver <see cref="Pos.Domain.Entities.RecargoLogistica"/>), ya
/// importado a SQL — ImportarAsync sincroniza desde recargo_logistica.dbf.
/// </summary>
public record RecargoLogisticaDto(
    decimal Inicio,
    decimal Fin,
    decimal Porcentaje);

public interface IRecargoLogisticaService
{
    Task<IReadOnlyList<RecargoLogisticaDto>> ObtenerAsync(CancellationToken ct = default);

    /// <summary>Relee recargo_logistica.dbf y reemplaza el contenido de la tabla SQL.</summary>
    /// <returns>Cantidad de tramos importados.</returns>
    Task<int> ImportarAsync(CancellationToken ct = default);
}
