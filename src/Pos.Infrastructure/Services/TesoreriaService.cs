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
        if (filas.Count == 0) return false;

        foreach (var f in filas)
        {
            f.VerificaTesoreria = true;
            f.IdMotivoCierre = req.IdMotivoCierre;
            f.ObservacionTesoreria = req.ObservacionTesoreria;
        }
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
        // caja del puesto. La sesión queda atada a una caja física según la IP de la PC (ver
        // LoginCommand/ResolverCajaPorIpAsync), así que exigir que coincida haría que un Tesorero
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

    public async Task<IReadOnlyList<MotivoCierreDto>> GetMotivosCierreAsync(CancellationToken ct = default) =>
        await _db.MotivosCierre.AsNoTracking().OrderBy(m => m.Descripcion)
            .Select(m => new MotivoCierreDto(m.IdMotivoCierre, m.Descripcion)).ToListAsync(ct);

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
