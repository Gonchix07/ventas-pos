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

public record ArqueoXResponse(int IdSucursal, int IdLote, int IdCaja, string DescripcionCaja, DateTime FechaApertura,
    List<AcumuladoDto> Acumulados, decimal TotalGeneral, string? Referencia,
    List<AnulacionDto> Anulaciones, decimal TotalAnulaciones,
    List<RetiroDto> Retiros, decimal TotalRetiros,
    List<VueltoDto> Vueltos, decimal TotalVueltos);

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
    Task<ArqueoXResponse> ArqueoXAsync(int idSucursal, int idCaja, CancellationToken ct = default);
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
