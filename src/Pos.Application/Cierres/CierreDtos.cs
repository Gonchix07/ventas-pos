namespace Pos.Application.Cierres;

public record AcumuladoDto(int IdMedioPago, string Descripcion, decimal Total, decimal Redondeo);

/// <summary>
/// Una nota de crédito emitida durante el turno. Se lista aparte en la rendición: el importe ya
/// viene descontado de <see cref="AcumuladoDto.Total"/> (la plata salió de verdad de la caja),
/// pero el cajero necesita ver qué anuló y por qué para justificar el faltante de efectivo.
/// </summary>
public record AnulacionDto(int IdComprobante, string NumeroCompleto, string? Letra, DateTime Fecha,
    decimal Total, string? Motivo, string? ComprobanteOrigen);

/// <summary>
/// Un retiro de efectivo del turno (ver RetiroCajaService). Igual que las anulaciones, ya está
/// descontado de <see cref="AcumuladoDto.Total"/> del medio Efectivo — se lista aparte para que el
/// cajero pueda justificar el faltante al rendir.
/// </summary>
public record RetiroDto(int IdMovCaja, DateTime Fecha, decimal Monto, string? Concepto, string? Usuario);

/// <summary>
/// Vuelto entregado en una venta con sobrante en Efectivo (ver FacturacionService.EmitirAsync).
/// Mismo mecanismo que un retiro (movimiento negativo, ya descontado del Efectivo esperado) — se
/// lista aparte para que el cajero pueda justificar el faltante al rendir.
/// </summary>
public record VueltoDto(int IdMovCaja, DateTime Fecha, decimal Monto, string? Concepto, string? Usuario);

/// <summary>Fondo inicial cargado al abrir el turno (ver CajaService.AbrirCajaAsync). A diferencia
/// de retiro/vuelto, suma al esperado en vez de restar.</summary>
public record IngresoDto(int IdMovCaja, DateTime Fecha, int IdMedioPago, decimal Monto, string? Concepto);

/// <summary>
/// Corrección +/- cargada por Tesorería sobre el lote (ver TesoreriaService.CorregirAsync), incluso
/// si ya está cerrado. <see cref="Monto"/> viene con su signo propio (a diferencia de Retiro/Vuelto,
/// que siempre restan): una corrección puede sumar o restar según haga falta.
/// </summary>
public record CorreccionDto(int IdMovCaja, DateTime Fecha, int IdMedioPago, decimal Monto, string? Concepto, string? Usuario);

/// <summary>
/// Cabecera de un comprobante emitido en el lote (ver CierreLoteEjecutor.ComprobantesAsync) — el
/// "ver las ventas hechas en ese lote" del popup de detalle por medio de pago. <c>MontoEnMedio</c>
/// es lo pagado en el medio consultado (si se filtró por uno); si no se filtró, es la suma de TODOS
/// los medios de ese comprobante (equivalente a <see cref="Total"/> salvo redondeos de vuelto).
/// </summary>
public record ComprobanteLoteDto(int IdComprobante, string? NumeroCompleto, string? Letra,
    string TipoDescripcion, DateTime Fecha, decimal Total, decimal MontoEnMedio,
    string? ClienteCodigo, string? ClienteDescripcion);

public record ArqueoXResponse(int IdSucursal, int IdLote, int IdCaja, string DescripcionCaja, DateTime FechaApertura,
    List<AcumuladoDto> Acumulados, decimal TotalGeneral, string? Referencia,
    List<AnulacionDto> Anulaciones, decimal TotalAnulaciones,
    List<RetiroDto> Retiros, decimal TotalRetiros,
    List<VueltoDto> Vueltos, decimal TotalVueltos,
    // Fondo con el que arrancó el turno (ver IngresoDto) — null si se abrió sin fondo. Se agrega acá
    // (no solo en el reporte impreso) para que la rendición del cajero lo pueda mostrar como
    // "saldo inicial" junto con el resto de los movimientos del lote.
    IngresoDto? IngresoInicial,
    // Efectivo acumulado en el lote (dentro de Acumulados/TotalGeneral, no aparte) y el tope
    // configurado (Configuracion.LimiteEfectivoCaja) — para que la pantalla de caja avise al
    // cajero que conviene hacer un retiro. LimiteEfectivoCaja = 0 significa "sin límite cargado".
    decimal EfectivoAcumulado = 0, decimal LimiteEfectivoCaja = 0);

public record DeclaracionPagoInput(int IdMedioPago, decimal MontoDeclarado);

public record CierreTurnoDetalleDto(int IdMedioPago, string Descripcion,
    decimal Esperado, decimal Declarado, decimal Diferencia, bool RequiereMotivo);

public record CerrarTurnoRequest(List<DeclaracionPagoInput> Declaraciones,
    int? IdMotivoDiferencia, string? ObservacionesCajero);

// Sin Referencia/dato fiscal: el cierre de turno es negocio puro (rendición del cajero), separado
// del cierre Z del controlador — ver CierreZFiscalRequest/Response e ICierreZFiscalService más abajo.
public record CerrarTurnoResponse(int IdSucursal, int IdLote, int NumeroCierre, DateTime FechaCierre,
    List<CierreTurnoDetalleDto> Detalle, decimal DiferenciaTotal,
    List<AnulacionDto> Anulaciones, decimal TotalAnulaciones);

public record MotivoDto(int Id, string Descripcion);

/// <summary>Arqueo X (vista del lote abierto) y cierre de turno (irreversible) del cajero, ambos
/// sobre el LOTE — no tocan el controlador fiscal. Ver <see cref="ICierreZFiscalService"/> para el
/// Cierre Z real (reporte del controlador Hasar), que es una operación de máquina aparte.</summary>
public interface ICierreCajaService
{
    /// <param name="imprimir">Si además de calcular los acumulados hay que imprimir el reporte X en
    /// el controlador fiscal. En false cuando el arqueo se pide solo para armar una pantalla (ej. el
    /// preview de "Cerrar turno"), que no necesita disparar una impresión física.</param>
    Task<ArqueoXResponse> ArqueoXAsync(int idSucursal, int idCaja, bool imprimir = true, CancellationToken ct = default);
    Task<CerrarTurnoResponse> CerrarTurnoAsync(int idSucursal, int idCaja, CerrarTurnoRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<MotivoDto>> GetMotivosDiferenciaAsync(CancellationToken ct = default);
}

// ---- Cierre Z del controlador fiscal (Hasar) ----
// Reporte de cierre de jornada fiscal DE LA CAJA FÍSICA: no depende de ningún LoteCaja (puede haber
// cero, uno o varios turnos de distintos cajeros abiertos en esa caja a la vez) y no exige un lote
// propio para ejecutarse — así un supervisor puede dispararlo sin tener que abrir un turno de venta.
// Gateado por código de supervisor (ISupervisorAuthService), no por rol de login: cualquiera que
// tenga el código puede ejecutarlo, igual que anular un artículo o emitir una nota de crédito.
public record CierreZFiscalRequest(string? CodigoSupervisor);
public record CierreZFiscalResponse(int IdSucursal, int IdCaja, DateTime FechaHoraUtc, string? NumeroFiscal);

public interface ICierreZFiscalService
{
    Task<CierreZFiscalResponse> EjecutarAsync(int idSucursal, int idCaja, CierreZFiscalRequest req, CancellationToken ct = default);
}
