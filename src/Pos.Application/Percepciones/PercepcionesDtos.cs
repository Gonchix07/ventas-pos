namespace Pos.Application.Percepciones;

/// <summary>Una línea de venta tal como la necesita el cálculo de percepciones (subconjunto mínimo).</summary>
public record LineaParaPercepcion(int IdPresentacion, decimal Cantidad, decimal Precio, decimal Descuento, int? IdListaPrecio);

/// <summary>
/// Resultado del cálculo de percepciones de una operación. <paramref name="AlicuotaEfectivaPorLinea"/>
/// viene en el MISMO ORDEN que la lista de líneas pasada a <see cref="IPercepcionesCalculoService.CalcularAsync"/>
/// (no por diccionario, para no depender de que IdPresentacion sea único dentro del carrito) — ya
/// con el ajuste "impuesto interno → Exento" aplicado, así que quien facture no tiene que resolver
/// la alícuota del artículo dos veces.
/// </summary>
public record PercepcionesResultado(
    decimal PercepcionIva21, decimal PercepcionIva105, decimal PercepcionIibb, decimal Total,
    IReadOnlyList<decimal> AlicuotaEfectivaPorLinea,
    decimal BaseImponibleIva21 = 0, decimal BaseImponibleIva105 = 0, decimal BaseImponibleIibb = 0);

/// <summary>
/// Calcula, en el momento del carrito o de facturar, las percepciones de IVA (según neto gravado al
/// 21%/10,5%, con piso mínimo) e IIBB (según padrón del cliente, con piso mínimo) — ver
/// <see cref="Pos.Domain.Services.PercepcionesReglas"/> para las reglas puras. Se usa tanto desde
/// Caja (vista previa en el carrito) como desde Facturación (cálculo definitivo al emitir), para no
/// duplicar la resolución de alícuotas/padrón/mínimos en los dos lados.
/// </summary>
public interface IPercepcionesCalculoService
{
    Task<PercepcionesResultado> CalcularAsync(int idSucursal, int? idCliente,
        IReadOnlyList<LineaParaPercepcion> lineas, CancellationToken ct = default);
}
