namespace Pos.Application.Facturacion;

/// <summary>
/// Un pago del cobro. <c>NumeroCupon</c>/<c>NumeroLote</c> son obligatorios cuando el medio es de
/// tipo Tarjeta (quedan guardados para la rendición de cupones); <c>IdBanco</c>/<c>NumeroCheque</c>
/// son obligatorios cuando el medio es Cheque (<c>ObservacionesCheque</c> queda libre, no se exige).
/// Todos se ignoran cuando no corresponden a la fuente del medio.
/// </summary>
// IdPlan: plan de cuotas elegido junto con el medio (solo tiene sentido si es Tarjeta; opcional —
// no todo medio Tarjeta tiene planes cargados, y no se obliga a elegir uno si los hay).
// CodigoGiftcard: obligatorio cuando el medio es de tipo GiftCard (código de 8 caracteres de
// giftcards-app); se ignora en el resto de los medios. TransaccionIdGiftcard: si el canje ya se
// aplicó de forma inmediata desde el popup "Confirmar uso" en Caja (ver CajaController.UsarGiftcard),
// viene con el id de esa transacción — FacturacionService NO vuelve a cobrar, solo la registra.
public record PagoInput(int IdMedioPago, decimal Monto, string? NumeroCupon = null, string? NumeroLote = null,
    int? IdPlan = null, int? IdBanco = null, string? NumeroCheque = null, string? ObservacionesCheque = null,
    string? CodigoGiftcard = null, string? TransaccionIdGiftcard = null);

/// <summary>
/// <c>Modo</c>: Presupuesto (0, comprobante X sin valor fiscal), Electronica (1) o Fiscal (2).
/// <para>El presupuesto exige un único pago en efectivo, un cliente con
/// <c>Cliente.PermitePresupuesto</c> y un punto de venta de tipo Presupuesto — lo valida el
/// servidor, no confía en lo que mande la caja.</para>
/// <para><c>Letra</c> es informativa: la letra REAL la decide el servidor ("X" en Presupuesto; A o B
/// según la condición del cliente frente al IVA en el resto — ver LetraComprobante). Se deja en el
/// contrato para no romper a los clientes existentes, pero si no coincide con la resuelta se ignora.</para>
/// </summary>
public record EmitirComprobanteRequest(
    int IdSucursal, int IdOperacion, int IdPuntoVenta,
    int Modo, string? Letra, List<PagoInput> Pagos);

public record PagoResultadoDto(int IdMedioPago, decimal Monto, bool Aprobado, string? IdTransaccion, string? Error);

/// <summary>Resultado de sumar puntos en puntos-app para esta factura (ver
/// IPuntosFidelizacionService) — null en EmitirComprobanteResponse si el cliente no tenía DNI
/// cargado (ni se intentó). Con Ok=false, Error puede venir null (integración deshabilitada, caso
/// silencioso) o con un motivo (tarjeta no encontrada, comercio inexistente, etc.) — la venta ya se
/// facturó en cualquier caso, esto es solo para el popup de confirmación en caja.</summary>
public record FidelizacionResultDto(bool Ok, string? Cliente, decimal? PuntosOtorgados,
    decimal? PuntosTotales, string? Error);

/// <param name="Total">Neto + Iva + PercepcionIva21 + PercepcionIva105 + PercepcionIibb — lo que
/// efectivamente se cobró.</param>
public record EmitirComprobanteResponse(
    int IdSucursal, int IdComprobante, string NumeroCompleto, string Letra,
    string? Cae, DateTime? CaeVencimiento, bool EsCaea, string Estado,
    decimal Neto, decimal Iva, decimal Total,
    List<PagoResultadoDto> Pagos, bool Impreso, string? ErrorImpresion,
    decimal PercepcionIva21 = 0, decimal PercepcionIva105 = 0, decimal PercepcionIibb = 0,
    /// <summary>Sobrante devuelto en efectivo (0 si no hubo). Ya se registró aparte como una salida
    /// de caja — ver FacturacionService.EmitirAsync — así que se resta sola de la rendición.</summary>
    decimal Vuelto = 0,
    /// <summary>Alícuota (%) con la que se calculó PercepcionIibb (0 si no corresponde).</summary>
    decimal AlicuotaIibb = 0,
    /// <summary>Null si el cliente no tenía DNI cargado (no se intentó sumar puntos) — ver
    /// FidelizacionResultDto.</summary>
    FidelizacionResultDto? Fidelizacion = null);

