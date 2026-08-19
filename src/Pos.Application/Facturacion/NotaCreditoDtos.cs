using Pos.Domain.Services;

namespace Pos.Application.Facturacion;

/// <summary>
/// Una factura candidata a ser anulada, tal como se lista en el buscador de la caja.
/// <paramref name="SaldoAnulable"/> ya descuenta las notas de crédito previas.
/// </summary>
public record ComprobanteAnulableDto(
    int IdSucursal, int IdComprobante, string NumeroCompleto, string? Letra,
    DateTime Fecha, int? IdCliente, string? ClienteDescripcion,
    decimal Total, decimal YaAcreditado, decimal SaldoAnulable, bool Anulable);

/// <summary>Línea de la factura original, con el dato de si ya fue acreditada.</summary>
public record LineaAnulableDto(
    long IdDetalleComprobante, int IdPresentacion, string DescripcionTicket,
    decimal Cantidad, decimal PrecioUnit, decimal Descuento, decimal AlicuotaIva,
    decimal Importe, bool YaAnulada);

public record ComprobanteAnulableDetalleDto(
    ComprobanteAnulableDto Comprobante, List<LineaAnulableDto> Lineas);

/// <summary>
/// <para><c>Tipo</c>: Total (todo lo que quede con saldo), PorArticulos (las líneas de
/// <c>IdsDetalle</c>, siempre completas) o PorMonto (<c>Monto</c>, prorrateado entre las
/// alícuotas de la factura).</para>
/// <para>La devolución se hace siempre en efectivo por ahora, así que no viaja medio de pago.</para>
/// </summary>
public record EmitirNotaCreditoRequest(
    int IdSucursal, int IdComprobanteOrigen, int IdCaja,
    TipoAnulacion Tipo, List<long>? IdsDetalle, decimal? Monto, string? Motivo,
    // Null si quien emite ya es Supervisor/Administrador (ver ISupervisorAuthService).
    string? CodigoSupervisor = null);

/// <summary>Lo devuelto en UN medio de pago concreto (ver NotaCreditoResponse.Devoluciones).</summary>
public record DevolucionMedioDto(int IdMedioPago, string MedioDescripcion, decimal Monto);

public record NotaCreditoResponse(
    int IdSucursal, int IdComprobante, string NumeroCompleto, string Letra,
    string? Cae, DateTime? CaeVencimiento, bool EsCaea, string Estado,
    decimal Neto, decimal Iva, decimal Total, decimal DevueltoEnEfectivo,
    bool Impreso, string? ErrorImpresion,
    /// <summary>
    /// Desglose real de por dónde salió la plata. En el caso general tiene una sola fila
    /// (Efectivo, igual a <see cref="DevueltoEnEfectivo"/>); en una reversión completa (ver
    /// <see cref="ReversionCompleta"/>) tiene una fila por cada medio de la venta original.
    /// </summary>
    List<DevolucionMedioDto> Devoluciones,
    /// <summary>
    /// true si esta NC revirtió TODOS los medios de pago originales (venta 100% acreditada, mismo
    /// día, lote de la venta todavía abierto) en vez de devolver un monto genérico en efectivo.
    /// </summary>
    bool ReversionCompleta);

public interface INotaCreditoService
{
    /// <summary>Busca facturas anulables de la sucursal por número, cliente o CUIT.</summary>
    Task<IReadOnlyList<ComprobanteAnulableDto>> BuscarAsync(int idSucursal, string texto,
        DateTime? desde, DateTime? hasta, CancellationToken ct = default);

    Task<ComprobanteAnulableDetalleDto> ObtenerAsync(int idSucursal, int idComprobante, CancellationToken ct = default);

    Task<NotaCreditoResponse> EmitirAsync(EmitirNotaCreditoRequest req, CancellationToken ct = default);
}
