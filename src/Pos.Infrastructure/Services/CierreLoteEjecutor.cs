using Microsoft.EntityFrameworkCore;
using Pos.Application.Cierres;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>Datos de justificación que acompañan al cierre, según quién lo dispara.</summary>
public sealed record CierreLoteJustificacion(
    int? IdMotivoDiferencia,
    string? ObservacionesCajero,
    int? IdMotivoCierre,
    string? ObservacionTesoreria);

public sealed record CierreLoteResultado(int NumeroCierre, DateTime FechaCierre);

/// <summary>
/// Mecánica compartida del cierre de un lote: acumulado por medio de pago, comparación
/// declarado/esperado, numeración del cierre y persistencia bajo lock.
///
/// La usan los dos caminos que cierran un lote — el cierre Z del cajero
/// (<see cref="CierreCajaService"/>) y el cierre administrativo de un lote que quedó pendiente de un
/// día anterior (<see cref="TesoreriaService"/>) — que solo difieren en quién lo dispara, qué
/// justificación se exige y si además se imprime el Z fiscal. Vive acá y no duplicado en cada
/// servicio porque la parte delicada es la numeración de <c>NumeroCierre</c> y el re-chequeo dentro
/// del lock: dos implementaciones podrían divergir y romper la secuencia.
/// </summary>
public class CierreLoteEjecutor
{
    private readonly PosDbContext _db;
    private readonly Pos.Application.Abstractions.ICurrentUser _currentUser;
    public CierreLoteEjecutor(PosDbContext db, Pos.Application.Abstractions.ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<List<AcumuladoDto>> AcumularAsync(int idSucursal, int idLote, CancellationToken ct)
    {
        var movimientos = await (
            from mc in _db.MovimientosCaja.AsNoTracking().Where(m => m.IdSucursal == idSucursal && m.IdLote == idLote)
            join mp in _db.MovimientosPagos.AsNoTracking() on mc.IdMovPagos equals mp.IdMovPagos
            select new MovimientoPagoPlano(mp.IdMedioPago, mp.Total, mp.Redondeo)
        ).ToListAsync(ct);

        var acumulados = AcumuladorPagos.Acumular(movimientos);
        var medios = await DescripcionesMediosAsync(ct);
        return acumulados.Select(a => new AcumuladoDto(a.IdMedioPago,
            medios.GetValueOrDefault(a.IdMedioPago, $"Medio {a.IdMedioPago}"), a.Total, a.Redondeo)).ToList();
    }

    /// <summary>
    /// Notas de crédito emitidas en el lote. Su importe YA está restado del acumulado por medio de
    /// pago (la devolución se registra como un movimiento negativo, así que el efectivo esperado
    /// en el cajón ya lo descuenta); esto es el detalle para que el cajero pueda justificarlo.
    /// </summary>
    public async Task<List<AnulacionDto>> AnulacionesAsync(int idSucursal, int idLote, CancellationToken ct)
    {
        var filas = await (
            from mc in _db.MovimientosCaja.AsNoTracking()
                .Where(m => m.IdSucursal == idSucursal && m.IdLote == idLote && m.IdComprobante != null)
            join c in _db.CabecerasComprobantes.AsNoTracking()
                on new { mc.IdSucursal, IdComprobante = mc.IdComprobante!.Value }
                equals new { c.IdSucursal, c.IdComprobante }
            join t in _db.TiposComprobante.AsNoTracking() on c.IdTipoComprobante equals t.IdTipoComprobante
            where t.Signo == -1
            orderby c.Fecha
            select new
            {
                c.IdComprobante, c.NumeroCompleto, c.Letra, c.Fecha, c.Total,
                c.MotivoAnulacion, c.IdComprobanteOrigen
            }).ToListAsync(ct);

        // El número del comprobante anulado se resuelve aparte: un self-join sobre la misma tabla
        // dentro de la consulta de arriba no aporta y complica la traducción.
        var idsOrigen = filas.Where(f => f.IdComprobanteOrigen != null)
            .Select(f => f.IdComprobanteOrigen!.Value).Distinct().ToList();
        var numerosOrigen = idsOrigen.Count == 0
            ? new Dictionary<int, string>()
            : await _db.CabecerasComprobantes.AsNoTracking()
                .Where(c => c.IdSucursal == idSucursal && idsOrigen.Contains(c.IdComprobante))
                .ToDictionaryAsync(c => c.IdComprobante, c => c.NumeroCompleto ?? "", ct);

        return filas.Select(f => new AnulacionDto(f.IdComprobante, f.NumeroCompleto ?? "", f.Letra,
            f.Fecha, f.Total, f.MotivoAnulacion,
            f.IdComprobanteOrigen is int o ? numerosOrigen.GetValueOrDefault(o) : null)).ToList();
    }

    /// <summary>
    /// Retiros del lote (ver RetiroCajaService), de cualquier medio de pago. Igual que las notas de
    /// crédito, ya están restados del acumulado por medio de pago (se registran como movimiento
    /// negativo); esto es el detalle para que el cajero pueda justificar el faltante. Se identifican
    /// por <see cref="TipoMovimientoManual.Retiro"/> — antes se distinguía por un prefijo de texto en
    /// Concepto, reemplazado para no depender de parsear texto libre.
    /// </summary>
    public async Task<List<RetiroDto>> RetirosAsync(int idSucursal, int idLote, CancellationToken ct)
    {
        var query =
            from mc in _db.MovimientosCaja.AsNoTracking()
                .Where(m => m.IdSucursal == idSucursal && m.IdLote == idLote
                    && m.TipoManual == TipoMovimientoManual.Retiro)
            join mp in _db.MovimientosPagos.AsNoTracking() on mc.IdMovPagos equals mp.IdMovPagos
            join u in _db.Usuarios.AsNoTracking() on mc.IdUsuario equals u.IdUsuario into uj
            from u in uj.DefaultIfEmpty()
            orderby mc.Fecha
            select new RetiroDto(mc.IdMovCaja, mc.Fecha, -mp.Total, mc.Concepto, u != null ? u.NombreUsuario : null);
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Vuelto entregado en ventas del lote (ver FacturacionService.EmitirAsync). Mismo mecanismo que
    /// un retiro (movimiento negativo + <c>IdComprobante == null</c>), identificado por
    /// <see cref="TipoMovimientoManual.Vuelto"/> — ya está restado del acumulado de Efectivo; esto es
    /// el detalle para justificarlo.
    /// </summary>
    public async Task<List<VueltoDto>> VueltosAsync(int idSucursal, int idLote, CancellationToken ct)
    {
        var query =
            from mc in _db.MovimientosCaja.AsNoTracking()
                .Where(m => m.IdSucursal == idSucursal && m.IdLote == idLote
                    && m.TipoManual == TipoMovimientoManual.Vuelto)
            join mp in _db.MovimientosPagos.AsNoTracking() on mc.IdMovPagos equals mp.IdMovPagos
            join u in _db.Usuarios.AsNoTracking() on mc.IdUsuario equals u.IdUsuario into uj
            from u in uj.DefaultIfEmpty()
            orderby mc.Fecha
            select new VueltoDto(mc.IdMovCaja, mc.Fecha, -mp.Total, mc.Concepto, u != null ? u.NombreUsuario : null);
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Fondo inicial cargado al abrir el turno (ver CajaService.AbrirCajaAsync), si lo hubo. Como
    /// mucho hay uno por lote — a diferencia de retiros/correcciones, que pueden repetirse.
    /// </summary>
    public async Task<IngresoDto?> IngresoInicialAsync(int idSucursal, int idLote, CancellationToken ct)
    {
        var query =
            from mc in _db.MovimientosCaja.AsNoTracking()
                .Where(m => m.IdSucursal == idSucursal && m.IdLote == idLote
                    && m.TipoManual == TipoMovimientoManual.Ingreso)
            join mp in _db.MovimientosPagos.AsNoTracking() on mc.IdMovPagos equals mp.IdMovPagos
            select new IngresoDto(mc.IdMovCaja, mc.Fecha, mp.IdMedioPago, mp.Total, mc.Concepto);
        return await query.FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Correcciones +/- cargadas por Tesorería sobre el lote (ver TesoreriaService.CorregirAsync),
    /// incluso si el lote ya está cerrado — a diferencia de un retiro, que solo el cajero carga sobre
    /// su propio lote abierto.
    /// </summary>
    public async Task<List<CorreccionDto>> CorreccionesAsync(int idSucursal, int idLote, CancellationToken ct)
    {
        var query =
            from mc in _db.MovimientosCaja.AsNoTracking()
                .Where(m => m.IdSucursal == idSucursal && m.IdLote == idLote
                    && m.TipoManual == TipoMovimientoManual.CorreccionTesoreria)
            join mp in _db.MovimientosPagos.AsNoTracking() on mc.IdMovPagos equals mp.IdMovPagos
            join u in _db.Usuarios.AsNoTracking() on mc.IdUsuario equals u.IdUsuario into uj
            from u in uj.DefaultIfEmpty()
            orderby mc.Fecha
            select new CorreccionDto(mc.IdMovCaja, mc.Fecha, mp.IdMedioPago, mp.Total, mc.Concepto,
                u != null ? u.NombreUsuario : null);
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Comprobantes (facturas y notas de crédito) emitidos en el lote — "ver las ventas hechas en
    /// ese lote", el popup que se abre al hacer click en un valor por medio de pago. Con
    /// <paramref name="idMedioPago"/> filtra a los comprobantes que tuvieron un pago en ESE medio, y
    /// <see cref="ComprobanteLoteDto.MontoEnMedio"/> es lo pagado ahí (no el total del comprobante,
    /// que puede incluir otros medios si la venta se pagó combinada).
    /// </summary>
    public async Task<List<ComprobanteLoteDto>> ComprobantesAsync(int idSucursal, int idLote,
        int? idMedioPago, CancellationToken ct)
    {
        var pagos = await (
            from mc in _db.MovimientosCaja.AsNoTracking()
                .Where(m => m.IdSucursal == idSucursal && m.IdLote == idLote && m.IdComprobante != null)
            join mp in _db.MovimientosPagos.AsNoTracking() on mc.IdMovPagos equals mp.IdMovPagos
            where idMedioPago == null || mp.IdMedioPago == idMedioPago
            select new { IdComprobante = mc.IdComprobante!.Value, mp.Total }
        ).ToListAsync(ct);
        if (pagos.Count == 0) return new List<ComprobanteLoteDto>();

        var montoPorComprobante = pagos.GroupBy(p => p.IdComprobante)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Total));
        var ids = montoPorComprobante.Keys.ToList();

        var filas = await (
            from c in _db.CabecerasComprobantes.AsNoTracking()
                .Where(c => c.IdSucursal == idSucursal && ids.Contains(c.IdComprobante))
            join t in _db.TiposComprobante.AsNoTracking() on c.IdTipoComprobante equals t.IdTipoComprobante
            join cli in _db.Clientes.AsNoTracking() on c.IdCliente equals cli.IdCliente into clj
            from cli in clj.DefaultIfEmpty()
            orderby c.Fecha
            select new
            {
                c.IdComprobante, c.NumeroCompleto, c.Letra, TipoDescripcion = t.Descripcion,
                c.Fecha, c.Total, ClienteCodigo = cli != null ? cli.CodigoInt : null,
                ClienteDescripcion = cli != null ? cli.Descripcion : null
            }
        ).ToListAsync(ct);

        // El monto por medio sale de un dictionary armado en memoria (arriba): no se puede traducir
        // un lookup de Dictionary dentro del select de la query de EF.
        return filas.Select(f => new ComprobanteLoteDto(f.IdComprobante, f.NumeroCompleto, f.Letra,
            f.TipoDescripcion, f.Fecha, f.Total, montoPorComprobante[f.IdComprobante],
            f.ClienteCodigo, f.ClienteDescripcion)).ToList();
    }

    /// <summary>
    /// Compara lo declarado contra lo esperado, medio por medio. El universo de medios son los que
    /// tuvieron movimiento MÁS los declarados: un medio declarado sin movimientos también arroja
    /// diferencia, y un medio con movimientos que nadie declaró queda declarado en 0.
    /// </summary>
    public async Task<List<CierreTurnoDetalleDto>> ArmarDetalleAsync(
        List<AcumuladoDto> acumulados, IReadOnlyList<DeclaracionPagoInput> declaraciones, CancellationToken ct)
    {
        var medios = await DescripcionesMediosAsync(ct);
        var idsMedios = acumulados.Select(a => a.IdMedioPago)
            .Union(declaraciones.Select(d => d.IdMedioPago)).Distinct().ToList();

        var detalle = new List<CierreTurnoDetalleDto>();
        foreach (var idMedio in idsMedios)
        {
            var esperado = acumulados.FirstOrDefault(a => a.IdMedioPago == idMedio)?.Total ?? 0m;
            var declarado = declaraciones.FirstOrDefault(d => d.IdMedioPago == idMedio)?.MontoDeclarado ?? 0m;
            var eval = DiferenciaCierreReglas.Evaluar(declarado, esperado);
            detalle.Add(new CierreTurnoDetalleDto(idMedio, medios.GetValueOrDefault(idMedio, $"Medio {idMedio}"),
                esperado, declarado, eval.Diferencia, eval.RequiereMotivo));
        }
        return detalle;
    }

    /// <summary>
    /// Persiste el cierre y marca el lote como Cerrado. Los chequeos de negocio (motivo requerido,
    /// quién puede cerrar qué lote) son responsabilidad del llamador: acá solo se re-verifica que el
    /// lote siga abierto, porque eso puede cambiar entre el chequeo y el lock.
    /// </summary>
    public async Task<CierreLoteResultado> CerrarAsync(int idSucursal, int idLote,
        List<CierreTurnoDetalleDto> detalle, List<AcumuladoDto> acumulados,
        CierreLoteJustificacion justificacion, CancellationToken ct)
    {
        // Lock por sucursal: serializa TODOS los cierres de la sucursal (evita tanto el doble-cierre
        // concurrente del mismo lote como la colisión de NumeroCierre entre cajas distintas cerrando
        // al mismo tiempo). Los cierres son infrecuentes, así que serializar por sucursal no es un
        // problema de performance.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await RecursoLockHelper.AdquirirAsync(_db, $"CierreLote:{idSucursal}", ct);

        // Re-chequeo DENTRO de la transacción/lock: el chequeo del llamador se hizo antes de tomar el
        // lock, así que otra request pudo haber cerrado este mismo lote mientras tanto (TOCTOU). Sin
        // esto, el segundo cierre concurrente pisaría el estado sin detectar el conflicto.
        var lote = await _db.LotesCaja.FirstAsync(l => l.IdSucursal == idSucursal && l.IdLote == idLote, ct);
        if (!CierreLoteReglas.PuedeCerrarse(lote.Estado))
            throw new DomainException("LOTE_YA_CERRADO", "El lote ya fue cerrado (el cierre Z es irreversible).");

        var numeroCierre = (await _db.CierresLotesCaja.Where(c => c.IdSucursal == idSucursal)
            .Select(c => (int?)c.NumeroCierre).MaxAsync(ct) ?? 0) + 1;

        foreach (var d in detalle)
        {
            _db.CierresLotesCaja.Add(new CierreLoteCaja
            {
                IdSucursal = idSucursal, IdLote = idLote, IdMedioPago = d.IdMedioPago,
                Total = d.Declarado, NumeroCierre = numeroCierre,
                RedondeoAcumulado = acumulados.FirstOrDefault(a => a.IdMedioPago == d.IdMedioPago)?.Redondeo ?? 0m,
                DiferenciaTotal = d.Diferencia,
                IdMotivoDiferencia = d.RequiereMotivo ? justificacion.IdMotivoDiferencia : null,
                ObservacionesCajero = justificacion.ObservacionesCajero,
                IdMotivoCierre = justificacion.IdMotivoCierre,
                ObservacionTesoreria = justificacion.ObservacionTesoreria,
                // Queda pendiente de validación de tesorería igual que cualquier otro cierre: cerrar
                // el lote y verificar sus números son dos pasos distintos, también cuando el cierre
                // lo dispara Tesorería.
                VerificaTesoreria = false
            });
        }

        lote.Estado = EstadoLote.Cerrado;
        lote.FechaCierre = DateTime.UtcNow;
        // Quién cerró y por qué se graba en el lote: CierresLotesCaja tiene una fila por medio de
        // pago, así que un lote sin movimientos no genera ninguna y el cierre quedaría sin rastro.
        lote.IdUsuarioCierre = _currentUser.IdUsuario;
        lote.IdMotivoCierre = justificacion.IdMotivoCierre;
        lote.ObservacionCierre = justificacion.ObservacionTesoreria;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CierreLoteResultado(numeroCierre, lote.FechaCierre.Value);
    }

    private async Task<Dictionary<int, string>> DescripcionesMediosAsync(CancellationToken ct) =>
        await _db.MediosPago.AsNoTracking().ToDictionaryAsync(m => m.IdMedioPago, m => m.Descripcion, ct);
}
