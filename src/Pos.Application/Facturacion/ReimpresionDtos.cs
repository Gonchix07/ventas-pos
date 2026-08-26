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
/// Resultado de búsqueda de rendiciones (cierres de turno de cajero) para reimprimir — mismo PDF
/// que ya se genera al cerrar el turno en Caja (ver RendicionPdf.tsx en el frontend), solo que acá
/// se reconstruye para un lote YA CERRADO cualquiera, no el propio del cajero logueado hoy.
/// </summary>
public record RendicionReimpresionDto(int IdSucursal, int IdLote, int IdCaja, string DescripcionCaja,
    string? Cajero, DateTime FechaCierre, int? NumeroCierre, decimal Total);

/// <summary>
/// Rendición armada para reimprimir: mismo shape (<see cref="Pos.Application.Cierres.ArqueoXResponse"/>
/// + <see cref="Pos.Application.Cierres.CerrarTurnoResponse"/>) que ya arma Caja al cerrar el turno
/// — el frontend reusa el mismo componente de PDF sin necesitar un armado paralelo.
/// </summary>
public record RendicionImpresionDto(
    Pos.Application.Cierres.ArqueoXResponse Arqueo, Pos.Application.Cierres.CerrarTurnoResponse Cierre,
    string Usuario, string? MotivoDescripcion, string? Observaciones);

/// <summary>
/// Búsqueda de comprobantes ya emitidos para reimprimir (Supervisor/Tesorero/Administrador). La
/// reimpresión en sí reusa <see cref="IFacturacionService.ObtenerParaImprimirAsync"/> — mismo armado
/// que la vista que ya se muestra justo después de emitir, no reemite ni reabre nada fiscal.
/// </summary>
public interface IReimpresionService
{
    /// <param name="tipo">Filtro opcional: "Factura", "NotaCredito" o "Presupuesto". Null/vacío = todos.</param>
    Task<IReadOnlyList<ComprobanteReimpresionDto>> BuscarAsync(int idSucursal, string texto,
        DateTime? desde, DateTime? hasta, string? tipo = null, CancellationToken ct = default);

    /// <summary>Busca lotes CERRADOS por número o cajero, dentro de la vigencia elegida.</summary>
    Task<IReadOnlyList<RendicionReimpresionDto>> BuscarRendicionesAsync(int idSucursal, string texto,
        DateTime? desde, DateTime? hasta, CancellationToken ct = default);

    /// <summary>Arma la rendición de un lote puntual para reimprimir. Null si no existe o no está cerrado.</summary>
    Task<RendicionImpresionDto?> ObtenerRendicionAsync(int idSucursal, int idLote, CancellationToken ct = default);
}
