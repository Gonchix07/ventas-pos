namespace Pos.Application.Facturacion;

/// <summary>
/// Resultado de la búsqueda de comprobantes para reimprimir. Misma UX de búsqueda que Nota de
/// Crédito (número, cliente o CUIT + rango de fechas — ver INotaCreditoService.BuscarAsync), pero
/// sin filtrar por signo: acá interesan tanto facturas como notas de crédito ya emitidas, y no
/// aplica ningún cálculo de saldo anulable.
/// </summary>
public record ComprobanteReimpresionDto(int IdSucursal, int IdComprobante, string NumeroCompleto,
    string? Letra, string TipoComprobante, DateTime Fecha, int? IdCliente, string? ClienteDescripcion,
    decimal Total, string Estado);

/// <summary>
/// Búsqueda de comprobantes ya emitidos para reimprimir (Supervisor/Tesorero/Administrador). La
/// reimpresión en sí reusa <see cref="IFacturacionService.ObtenerParaImprimirAsync"/> — mismo armado
/// que la vista que ya se muestra justo después de emitir, no reemite ni reabre nada fiscal.
/// </summary>
public interface IReimpresionService
{
    Task<IReadOnlyList<ComprobanteReimpresionDto>> BuscarAsync(int idSucursal, string texto,
        DateTime? desde, DateTime? hasta, CancellationToken ct = default);
}
