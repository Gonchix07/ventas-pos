namespace Pos.Application.Tesoreria;

public record CajaResumenDto(int IdSucursal, string? SucursalDescripcion, int IdCaja, string CajaDescripcion,
    string Estado, int? IdLote, string? Cajero, DateTime? FechaApertura, DateTime? FechaCierre, decimal? TotalLote);

public record DashboardResponse(List<CajaResumenDto> Cajas, decimal AcumuladoGeneral,
    List<Pos.Application.Cierres.AcumuladoDto> AcumuladoPorMedio);

public record CierreListItemDto(int IdSucursal, int IdLote, int IdCaja, string? Cajero,
    int IdMedioPago, string MedioDescripcion, decimal Total, decimal DiferenciaTotal,
    int? IdMotivoDiferencia, string? ObservacionesCajero, bool VerificaTesoreria, DateTime? FechaCierre);

public record ValidarCierreRequest(int? IdMotivoCierre, string? ObservacionTesoreria);
public record MotivoCierreDto(int Id, string Descripcion);

/// <summary>
/// Lote que quedó Abierto en un día anterior y que por lo tanto ya no puede cerrar su cajero (arqueo
/// X y cierre Z solo operan sobre el lote de hoy). Incluye el acumulado esperado por medio de pago
/// para que Tesorería vea contra qué está declarando.
/// </summary>
public record LotePendienteDto(int IdSucursal, string? SucursalDescripcion, int IdLote, int IdCaja,
    string CajaDescripcion, string? Cajero, DateTime FechaApertura, int DiasPendiente,
    List<Pos.Application.Cierres.AcumuladoDto> Acumulados, decimal TotalEsperado);

/// <summary>
/// A diferencia del cierre Z del cajero, el motivo de cierre es SIEMPRE obligatorio: regularizar el
/// lote de otro usuario, días después, siempre necesita quedar justificado. Las declaraciones son
/// opcionales — un medio no declarado se toma como 0, lo que arroja diferencia y entonces exige
/// además <see cref="IdMotivoDiferencia"/>.
/// </summary>
public record CerrarLotePendienteRequest(List<Pos.Application.Cierres.DeclaracionPagoInput> Declaraciones,
    int? IdMotivoDiferencia, int IdMotivoCierre, string? ObservacionTesoreria);

public interface ITesoreriaService
{
    Task<DashboardResponse> GetDashboardAsync(int? idSucursal, CancellationToken ct = default);
    Task<IReadOnlyList<CierreListItemDto>> GetCierresAsync(int? idSucursal, string? cajero, CancellationToken ct = default);
    Task<bool> ValidarCierreAsync(int idSucursal, int idLote, ValidarCierreRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<MotivoCierreDto>> GetMotivosCierreAsync(CancellationToken ct = default);

    /// <summary>
    /// Mismo lookup que expone Caja, pero accesible al rol Tesorero: al cerrar un lote pendiente hay
    /// que justificar la diferencia igual que en un cierre Z, y <c>/caja/motivos-diferencia</c> solo
    /// admite Cajero/Supervisor/Administrador.
    /// </summary>
    Task<IReadOnlyList<Pos.Application.Cierres.MotivoDto>> GetMotivosDiferenciaAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LotePendienteDto>> GetLotesPendientesAsync(int? idSucursal, CancellationToken ct = default);
    Task<Pos.Application.Cierres.CerrarTurnoResponse> CerrarLotePendienteAsync(int idSucursal, int idLote,
        CerrarLotePendienteRequest req, CancellationToken ct = default);
}
