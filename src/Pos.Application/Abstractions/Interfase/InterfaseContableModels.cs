namespace Pos.Application.Abstractions.Interfase;

/// <summary>
/// Una fila lista para insertar en la tabla <c>ivavtas</c> de la base MySQL "interfase" (sistema
/// contable externo). Los datos ya vienen resueltos/formateados en el punto de emisión — este
/// registro es un espejo casi literal de las columnas de la tabla, ver docs de la integración.
/// </summary>
public record IvaVtaInterfase(
    DateTime Fecha,
    string? Cliente,
    string? Nombre,
    int? CondIva,
    string? Cuit,
    string Tipo,
    string Prenum,
    string Numero,
    decimal Neto,
    decimal Iva,
    decimal IvaAdic,
    decimal Exento,
    // Periva: confirmado con el usuario (2026-08-21) que lleva la misma suma que IvaAdic
    // (percepción IVA 21% + 10,5%) — son dos parámetros separados porque son columnas distintas
    // en la tabla, pero siempre con el mismo valor.
    decimal Periva,
    decimal Final,
    decimal BaseImp,
    decimal ImpPerc,
    decimal PorcIibb1,
    string Prov,
    string Empresa,
    long IdVentaSalon);

/// <summary>
/// Una fila lista para insertar en la tabla <c>movstock</c> (movimiento de stock por línea de
/// artículo vendido). Igual criterio que <see cref="IvaVtaInterfase"/>: espejo casi literal de las
/// columnas, ya resuelto/formateado en el punto de emisión.
/// </summary>
public record MovStockInterfase(
    DateTime Fecha,
    string Articulo,
    decimal Salida,
    decimal Descto,
    decimal Unitario,
    decimal Pesos,
    string DeDeposito,
    string? Cliente,
    string? Nombre,
    string Tipo,
    string Numero,
    string? Vendedor,
    string Lista,
    decimal ImpInt,
    string Reparto,
    int ModoFact,
    string? CodConv,
    string Prov,
    string Empresa,
    long IdVentaSalon,
    decimal Iva,
    decimal Periva);

/// <summary>
/// Una fila lista para insertar en la tabla <c>ctacte</c> (movimiento de cuenta corriente). Solo
/// corresponde escribirla cuando la venta/NC realmente mueve cuenta corriente — confirmado con el
/// usuario (2026-08-21): no se manda una fila "en 0" por cada Factura/NC que no la usó.
/// <paramref name="Estado"/> siempre null (confirmado: no lo completa pos-mayorista).
/// </summary>
public record CtaCteInterfase(
    DateTime Fecha,
    string Tipo,
    string Prenum,
    string Numero,
    decimal Debe,
    decimal Haber,
    string? Cliente,
    string Prov,
    string Empresa,
    long IdVentaSalon);

/// <summary>
/// Una fila lista para insertar en la tabla <c>comision</c> — muy similar a
/// <see cref="IvaVtaInterfase"/> (cliente/tipo/prenum/numero/neto/final/prov/empresa comparten el
/// mismo mapeo), con tres columnas propias: <paramref name="Vendedor"/> (siempre null, pos-mayorista
/// no tiene ese concepto separado del cajero), <paramref name="CondVta"/> ("01" normal / "02" cuenta
/// corriente) y <paramref name="Reparto"/> (número de operación, mismo criterio que movstock).
/// </summary>
public record ComisionInterfase(
    DateTime Fecha,
    string? Cliente,
    string Tipo,
    string Prenum,
    string Numero,
    decimal Neto,
    decimal Final,
    string? Vendedor,
    string CondVta,
    string Reparto,
    string Prov,
    string Empresa,
    long IdVentaSalon,
    string Hora);

/// <summary>
/// Una fila lista para insertar en la tabla <c>cupones</c>. Solo corresponde una fila por cada pago
/// con tarjeta (Fuente Tarjeta) de la venta — el resto de los medios no generan cupón.
/// </summary>
public record CuponInterfase(
    string? Numero,
    string? Tarjeta,
    string? Plan,
    decimal Importe,
    DateTime FechaRec,
    string? CodCli,
    string? NomCli,
    string Caja,
    string Cajero,
    string Operacion,
    long IdVentaSalon);

/// <summary>
/// Una fila lista para insertar en <c>cja_movi</c> Y en <c>tmp_cja</c> (mismo evento, dos tablas muy
/// similares — ver <see cref="Pos.Infrastructure.Services.InterfaseContableService.RegistrarCierreCajaAsync"/>)
/// — se manda UNA VEZ por cierre de turno del cajero o por retiro de efectivo, no por comprobante.
/// <c>Tipo</c>="I" y <c>Rubro</c>="15418" (solo cja_movi) son fijos (ver
/// <see cref="Pos.Domain.Services.InterfaseContableReglas"/>); <c>Dolares</c> y <c>Documentos</c>
/// (solo cja_movi) siempre 0. <paramref name="IdVentaSalon"/> es solo para tmp_cja.tmp_cja: el
/// número de cierre en la fila de cierre de turno, null en la de retiro (confirmado con el usuario).
/// </summary>
public record CjaMoviInterfase(
    DateTime Fecha,
    string Detalle,
    decimal Efectivo,
    decimal Cheques,
    decimal Tarjetas,
    decimal Otros,
    string NroCaja,
    string Cajero,
    string Empresa,
    long? IdVentaSalon = null);

/// <summary>
/// Puerto hacia el sistema contable externo (base MySQL "interfase", configurable en
/// Administración → Conexión externa). Best-effort a propósito: un corte de red o la base
/// deshabilitada NUNCA debe impedir emitir una venta — ver <see cref="Pos.Infrastructure.Services.InterfaseContableService"/>.
/// Todavía cubre <c>ivavtas</c>, <c>movstock</c>, <c>ctacte</c>, <c>comision</c>, <c>cupones</c>,
/// <c>cja_movi</c> y <c>tmp_cja</c>; solo queda pendiente <c>newcheques</c>.
/// </summary>
public interface IInterfaseContableService
{
    Task RegistrarVentaAsync(IvaVtaInterfase fila, CancellationToken ct = default);

    /// <summary>Una fila por línea del comprobante (se manda toda la venta en una sola llamada).</summary>
    Task RegistrarMovStockAsync(IReadOnlyList<MovStockInterfase> filas, CancellationToken ct = default);

    Task RegistrarCtaCteAsync(CtaCteInterfase fila, CancellationToken ct = default);

    Task RegistrarComisionAsync(ComisionInterfase fila, CancellationToken ct = default);

    Task RegistrarCuponAsync(CuponInterfase fila, CancellationToken ct = default);

    Task RegistrarCierreCajaAsync(CjaMoviInterfase fila, CancellationToken ct = default);
}