public record DetalleComprobanteDto(int IdPresentacion, string DescripcionTicket,
    decimal Cantidad, decimal PrecioUnit, decimal Descuento, decimal AlicuotaIva, decimal Importe);

public record ComprobanteDetailDto(
    int IdSucursal, int IdComprobante, string NumeroCompleto, string? Letra, string TipoComprobante,
    DateTime Fecha, int? IdCliente, string? ClienteDescripcion,
    decimal Neto, decimal Iva, decimal Total, string? Cae, DateTime? CaeVencimiento, bool EsCaea,
    string Estado, List<DetalleComprobanteDto> Detalles);

public record ReimpresionResponse(bool Impreso, string? Error);

// ---- Comprobante para imprimir (formatos A y B) ----

/// <summary>Datos fiscales del emisor: empresa + domicilio de la sucursal que emitió.</summary>
public record EmisorComprobanteDto(
    string RazonSocial, string? Cuit, string? CondicionIva,
    string? Domicilio, string? Localidad, string? Provincia, string? CodigoPostal,
    string? IngresosBrutos, DateTime? InicioActividad);

/// <summary>
/// Datos del comprador. En la B alcanza con "Consumidor Final"; en la A van todos completos
/// (razón social, CUIT, domicilio, localidad, provincia y condición frente al IVA).
/// </summary>
public record ClienteComprobanteDto(
    string Descripcion, string? Cuit, string? Documento, string? CondicionIva,
    string? Domicilio, string? Localidad, string? Provincia, string? CodigoPostal);

/// <summary>
/// Línea del comprobante. En la A los importes van NETOS (sin IVA, que se discrimina al pie);
/// en la B van con el IVA incluido, que es el precio que ve el consumidor final.
/// </summary>
public record LineaComprobanteDto(
    string Descripcion, decimal Cantidad, decimal PrecioUnitario, decimal Descuento,
    decimal Importe, decimal Alicuota);

public record IvaDiscriminadoDto(decimal Alicuota, decimal Base, decimal Importe);

public record PagoComprobanteDto(string Descripcion, decimal Monto);

public record ComprobanteImpresionDto(
    int IdSucursal, int IdComprobante,
    string TipoComprobante, string Letra, string? CodigoArca, string NumeroCompleto, DateTime Fecha,
    EmisorComprobanteDto Emisor, ClienteComprobanteDto Cliente,
    List<LineaComprobanteDto> Lineas,
    decimal Descuento, decimal Neto, decimal Iva, decimal Total,
    List<IvaDiscriminadoDto> IvaDiscriminado, List<PagoComprobanteDto> Pagos,
    string? Cae, DateTime? CaeVencimiento, bool EsCaea, string Estado,
    decimal PercepcionIva21 = 0, decimal PercepcionIva105 = 0, decimal PercepcionIibb = 0,
    /// <summary>Alícuota (%) con la que se calculó PercepcionIibb (0 si no corresponde).</summary>
    decimal AlicuotaIibb = 0);

public interface IFacturacionService
{
    /// <summary>
    /// Emite el comprobante para una operación FINALIZADA (Fase 3): reserva número, procesa
    /// pagos, solicita CAE (o CAEA en contingencia), persiste y confirma. Idempotente por
    /// operación: una operación ya facturada no vuelve a emitirse.
    /// </summary>
    Task<EmitirComprobanteResponse> EmitirAsync(EmitirComprobanteRequest req, CancellationToken ct = default);
    Task<ComprobanteDetailDto?> ObtenerAsync(int idSucursal, int idComprobante, CancellationToken ct = default);
    /// <summary>Comprobante armado para imprimir, con emisor, cliente y totales según la letra.</summary>
    Task<ComprobanteImpresionDto?> ObtenerParaImprimirAsync(int idSucursal, int idComprobante, CancellationToken ct = default);
    /// <summary>Letra que le corresponde a la operación (para mostrarla antes de cobrar).</summary>
    Task<string> ResolverLetraAsync(int idSucursal, int idOperacion, CancellationToken ct = default);
    Task<ReimpresionResponse> ReimprimirAsync(int idSucursal, int idComprobante, CancellationToken ct = default);
}
