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

/// <summary>
/// Corrección +/- que carga Tesorería sobre un lote — de cualquier medio de pago y aunque el lote
/// ya esté cerrado (a diferencia del retiro del cajero, que solo opera sobre su propio lote
/// abierto). El motivo es obligatorio: es un ajuste manual sobre la rendición de otra persona.
/// </summary>
public record CorreccionManualInput(int IdMedioPago, decimal Monto, string Concepto);

/// <summary>Lookup de medios de pago para el popup de "Entrega de valores" — Tesorería no tiene
/// acceso al ABM de medios (rol Administrador) ni a /caja/medios-pago (rol Cajero), así que
/// necesita su propia vía de solo lectura.</summary>
public record MedioPagoLookupDto(int Id, string Descripcion);

/// <summary>
/// Una fila de la vista principal de Tesorería: un lote (abierto o cerrado) dentro de la vigencia
/// consultada. <c>EstadoCierre</c> es un estado calculado (no persistido, ver GetLotesAsync):
/// "Abierto" | "CierreCajero" | "CierreTesoreria" — distinto de <c>EstadoLote</c> (que solo tiene
/// Abierto/Cerrado en el esquema), se resuelve mirando si TODAS las filas de CierreLoteCaja del lote
/// tienen VerificaTesoreria=true.
/// </summary>
public record LoteResumenDto(
    int IdSucursal, string? SucursalDescripcion, int IdLote, int IdCaja, string CajaDescripcion,
    string? Usuario, DateTime FechaApertura, DateTime? FechaCierre,
    string EstadoLote, string EstadoCierre,
    /// <summary>Fondo con el que abrió el turno (0 si no cargó ninguno).</summary>
    decimal SaldoInicial,
    /// <summary>
    /// Rendición NETA del turno a este momento: ventas + notas de crédito + retiros + vueltos +
    /// correcciones — SIN el saldo inicial (ese se ve aparte en <see cref="SaldoInicial"/>).
    /// </summary>
    decimal RendicionTotal,
    /// <summary>Total de vuelto entregado en efectivo durante el turno.</summary>
    decimal CambioAcumulado,
    /// <summary>
    /// SaldoInicial + RendicionTotal: lo que el sistema espera que haya en la caja a este momento
    /// (todos los medios). Se calcula siempre, esté el lote abierto o cerrado.
    /// </summary>
    decimal SaldoEsperado,
    /// <summary>Suma de lo declarado por el cajero al cerrar (todos los medios). Null si el lote sigue Abierto.</summary>
    decimal? Saldo);

/// <summary>
/// Detalle de rendición de UN lote (la subfila al hacer click en GetLotesAsync). Si el lote sigue
/// Abierto, <c>Declarado</c> viene vacío (todavía no declaró nada — solo hay <c>Acumulados</c>, el
/// esperado). Si está Cerrado, el <c>Declarado</c> de cada fila es una foto fija de lo que dijo el
/// cajero al cerrar (no cambia más), pero el <c>Esperado</c> y la <c>Diferencia</c> de esa misma
/// fila se recalculan contra el estado ACTUAL (si Tesorería cargó una corrección después, ya la
/// reflejan) — mostrar un esperado actualizado junto a una diferencia congelada del momento del
/// cierre sería inconsistente.
/// </summary>
public record LoteDetalleDto(
    int IdSucursal, int IdLote,
    List<Pos.Application.Cierres.AcumuladoDto> Acumulados,
    List<Pos.Application.Cierres.CierreTurnoDetalleDto> Declarado,
    Pos.Application.Cierres.IngresoDto? IngresoInicial,
    List<Pos.Application.Cierres.RetiroDto> Retiros,
    List<Pos.Application.Cierres.VueltoDto> Vueltos,
    List<Pos.Application.Cierres.CorreccionDto> Correcciones,
    List<Pos.Application.Cierres.AnulacionDto> Anulaciones,
    /// <summary>
    /// Motivo del cierre, si lo tuvo (ver LoteCaja.IdMotivoCierre): en el cierre Z normal del cajero
    /// queda null — solo se exige en un cierre administrativo de Tesorería sobre un lote pendiente.
    /// </summary>
    string? MotivoCierreDescripcion,
    /// <summary>Texto libre que dejó el cajero al cerrar (CierreLoteCaja.ObservacionesCajero).</summary>
    string? ObservacionesCajero);

public interface ITesoreriaService
{
    Task<DashboardResponse> GetDashboardAsync(int? idSucursal, CancellationToken ct = default);
    Task<IReadOnlyList<CierreListItemDto>> GetCierresAsync(int? idSucursal, string? cajero, CancellationToken ct = default);
    Task<bool> ValidarCierreAsync(int idSucursal, int idLote, ValidarCierreRequest req, CancellationToken ct = default);

    /// <summary>
    /// Deshace la validación de Tesorería sobre un lote ya validado (vuelve a "Pendiente" —
    /// <c>CierreCajero</c>). No reabre el turno de caja en sí (el lote sigue Cerrado, el cajero no
    /// puede volver a operarlo): solo permite corregir/revalidar desde Tesorería sin tener que pasar
    /// por soporte. Devuelve false si el lote no existe o no está actualmente validado.
    /// </summary>
    Task<bool> ReabrirCierreAsync(int idSucursal, int idLote, CancellationToken ct = default);
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

    /// <summary>Corrección manual +/- de Tesorería sobre un lote (cualquier medio, cualquier estado).</summary>
    Task<Pos.Application.Cierres.CorreccionDto> CorregirAsync(int idSucursal, int idLote,
        CorreccionManualInput req, CancellationToken ct = default);

    /// <summary>Lookup de medios de pago para el popup de "Entrega de valores".</summary>
    Task<IReadOnlyList<MedioPagoLookupDto>> GetMediosPagoAsync(CancellationToken ct = default);

    /// <summary>Vista principal: lotes (abiertos y cerrados) cuya apertura cae dentro de [desde, hasta].</summary>
    Task<IReadOnlyList<LoteResumenDto>> GetLotesAsync(int? idSucursal, DateTime desde, DateTime hasta,
        CancellationToken ct = default);

    /// <summary>Detalle de rendición de un lote puntual (la subfila al expandir una fila de GetLotesAsync).</summary>
    Task<LoteDetalleDto> GetDetalleLoteAsync(int idSucursal, int idLote, CancellationToken ct = default);

    /// <summary>
    /// Comprobantes del lote (el popup al hacer click en un valor por medio de pago). Sin
    /// <paramref name="idMedioPago"/> trae todos los comprobantes del lote.
    /// </summary>
    Task<IReadOnlyList<Pos.Application.Cierres.ComprobanteLoteDto>> GetComprobantesLoteAsync(
        int idSucursal, int idLote, int? idMedioPago, CancellationToken ct = default);
}
