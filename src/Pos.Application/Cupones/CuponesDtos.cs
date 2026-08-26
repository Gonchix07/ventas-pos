namespace Pos.Application.Cupones;

/// <summary>
/// Un pago con tarjeta (MovimientoPago cuyo TipoPago.Fuente es Tarjeta), con los datos de cupón
/// para rendir contra el resumen del operador de tarjeta. La fuente de verdad es MovimientoPago —
/// no existe una entidad "Cupon" separada (se descartó: nunca se usaba, ver FASE-Tesorería).
/// </summary>
public record CuponDto(
    long IdMovPagos, int IdSucursal, int IdLote, int IdCaja, DateTime Fecha,
    int IdMedioPago, string MedioDescripcion, decimal Monto,
    string? NumeroCupon, string? NumeroLote,
    int? IdPlanCuota, string? PlanDescripcion, int? CantidadCuotas,
    string? Cajero, int? IdComprobante, string? NumeroComprobante,
    /// <summary>true si alguna vez se corrigió (ver CorreccionCupon) — para resaltarlo en la lista.</summary>
    bool Corregido,
    /// <summary>
    /// true si este pago quedó anulado por una nota de crédito de reversión completa (mismo día,
    /// 100% de la venta, lote todavía abierto — ver NotaCreditoService.EmitirAsync). Ya no
    /// corresponde rendirlo contra el operador de tarjeta.
    /// </summary>
    bool Anulado, DateTime? FechaAnulacion,
    /// <summary>Descripción del tipo de comprobante ("Factura A/B", etc.) — null si el pago quedó
    /// sin comprobante asociado (no debería pasar en la práctica, ver el left join en GetAsync).</summary>
    string? TipoComprobante);

/// <summary>Motivo obligatorio: es una corrección retroactiva sobre el cobro de otra persona.</summary>
public record CorregirCuponInput(string? NumeroCupon, string? NumeroLote, int? IdPlanCuota, string Motivo);

public record CorreccionCuponDto(long IdCorreccionCupon, DateTime Fecha, string? Usuario, string Motivo,
    string? NumeroCuponAnterior, string? NumeroLoteAnterior, int? IdPlanCuotaAnterior,
    string? NumeroCuponNuevo, string? NumeroLoteNuevo, int? IdPlanCuotaNuevo);

public interface ICuponesService
{
    /// <summary>Cupones (pagos con tarjeta) filtrados por vigencia y, opcionalmente, por cajero.</summary>
    Task<IReadOnlyList<CuponDto>> GetAsync(int? idSucursal, DateTime desde, DateTime hasta,
        string? cajero, CancellationToken ct = default);

    Task<CuponDto> CorregirAsync(int idSucursal, long idMovPagos, CorregirCuponInput req,
        CancellationToken ct = default);

    Task<IReadOnlyList<CorreccionCuponDto>> HistorialAsync(int idSucursal, long idMovPagos,
        CancellationToken ct = default);

    /// <summary>Planes de cuotas del medio de pago de ESE cupón puntual, para elegir al corregir.</summary>
    Task<IReadOnlyList<Pos.Application.Caja.PlanCuotaResumen>> GetPlanesAsync(int idSucursal, long idMovPagos,
        CancellationToken ct = default);
}
