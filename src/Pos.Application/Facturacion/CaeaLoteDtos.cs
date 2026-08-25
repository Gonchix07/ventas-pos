namespace Pos.Application.Facturacion;

/// <summary>
/// Un "lote" pendiente de informar a ARCA: todos los comprobantes emitidos bajo el mismo CAEA, en
/// el mismo punto de venta y del mismo tipo (Factura A/B, NC A/B — WSFEv1 exige informarlos por
/// separado, ver AfipWsfeClient.InformarLoteCaeaAsync) que todavía no se subieron.
/// </summary>
public record LoteCaeaPendienteDto(
    int IdSucursal, string SucursalDescripcion, int IdPuntoVenta, int NumeroPuntoVenta,
    int IdTipoComprobante, string TipoDescripcion, string? Letra,
    string Caea, int Cantidad, decimal Total, DateTime FechaDesde, DateTime FechaHasta);

/// <summary>Un comprobante puntual dentro de un lote — para revisar antes de subirlo.</summary>
public record ComprobanteCaeaDto(int IdSucursal, int IdComprobante, string? NumeroCompleto,
    string? Letra, DateTime Fecha, decimal Total, string? ClienteDescripcion);

public record InformarLoteCaeaRequest(int IdSucursal, int IdPuntoVenta, int IdTipoComprobante, string Caea);

public record InformarLoteCaeaResponse(bool Ok, string? Error, int CantidadInformada);

public interface ICaeaLoteService
{
    /// <summary>Todos los lotes CAEA con comprobantes pendientes de informar, agrupados por
    /// sucursal + punto de venta + tipo de comprobante + valor de CAEA.</summary>
    Task<IReadOnlyList<LoteCaeaPendienteDto>> ListarPendientesAsync(CancellationToken ct = default);

    /// <summary>El detalle de comprobantes de un lote puntual (mismos 4 campos que lo identifican
    /// en <see cref="ListarPendientesAsync"/>), para revisar antes de subirlo.</summary>
    Task<IReadOnlyList<ComprobanteCaeaDto>> ListarComprobantesAsync(int idSucursal, int idPuntoVenta,
        int idTipoComprobante, string caea, CancellationToken ct = default);

    /// <summary>Informa el lote completo a ARCA (FECAEARegInformativo) y, si sale bien, marca todos
    /// sus comprobantes como informados — no queda "a medias": o se marcan todos, o ninguno.</summary>
    Task<InformarLoteCaeaResponse> InformarLoteAsync(InformarLoteCaeaRequest req, CancellationToken ct = default);
}
