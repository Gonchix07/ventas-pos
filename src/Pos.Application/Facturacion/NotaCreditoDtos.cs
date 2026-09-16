using Pos.Domain.Services;

namespace Pos.Application.Facturacion;

/// <summary>
/// Una factura candidata a ser anulada, tal como se lista en el buscador de la caja.
/// <paramref name="SaldoAnulable"/> ya descuenta las notas de crédito previas.
///
/// <paramref name="PercepcionIva21"/>/<paramref name="PercepcionIva105"/>/<paramref name="PercepcionIibb"/>:
/// percepciones de la factura ORIGINAL (ya están adentro de <paramref name="Total"/>, se muestran
/// aparte para que se entienda por qué el saldo anulable no cierra contra la suma de los artículos
/// — viven en la cabecera, no en ninguna línea de detalle). Solo "Anulación total" las acredita
/// (ver NotaCreditoService.EmitirAsync); "Por artículos" y "Por monto" anulan solo lo elegido, sin
/// tocarlas.
/// </summary>
public record ComprobanteAnulableDto(
    int IdSucursal, int IdComprobante, string NumeroCompleto, string? Letra,
    DateTime Fecha, int? IdCliente, string? ClienteDescripcion,
    decimal Total, decimal YaAcreditado, decimal SaldoAnulable, bool Anulable,
    decimal PercepcionIva21 = 0m, decimal PercepcionIva105 = 0m, decimal PercepcionIibb = 0m);

/// <summary>
/// Línea de la factura original, con cuánto de esa línea ya se acreditó en notas de crédito
/// previas. <paramref name="CantidadDisponible"/> es lo que todavía se puede anular (la cantidad
/// completa la primera vez, el resto si ya hubo una anulación parcial); <paramref name="YaAnulada"/>
/// queda en true solo cuando no queda nada disponible.
/// </summary>
public record LineaAnulableDto(
    long IdDetalleComprobante, int IdPresentacion, string DescripcionTicket,
    decimal Cantidad, decimal PrecioUnit, decimal Descuento, decimal AlicuotaIva,
    decimal Importe, decimal CantidadYaAnulada, decimal CantidadDisponible, bool YaAnulada);

/// <summary>Un medio de pago de la factura original, tal como quedó en
/// MovimientosCaja/MovimientosPagos — a diferencia de <see cref="PagoComprobanteDto"/> (el que se
/// imprime en el ticket) éste lleva <paramref name="IdMedioPago"/>, porque acá además sirve para que
/// el cajero elija por dónde devolver el importe de la nota de crédito (ver
/// EmitirNotaCreditoRequest.Devoluciones).</summary>
public record PagoOrigenNcDto(int IdMedioPago, string Descripcion, decimal Monto);

/// <summary><paramref name="Pagos"/>: medios de pago con los que se cobró la factura original — se
/// muestran al pie del popup de Nota de Crédito para que el cajero vea de entrada por dónde entró
/// la plata (sin tener que ir a buscar el ticket original) y, opcionalmente, elija devolver por
/// alguno de esos mismos medios en vez del efectivo por defecto.</summary>
public record ComprobanteAnulableDetalleDto(
    ComprobanteAnulableDto Comprobante, List<LineaAnulableDto> Lineas, List<PagoOrigenNcDto> Pagos);

/// <summary>Una línea elegida en la anulación "Por artículos", con la cantidad puntual a acreditar
/// (de 1 hasta <see cref="LineaAnulableDto.CantidadDisponible"/> de esa línea).</summary>
public record LineaSeleccionNc(long IdDetalle, decimal Cantidad);

/// <summary>Un medio de pago que el cajero eligió para devolver parte (o todo) el importe de la
/// nota de crédito — ver <see cref="EmitirNotaCreditoRequest.Devoluciones"/>.</summary>
public record DevolucionSeleccionadaDto(int IdMedioPago, decimal Monto);

/// <summary>
/// <para><c>Tipo</c>: Total (todo lo que quede con saldo), PorArticulos (las líneas de
/// <c>Lineas</c>, cada una por la cantidad indicada — puede ser parcial) o PorMonto (<c>Monto</c>,
/// prorrateado entre las alícuotas de la factura).</para>
/// <para><paramref name="Devoluciones"/>: por qué medio(s) devolver el importe, elegido por el
/// cajero entre los medios de la factura original (ver ComprobanteAnulableDetalleDto.Pagos). La
/// suma tiene que cerrar EXACTO contra lo que esta NC termina acreditando (se valida en el
/// backend, ver NotaCreditoService.EmitirAsync) — si no, se rechaza. Null o vacío: comportamiento
/// de siempre, todo en Efectivo. Se ignora si termina siendo una reversión completa (ver
/// NotaCreditoResponse.ReversionCompleta): ese caso revierte los medios EXACTOS de la venta
/// original, no admite elegir otra cosa.</para>
/// </summary>
public record EmitirNotaCreditoRequest(
    int IdSucursal, int IdComprobanteOrigen, int IdCaja,
    TipoAnulacion Tipo, List<LineaSeleccionNc>? Lineas, decimal? Monto, string? Motivo,
    List<DevolucionSeleccionadaDto>? Devoluciones = null,
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
