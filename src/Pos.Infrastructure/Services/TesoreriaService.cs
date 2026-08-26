using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Cierres;
using Pos.Application.Common;
using Pos.Application.Tesoreria;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>Dashboard de tesorería: vista de cajas, acumulados y validación de cierres.</summary>
public class TesoreriaService : ITesoreriaService
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly CierreLoteEjecutor _ejecutor;
    public TesoreriaService(PosDbContext db, ICurrentUser currentUser, CierreLoteEjecutor ejecutor)
    {
        _db = db;
        _currentUser = currentUser;
        _ejecutor = ejecutor;
    }

    public async Task<DashboardResponse> GetDashboardAsync(int? idSucursal, CancellationToken ct = default)
    {
        idSucursal = _currentUser.AplicarAlcanceSucursal(idSucursal);
        var cajasQuery = _db.Cajas.AsNoTracking().AsQueryable();
        if (idSucursal.HasValue) cajasQuery = cajasQuery.Where(c => c.IdSucursal == idSucursal.Value);
        var cajas = await cajasQuery.ToListAsync(ct);

        var sucursales = await _db.Sucursales.AsNoTracking().ToDictionaryAsync(s => s.IdSucursal, s => s.Descripcion, ct);
        var usuarios = await _db.Usuarios.AsNoTracking().ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario, ct);

        var resumenes = new List<CajaResumenDto>();
        var idsLotesDelDia = new List<(int IdSucursal, int IdLote)>();

        foreach (var c in cajas)
        {
            // Una caja física puede tener varios lotes abiertos a la vez (uno por cajero): se
            // listan TODOS los abiertos hoy, no solo "el" lote de la caja (eso era el bug — un
            // cajero nuevo podía terminar operando el lote de otro). Si no hay ninguno abierto,
            // se muestra el último lote (abierto o cerrado) para no perder el estado de una caja
            // inactiva, igual que antes.
            var abiertosHoy = await _db.LotesCaja.AsNoTracking()
                .Where(l => l.IdSucursal == c.IdSucursal && l.IdCaja == c.IdCaja
                         && l.Estado == EstadoLote.Abierto && l.FechaApertura.Date == DateTime.UtcNow.Date)
                .OrderBy(l => l.FechaApertura).ToListAsync(ct);

            var lotesAMostrar = abiertosHoy;
            if (lotesAMostrar.Count == 0)
            {
                var ultimo = await _db.LotesCaja.AsNoTracking()
                    .Where(l => l.IdSucursal == c.IdSucursal && l.IdCaja == c.IdCaja)
                    .OrderByDescending(l => l.FechaApertura).FirstOrDefaultAsync(ct);
                if (ultimo is not null) lotesAMostrar = new List<LoteCaja> { ultimo };
            }

            if (lotesAMostrar.Count == 0)
            {
                resumenes.Add(new CajaResumenDto(c.IdSucursal, sucursales.GetValueOrDefault(c.IdSucursal), c.IdCaja,
                    c.Descripcion, "SinLote", null, null, null, null, null));
                continue;
            }

            foreach (var lote in lotesAMostrar)
            {
                var totalLote = await SumarMovimientosAsync(c.IdSucursal, lote.IdLote, ct);
                if (lote.FechaApertura.Date == DateTime.UtcNow.Date)
                    idsLotesDelDia.Add((c.IdSucursal, lote.IdLote));

                resumenes.Add(new CajaResumenDto(c.IdSucursal, sucursales.GetValueOrDefault(c.IdSucursal), c.IdCaja,
                    c.Descripcion, lote.Estado.ToString(), lote.IdLote,
                    usuarios.GetValueOrDefault(lote.IdUsuarioApertura), lote.FechaApertura, lote.FechaCierre, totalLote));
            }
        }

        var movimientosHoy = new List<MovimientoPagoPlano>();
        foreach (var (suc, idLote) in idsLotesDelDia)
            movimientosHoy.AddRange(await ObtenerMovimientosAsync(suc, idLote, ct));

        var acumuladoPorMedio = await MapearAcumuladosAsync(AcumuladorPagos.Acumular(movimientosHoy), ct);

        return new DashboardResponse(resumenes, acumuladoPorMedio.Sum(a => a.Total), acumuladoPorMedio);
    }

    public async Task<IReadOnlyList<CierreListItemDto>> GetCierresAsync(int? idSucursal, string? cajero, CancellationToken ct = default)
    {
        idSucursal = _currentUser.AplicarAlcanceSucursal(idSucursal);
        var query =
            from cl in _db.CierresLotesCaja.AsNoTracking()
            join lc in _db.LotesCaja.AsNoTracking() on new { cl.IdSucursal, cl.IdLote } equals new { lc.IdSucursal, lc.IdLote }
            join mp in _db.MediosPago.AsNoTracking() on cl.IdMedioPago equals mp.IdMedioPago
            join u in _db.Usuarios.AsNoTracking() on lc.IdUsuarioApertura equals u.IdUsuario into uj
            from u in uj.DefaultIfEmpty()
            select new { cl, lc, mp, Cajero = u != null ? u.NombreUsuario : null };

        if (idSucursal.HasValue) query = query.Where(x => x.cl.IdSucursal == idSucursal.Value);
        if (!string.IsNullOrWhiteSpace(cajero)) query = query.Where(x => x.Cajero == cajero);

        var rows = await query.OrderByDescending(x => x.lc.FechaCierre).ToListAsync(ct);

        return rows.Select(x => new CierreListItemDto(x.cl.IdSucursal, x.cl.IdLote, x.lc.IdCaja, x.Cajero,
            x.cl.IdMedioPago, x.mp.Descripcion, x.cl.Total, x.cl.DiferenciaTotal, x.cl.IdMotivoDiferencia,
            x.cl.ObservacionesCajero, x.cl.VerificaTesoreria, x.lc.FechaCierre)).ToList();
    }

    public async Task<bool> ValidarCierreAsync(int idSucursal, int idLote, ValidarCierreRequest req, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var filas = await _db.CierresLotesCaja
            .Where(c => c.IdSucursal == idSucursal && c.IdLote == idLote).ToListAsync(ct);

        if (filas.Count == 0)
        {
            // Lote cerrado sin movimientos: no generó ninguna fila en CierresLotesCaja (nada que
            // reconciliar por medio de pago), así que el visto bueno de Tesorería se marca en el
            // propio lote — sin esto, un turno vacío nunca podía "validarse" (404 permanente).
            var lote = await _db.LotesCaja
                .FirstOrDefaultAsync(l => l.IdSucursal == idSucursal && l.IdLote == idLote, ct);
            if (lote is not { Estado: EstadoLote.Cerrado }) return false;

            lote.VerificadoTesoreriaVacio = true;
            lote.IdMotivoCierre ??= req.IdMotivoCierre;
            lote.ObservacionCierre ??= req.ObservacionTesoreria;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        foreach (var f in filas)
        {
            f.VerificaTesoreria = true;
            f.IdMotivoCierre = req.IdMotivoCierre;
            f.ObservacionTesoreria = req.ObservacionTesoreria;
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReabrirCierreAsync(int idSucursal, int idLote, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var filas = await _db.CierresLotesCaja
            .Where(c => c.IdSucursal == idSucursal && c.IdLote == idLote).ToListAsync(ct);

        if (filas.Count == 0)
        {
            // Mismo caso especial que ValidarCierreAsync: lote sin movimientos, el visto bueno vive
            // en el lote mismo (VerificadoTesoreriaVacio), no hay filas de CierresLotesCaja.
            var lote = await _db.LotesCaja
                .FirstOrDefaultAsync(l => l.IdSucursal == idSucursal && l.IdLote == idLote, ct);
            if (lote is null || !lote.VerificadoTesoreriaVacio) return false;

            lote.VerificadoTesoreriaVacio = false;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        if (!filas.All(f => f.VerificaTesoreria)) return false; // no estaba validado del todo
        foreach (var f in filas) f.VerificaTesoreria = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---------- Lotes pendientes de días anteriores ----------

    public async Task<IReadOnlyList<MotivoDto>> GetMotivosDiferenciaAsync(CancellationToken ct = default) =>
        await _db.MotivosDiferencia.AsNoTracking().OrderBy(m => m.Descripcion)
            .Select(m => new MotivoDto(m.IdMotivoDiferencia, m.Descripcion)).ToListAsync(ct);

    public async Task<IReadOnlyList<LotePendienteDto>> GetLotesPendientesAsync(int? idSucursal, CancellationToken ct = default)
    {
        idSucursal = _currentUser.AplicarAlcanceSucursal(idSucursal);
        var hoy = DateTime.UtcNow;

        var query = _db.LotesCaja.AsNoTracking()
            .Where(l => l.Estado == EstadoLote.Abierto && l.FechaApertura.Date < hoy.Date);
        if (idSucursal.HasValue) query = query.Where(l => l.IdSucursal == idSucursal.Value);

        var lotes = await query.OrderBy(l => l.FechaApertura).ToListAsync(ct);
        if (lotes.Count == 0) return Array.Empty<LotePendienteDto>();

        var sucursales = await _db.Sucursales.AsNoTracking().ToDictionaryAsync(s => s.IdSucursal, s => s.Descripcion, ct);
        var usuarios = await _db.Usuarios.AsNoTracking().ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario, ct);
        var cajas = await _db.Cajas.AsNoTracking()
            .ToDictionaryAsync(c => new { c.IdSucursal, c.IdCaja }, c => c.Descripcion, ct);

        var resultado = new List<LotePendienteDto>();
        foreach (var l in lotes)
        {
            var acumulados = await _ejecutor.AcumularAsync(l.IdSucursal, l.IdLote, ct);
            resultado.Add(new LotePendienteDto(
                l.IdSucursal, sucursales.GetValueOrDefault(l.IdSucursal), l.IdLote, l.IdCaja,
                cajas.GetValueOrDefault(new { l.IdSucursal, l.IdCaja }, $"Caja {l.IdCaja}"),
                usuarios.GetValueOrDefault(l.IdUsuarioApertura), l.FechaApertura,
                (int)(hoy.Date - l.FechaApertura.Date).TotalDays,
                acumulados, acumulados.Sum(a => a.Total)));
        }
        return resultado;
    }

    public async Task<CerrarTurnoResponse> CerrarLotePendienteAsync(int idSucursal, int idLote,
        CerrarLotePendienteRequest req, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        if (req.Declaraciones.Any(d => d.MontoDeclarado < 0))
            throw new DomainException("MONTO_INVALIDO", "El monto declarado no puede ser negativo.");

        var lote = await _db.LotesCaja.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IdSucursal == idSucursal && l.IdLote == idLote, ct)
            ?? throw new DomainException("LOTE_INEXISTENTE", "No existe el lote indicado en esa sucursal.");

        // A propósito NO se llama a AsegurarCaja: el alcance de este endpoint es la sucursal, no la
        // caja del puesto. La sesión queda atada a una caja física según el equipo vinculado (ver
        // LoginCommand/ResolverCajaPorEquipoAsync), así que exigir que coincida haría que un Tesorero
        // sentado en una caja no pudiera regularizar ninguna otra — que es justamente para lo que
        // existe esta vía. Además contradecía a GetLotesPendientesAsync, que lista los lotes de todas
        // las cajas de la sucursal: se ofrecía cerrar un lote que después se rechazaba.
        if (!CierreLoteReglas.PuedeCerrarse(lote.Estado))
            throw new DomainException("LOTE_YA_CERRADO", "El lote ya fue cerrado (el cierre de turno es irreversible).");

        // El lote del día en curso queda fuera a propósito: ese lo cierra su cajero con el Z normal
        // desde Caja, con la plata en la mano. Esta vía es solo para regularizar lo que quedó colgado.
        if (!CierreLoteReglas.EsLotePendienteDeDiaAnterior(lote.Estado, lote.FechaApertura, DateTime.UtcNow))
            throw new DomainException("LOTE_DEL_DIA",
                "El lote es del día en curso: debe cerrarlo su cajero con el cierre de turno desde Caja.");

        // Se valida la existencia del motivo acá y no se delega a la FK: así el cajero/tesorero recibe
        // un 409 con código en vez del 500 genérico de una violación de clave foránea.
        if (!await _db.MotivosCierre.AsNoTracking().AnyAsync(m => m.IdMotivoCierre == req.IdMotivoCierre, ct))
            throw new DomainException("MOTIVO_CIERRE_INVALIDO",
                "Debe indicar un motivo de cierre válido para regularizar un lote pendiente.");

        var acumulados = await _ejecutor.AcumularAsync(idSucursal, idLote, ct);
        var detalle = await _ejecutor.ArmarDetalleAsync(acumulados, req.Declaraciones, ct);

        if (detalle.Any(d => d.RequiereMotivo))
        {
            if (req.IdMotivoDiferencia is null)
                throw new DomainException("MOTIVO_REQUERIDO",
                    "Hay diferencias entre lo declarado y lo esperado: debe indicar un motivo de diferencia.");
            if (!await _db.MotivosDiferencia.AsNoTracking().AnyAsync(m => m.IdMotivoDiferencia == req.IdMotivoDiferencia, ct))
                throw new DomainException("MOTIVO_DIFERENCIA_INVALIDO", "El motivo de diferencia indicado no existe.");
        }

        var cierre = await _ejecutor.CerrarAsync(idSucursal, idLote, detalle, acumulados,
            new CierreLoteJustificacion(req.IdMotivoDiferencia,
                // La observación del cajero queda vacía: no fue el cajero quien declaró estos montos.
                ObservacionesCajero: null,
                req.IdMotivoCierre, req.ObservacionTesoreria), ct);

        // Sin cierre Z fiscal: la impresora vive en la caja física y este cierre se hace días después
        // desde otro puesto. El comprobante fiscal del lote, si hace falta, se resuelve por fuera.
        var anulaciones = await _ejecutor.AnulacionesAsync(idSucursal, idLote, ct);
        return new CerrarTurnoResponse(idSucursal, idLote, cierre.NumeroCierre, cierre.FechaCierre,
            detalle, detalle.Sum(d => d.Diferencia),
            anulaciones, anulaciones.Sum(a => a.Total));
    }

    public async Task<CorreccionDto> CorregirAsync(int idSucursal, int idLote, CorreccionManualInput req,
        CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        if (req.Monto == 0)
            throw new DomainException("MONTO_INVALIDO", "El monto de la corrección no puede ser cero.");
        if (string.IsNullOrWhiteSpace(req.Concepto))
            throw new DomainException("CONCEPTO_REQUERIDO", "Una corrección de Tesorería necesita un motivo.");

        // A propósito no exige que el lote siga abierto: esta vía existe justamente para ajustar
        // rendiciones ya cerradas (o incluso ya validadas) sin tener que reabrir nada.
        var lote = await _db.LotesCaja.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IdSucursal == idSucursal && l.IdLote == idLote, ct)
            ?? throw new DomainException("LOTE_INEXISTENTE", "No existe el lote indicado en esa sucursal.");

        if (!await _db.MediosPago.AsNoTracking().AnyAsync(m => m.IdMedioPago == req.IdMedioPago, ct))
            throw new DomainException("MEDIO_INEXISTENTE", "El medio de pago indicado no existe.");

        var movCaja = await MovimientoManualCajaHelper.RegistrarAsync(_db, idSucursal, lote.IdCaja, idLote,
            _currentUser.IdUsuario ?? 0, req.IdMedioPago, req.Monto, TipoMovimientoManual.CorreccionTesoreria,
            req.Concepto.Trim(), ct);

        var usuario = _currentUser.IdUsuario is int idu
            ? (await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdUsuario == idu, ct))?.NombreUsuario
            : null;
        return new CorreccionDto(movCaja.IdMovCaja, movCaja.Fecha, req.IdMedioPago, req.Monto, movCaja.Concepto, usuario);
    }

    // ---------- Vista principal: lotes por vigencia ----------

    public async Task<IReadOnlyList<LoteResumenDto>> GetLotesAsync(int? idSucursal, DateTime desde, DateTime hasta,
        CancellationToken ct = default)
    {
        idSucursal = _currentUser.AplicarAlcanceSucursal(idSucursal);

        var query = _db.LotesCaja.AsNoTracking()
            .Where(l => l.FechaApertura.Date >= desde.Date && l.FechaApertura.Date <= hasta.Date);
        if (idSucursal.HasValue) query = query.Where(l => l.IdSucursal == idSucursal.Value);

        var lotes = await query.OrderByDescending(l => l.FechaApertura).ToListAsync(ct);
        if (lotes.Count == 0) return Array.Empty<LoteResumenDto>();

        var sucursales = await _db.Sucursales.AsNoTracking().ToDictionaryAsync(s => s.IdSucursal, s => s.Descripcion, ct);
        var usuarios = await _db.Usuarios.AsNoTracking().ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario, ct);
        var cajas = await _db.Cajas.AsNoTracking()
            .ToDictionaryAsync(c => new { c.IdSucursal, c.IdCaja }, c => c.Descripcion, ct);

        var resultado = new List<LoteResumenDto>();
        foreach (var l in lotes)
        {
            var acumulados = await _ejecutor.AcumularAsync(l.IdSucursal, l.IdLote, ct);
            var ingreso = await _ejecutor.IngresoInicialAsync(l.IdSucursal, l.IdLote, ct);
            var vueltos = await _ejecutor.VueltosAsync(l.IdSucursal, l.IdLote, ct);

            string estadoCierre;
            decimal? saldo;
            if (l.Estado == EstadoLote.Abierto)
            {
                estadoCierre = "Abierto";
                saldo = null;
            }
            else
            {
                var filasCierre = await _db.CierresLotesCaja.AsNoTracking()
                    .Where(c => c.IdSucursal == l.IdSucursal && c.IdLote == l.IdLote).ToListAsync(ct);
                // Lote sin movimientos (cero filas): el visto bueno de Tesorería queda en el propio
                // lote (ver ValidarCierreAsync), no hay filas por medio de pago que reconciliar.
                estadoCierre = filasCierre.Count > 0
                    ? (filasCierre.All(f => f.VerificaTesoreria) ? "CierreTesoreria" : "CierreCajero")
                    : (l.VerificadoTesoreriaVacio ? "CierreTesoreria" : "CierreCajero");
                saldo = filasCierre.Sum(f => f.Total);
            }

            var saldoInicial = ingreso?.Monto ?? 0m;
            // El propio ingreso inicial es un movimiento más dentro de "acumulados" (AcumularAsync
            // suma TODO lo que tenga IdLote, sin distinguir tipo): se resta acá para que
            // RendicionTotal quede neta, sin el fondo con el que arrancó el turno.
            var rendicionTotal = acumulados.Sum(a => a.Total) - saldoInicial;

            resultado.Add(new LoteResumenDto(
                l.IdSucursal, sucursales.GetValueOrDefault(l.IdSucursal), l.IdLote, l.IdCaja,
                cajas.GetValueOrDefault(new { l.IdSucursal, l.IdCaja }, $"Caja {l.IdCaja}"),
                usuarios.GetValueOrDefault(l.IdUsuarioApertura), l.FechaApertura, l.FechaCierre,
                l.Estado.ToString(), estadoCierre,
                saldoInicial, rendicionTotal, vueltos.Sum(v => v.Monto),
                saldoInicial + rendicionTotal, saldo));
        }
        return resultado;
    }

    public async Task<LoteDetalleDto> GetDetalleLoteAsync(int idSucursal, int idLote, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        var lote = await _db.LotesCaja.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IdSucursal == idSucursal && l.IdLote == idLote, ct)
            ?? throw new DomainException("LOTE_INEXISTENTE", "No existe el lote indicado en esa sucursal.");

        var acumulados = await _ejecutor.AcumularAsync(idSucursal, idLote, ct);
        var ingreso = await _ejecutor.IngresoInicialAsync(idSucursal, idLote, ct);
        var retiros = await _ejecutor.RetirosAsync(idSucursal, idLote, ct);
        var vueltos = await _ejecutor.VueltosAsync(idSucursal, idLote, ct);
        var correcciones = await _ejecutor.CorreccionesAsync(idSucursal, idLote, ct);
        var anulaciones = await _ejecutor.AnulacionesAsync(idSucursal, idLote, ct);

        var declarado = new List<CierreTurnoDetalleDto>();
        string? observacionesCajero = null;
        string? motivoCierreDescripcion = null;
        if (lote.Estado == EstadoLote.Cerrado)
        {
            var medios = await _db.MediosPago.AsNoTracking().ToDictionaryAsync(m => m.IdMedioPago, m => m.Descripcion, ct);
            var filasCierre = await _db.CierresLotesCaja.AsNoTracking()
                .Where(c => c.IdSucursal == idSucursal && c.IdLote == idLote).ToListAsync(ct);
            // Lo DECLARADO es una foto fija de lo que dijo el cajero al cerrar (f.Total no cambia
            // nunca). La DIFERENCIA, en cambio, se recalcula contra el esperado ACTUAL (no
            // f.DiferenciaTotal, que quedó congelada al momento del cierre): si Tesorería carga una
            // corrección después, el esperado se mueve y la diferencia tiene que reflejarlo — mostrar
            // el esperado nuevo al lado de una diferencia vieja es inconsistente (quedaba en $0 aunque
            // el esperado ya no coincidiera con lo declarado).
            declarado = filasCierre.Select(f =>
            {
                var esperadoActual = acumulados.FirstOrDefault(a => a.IdMedioPago == f.IdMedioPago)?.Total ?? 0m;
                var eval = DiferenciaCierreReglas.Evaluar(f.Total, esperadoActual);
                return new CierreTurnoDetalleDto(f.IdMedioPago,
                    medios.GetValueOrDefault(f.IdMedioPago, $"Medio {f.IdMedioPago}"),
                    esperadoActual, f.Total, eval.Diferencia, eval.RequiereMotivo);
            }).ToList();

            // Mismo valor repetido en todas las filas del cierre (se carga una sola vez, no por
            // medio): alcanza con tomarlo de la primera.
            observacionesCajero = filasCierre.FirstOrDefault()?.ObservacionesCajero;
            if (lote.IdMotivoCierre is int idMotivo)
                motivoCierreDescripcion = await _db.MotivosCierre.AsNoTracking()
                    .Where(m => m.IdMotivoCierre == idMotivo).Select(m => m.Descripcion).FirstOrDefaultAsync(ct);
        }

        return new LoteDetalleDto(idSucursal, idLote, acumulados, declarado, ingreso, retiros, vueltos,
            correcciones, anulaciones, motivoCierreDescripcion, observacionesCajero);
    }

    public async Task<IReadOnlyList<MedioPagoLookupDto>> GetMediosPagoAsync(CancellationToken ct = default) =>
        await _db.MediosPago.AsNoTracking().Where(m => m.Activo).OrderBy(m => m.Descripcion)
            .Select(m => new MedioPagoLookupDto(m.IdMedioPago, m.Descripcion)).ToListAsync(ct);

    public async Task<IReadOnlyList<Pos.Application.Cierres.ComprobanteLoteDto>> GetComprobantesLoteAsync(
        int idSucursal, int idLote, int? idMedioPago, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        if (!await _db.LotesCaja.AsNoTracking().AnyAsync(l => l.IdSucursal == idSucursal && l.IdLote == idLote, ct))
            throw new DomainException("LOTE_INEXISTENTE", "No existe el lote indicado en esa sucursal.");
        return await _ejecutor.ComprobantesAsync(idSucursal, idLote, idMedioPago, ct);
    }

    public async Task<IReadOnlyList<MotivoCierreDto>> GetMotivosCierreAsync(CancellationToken ct = default) =>
        await _db.MotivosCierre.AsNoTracking().OrderBy(m => m.Descripcion)
            .Select(m => new MotivoCierreDto(m.IdMotivoCierre, m.Descripcion)).ToListAsync(ct);

    public async Task<EfectividadResponse> GetEfectividadAsync(int? idSucursal, DateTime desde, DateTime hasta,
        string? cajero, CancellationToken ct = default)
    {
        idSucursal = _currentUser.AplicarAlcanceSucursal(idSucursal);

        // Solo lotes CERRADOS: la efectividad mide qué tan bien coincidió lo declarado con lo
        // esperado al cerrar, así que un lote todavía Abierto no tiene nada que aportar todavía.
        var query = _db.LotesCaja.AsNoTracking()
            .Where(l => l.Estado == EstadoLote.Cerrado && l.FechaCierre != null
                && l.FechaCierre.Value.Date >= desde.Date && l.FechaCierre.Value.Date <= hasta.Date);
        if (idSucursal.HasValue) query = query.Where(l => l.IdSucursal == idSucursal.Value);

        var lotes = await query.OrderBy(l => l.FechaCierre).ToListAsync(ct);
        var usuarios = await _db.Usuarios.AsNoTracking().ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario, ct);

        if (!string.IsNullOrWhiteSpace(cajero))
            lotes = lotes.Where(l => usuarios.GetValueOrDefault(l.IdUsuarioApertura) == cajero).ToList();

        if (lotes.Count == 0)
            return new EfectividadResponse(new List<EfectividadPuntoDto>(), new List<EfectividadCajeroDto>(), 0, 0, 100m);

        // Un punto por lote: fecha de cierre, cajero y si hubo diferencia (declarado vs esperado) —
        // se agrupa después en memoria para armar tanto la evolución diaria como el ranking por
        // cajero, sin repetir la consulta a CierresLotesCaja/acumulados dos veces.
        var puntos = new List<(DateTime Fecha, string? Cajero, bool ConDiferencia, decimal DiferenciaAbs)>();
        foreach (var l in lotes)
        {
            var acumulados = await _ejecutor.AcumularAsync(l.IdSucursal, l.IdLote, ct);
            // AcumularAsync ya incluye el fondo inicial (ver comentario en GetLotesAsync): esto ES
            // el saldo esperado total, sin tener que sumarle el ingreso aparte.
            var saldoEsperado = acumulados.Sum(a => a.Total);

            var filasCierre = await _db.CierresLotesCaja.AsNoTracking()
                .Where(c => c.IdSucursal == l.IdSucursal && c.IdLote == l.IdLote).ToListAsync(ct);
            var saldo = filasCierre.Sum(f => f.Total);
            var diferencia = Math.Abs(saldo - saldoEsperado);

            puntos.Add((l.FechaCierre!.Value.Date, usuarios.GetValueOrDefault(l.IdUsuarioApertura),
                diferencia > 0.01m, diferencia));
        }

        var evolucion = puntos.GroupBy(p => p.Fecha).OrderBy(g => g.Key)
            .Select(g => new EfectividadPuntoDto(
                g.Key.ToString("dd/MM"),
                Math.Round(100m * g.Count(x => !x.ConDiferencia) / g.Count(), 1)))
            .ToList();

        // Ranking ordenado por PEOR primero (más lotes con diferencia, después más monto acumulado):
        // es "el top que tiene diferencias" que pidió Tesorería, no un ranking de mejores.
        var ranking = puntos.Where(p => p.Cajero != null).GroupBy(p => p.Cajero!)
            .Select(g => new EfectividadCajeroDto(
                g.Key, g.Count(), g.Count(x => x.ConDiferencia),
                Math.Round(100m * g.Count(x => !x.ConDiferencia) / g.Count(), 1),
                g.Sum(x => x.DiferenciaAbs)))
            .OrderByDescending(c => c.LotesConDiferencia).ThenByDescending(c => c.SumaDiferenciasAbs)
            .ToList();

        var totalConDiferencia = puntos.Count(p => p.ConDiferencia);
        var efectividadGeneral = Math.Round(100m * (puntos.Count - totalConDiferencia) / puntos.Count, 1);

        return new EfectividadResponse(evolucion, ranking, puntos.Count, totalConDiferencia, efectividadGeneral);
    }

    private async Task<decimal> SumarMovimientosAsync(int idSucursal, int idLote, CancellationToken ct) =>
        (await ObtenerMovimientosAsync(idSucursal, idLote, ct)).Sum(m => m.Total);

    private async Task<List<MovimientoPagoPlano>> ObtenerMovimientosAsync(int idSucursal, int idLote, CancellationToken ct) =>
        await (
            from mc in _db.MovimientosCaja.AsNoTracking().Where(m => m.IdSucursal == idSucursal && m.IdLote == idLote)
            join mp in _db.MovimientosPagos.AsNoTracking() on mc.IdMovPagos equals mp.IdMovPagos
            select new MovimientoPagoPlano(mp.IdMedioPago, mp.Total, mp.Redondeo)
        ).ToListAsync(ct);

    private async Task<List<AcumuladoDto>> MapearAcumuladosAsync(IReadOnlyList<AcumuladoMedioPago> acumulados, CancellationToken ct)
    {
        var medios = await _db.MediosPago.AsNoTracking().ToDictionaryAsync(m => m.IdMedioPago, m => m.Descripcion, ct);
        return acumulados.Select(a => new AcumuladoDto(a.IdMedioPago,
            medios.GetValueOrDefault(a.IdMedioPago, $"Medio {a.IdMedioPago}"), a.Total, a.Redondeo)).ToList();
    }
}
