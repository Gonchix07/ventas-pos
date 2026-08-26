using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Application.Cupones;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Cupones de tarjeta: viven en MovimientoPago (cargados por el cajero al cobrar, ver
/// FacturacionService), no en una entidad separada — la que existía (Cupon) nunca se usaba y se
/// descartó. Este servicio los lista para rendir contra el resumen del operador y permite corregir
/// datos mal tipeados (número de cupón/lote, plan) con historial de auditoría.
/// </summary>
public class CuponesService : ICuponesService
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    public CuponesService(PosDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<CuponDto>> GetAsync(int? idSucursal, DateTime desde, DateTime hasta,
        string? cajero, CancellationToken ct = default)
    {
        idSucursal = _currentUser.AplicarAlcanceSucursal(idSucursal);

        var query =
            from mp in _db.MovimientosPagos.AsNoTracking()
            join mc in _db.MovimientosCaja.AsNoTracking() on mp.IdMovPagos equals mc.IdMovPagos!.Value
            join m in _db.MediosPago.AsNoTracking() on mp.IdMedioPago equals m.IdMedioPago
            join t in _db.TiposPago.AsNoTracking() on m.IdTipoPago equals t.IdTipoPago
            where t.Fuente == FuentePago.Tarjeta
                && mc.Fecha.Date >= desde.Date && mc.Fecha.Date <= hasta.Date
            join u in _db.Usuarios.AsNoTracking() on mc.IdUsuario equals u.IdUsuario into uj
            from u in uj.DefaultIfEmpty()
            // Left join por si algún día apareciera un pago tarjeta sin comprobante (no debería,
            // toda venta con tarjeta tiene uno) — evita que explote en vez de mostrar el cupón igual.
            join c in _db.CabecerasComprobantes.AsNoTracking()
                on new { mc.IdSucursal, IdComprobante = (int?)mc.IdComprobante }
                equals new { c.IdSucursal, IdComprobante = (int?)c.IdComprobante } into cj
            from c in cj.DefaultIfEmpty()
            // Tipo de comprobante (Factura A/B, etc.) para la columna abreviada de la tabla —
            // mismo left join encadenado que el de arriba, por si c vino null.
            join tc in _db.TiposComprobante.AsNoTracking() on c.IdTipoComprobante equals tc.IdTipoComprobante into tcj
            from tc in tcj.DefaultIfEmpty()
            select new {
                mp, mc, m, PlanId = mp.IdPlanCuota, Cajero = u != null ? u.NombreUsuario : null, c,
                TipoDescripcion = tc != null ? tc.Descripcion : null,
            };

        if (idSucursal.HasValue) query = query.Where(x => x.mc.IdSucursal == idSucursal.Value);
        if (!string.IsNullOrWhiteSpace(cajero)) query = query.Where(x => x.Cajero == cajero);

        var filas = await query.OrderByDescending(x => x.mc.Fecha).ToListAsync(ct);

        var idsPlanes = filas.Where(f => f.PlanId != null).Select(f => f.PlanId!.Value).Distinct().ToList();
        var planes = idsPlanes.Count == 0
            ? new Dictionary<int, string>()
            : await _db.PlanesCuota.AsNoTracking().Where(p => idsPlanes.Contains(p.IdPlan))
                .ToDictionaryAsync(p => p.IdPlan, p => p.Denominacion, ct);

        var idsMovPagos = filas.Select(f => f.mp.IdMovPagos).ToList();
        var corregidos = (await _db.CorreccionesCupon.AsNoTracking()
            .Where(cc => idsMovPagos.Contains(cc.IdMovPagos)).Select(cc => cc.IdMovPagos).Distinct()
            .ToListAsync(ct)).ToHashSet();

        return filas.Select(f => new CuponDto(
            f.mp.IdMovPagos, f.mc.IdSucursal, f.mc.IdLote, f.mc.IdCaja, f.mc.Fecha,
            f.mp.IdMedioPago, f.m.Descripcion, f.mp.Total,
            f.mp.NumeroCupon, f.mp.NumeroLote,
            f.mp.IdPlanCuota, f.PlanId != null ? planes.GetValueOrDefault(f.PlanId.Value) : null, f.mp.CantidadCuotas,
            f.Cajero, f.mc.IdComprobante, f.c != null ? f.c.NumeroCompleto : null,
            corregidos.Contains(f.mp.IdMovPagos),
            f.mp.Anulado, f.mp.FechaAnulacion,
            f.TipoDescripcion
        )).ToList();
    }

    public async Task<CuponDto> CorregirAsync(int idSucursal, long idMovPagos, CorregirCuponInput req,
        CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        if (string.IsNullOrWhiteSpace(req.Motivo))
            throw new DomainException("MOTIVO_REQUERIDO", "Una corrección de cupón necesita un motivo.");

        var mc = await _db.MovimientosCaja.AsNoTracking()
            .FirstOrDefaultAsync(m => m.IdSucursal == idSucursal && m.IdMovPagos == idMovPagos, ct)
            ?? throw new DomainException("CUPON_INEXISTENTE", "No existe ese pago en la sucursal indicada.");

        var mp = await _db.MovimientosPagos.FirstOrDefaultAsync(m => m.IdMovPagos == idMovPagos, ct)
            ?? throw new DomainException("CUPON_INEXISTENTE", "No existe ese pago.");

        var esTarjeta = await (
            from m in _db.MediosPago.AsNoTracking().Where(m => m.IdMedioPago == mp.IdMedioPago)
            join t in _db.TiposPago.AsNoTracking() on m.IdTipoPago equals t.IdTipoPago
            select t.Fuente == FuentePago.Tarjeta
        ).FirstOrDefaultAsync(ct);
        if (!esTarjeta)
            throw new DomainException("NO_ES_TARJETA", "Solo se pueden corregir datos de cupón de un pago con tarjeta.");

        int? cuotasNuevas = mp.CantidadCuotas;
        if (req.IdPlanCuota != mp.IdPlanCuota)
        {
            if (req.IdPlanCuota is int idPlan)
            {
                cuotasNuevas = await _db.PlanesCuota.AsNoTracking().Where(p => p.IdPlan == idPlan)
                    .Select(p => (int?)p.CantidadCuotas).FirstOrDefaultAsync(ct)
                    ?? throw new DomainException("PLAN_INEXISTENTE", "El plan de cuotas indicado no existe.");
            }
            else cuotasNuevas = null;
        }

        var correccion = new CorreccionCupon
        {
            IdMovPagos = idMovPagos,
            NumeroCuponAnterior = mp.NumeroCupon, NumeroLoteAnterior = mp.NumeroLote,
            IdPlanCuotaAnterior = mp.IdPlanCuota,
            NumeroCuponNuevo = req.NumeroCupon, NumeroLoteNuevo = req.NumeroLote,
            IdPlanCuotaNuevo = req.IdPlanCuota,
            IdUsuario = _currentUser.IdUsuario ?? 0, Fecha = DateTime.UtcNow, Motivo = req.Motivo.Trim(),
        };
        _db.CorreccionesCupon.Add(correccion);

        mp.NumeroCupon = req.NumeroCupon; mp.NumeroLote = req.NumeroLote;
        mp.IdPlanCuota = req.IdPlanCuota; mp.CantidadCuotas = cuotasNuevas;
        await _db.SaveChangesAsync(ct);

        var resultado = await GetAsync(idSucursal, mc.Fecha.Date, mc.Fecha.Date, null, ct);
        return resultado.First(c => c.IdMovPagos == idMovPagos);
    }

    public async Task<IReadOnlyList<Pos.Application.Caja.PlanCuotaResumen>> GetPlanesAsync(int idSucursal,
        long idMovPagos, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var idMedioPago = await _db.MovimientosCaja.AsNoTracking()
            .Where(m => m.IdSucursal == idSucursal && m.IdMovPagos == idMovPagos)
            .Join(_db.MovimientosPagos.AsNoTracking(), m => m.IdMovPagos, p => p.IdMovPagos, (m, p) => (int?)p.IdMedioPago)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("CUPON_INEXISTENTE", "No existe ese pago en la sucursal indicada.");

        return await _db.PlanesCuota.AsNoTracking().Where(p => p.IdMedioPago == idMedioPago)
            .OrderBy(p => p.CantidadCuotas)
            .Select(p => new Pos.Application.Caja.PlanCuotaResumen(p.IdPlan, p.Denominacion, p.CantidadCuotas))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CorreccionCuponDto>> HistorialAsync(int idSucursal, long idMovPagos,
        CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        if (!await _db.MovimientosCaja.AsNoTracking()
            .AnyAsync(m => m.IdSucursal == idSucursal && m.IdMovPagos == idMovPagos, ct))
            throw new DomainException("CUPON_INEXISTENTE", "No existe ese pago en la sucursal indicada.");

        var query =
            from cc in _db.CorreccionesCupon.AsNoTracking().Where(c => c.IdMovPagos == idMovPagos)
            join u in _db.Usuarios.AsNoTracking() on cc.IdUsuario equals u.IdUsuario into uj
            from u in uj.DefaultIfEmpty()
            orderby cc.Fecha descending
            select new CorreccionCuponDto(cc.IdCorreccionCupon, cc.Fecha, u != null ? u.NombreUsuario : null,
                cc.Motivo, cc.NumeroCuponAnterior, cc.NumeroLoteAnterior, cc.IdPlanCuotaAnterior,
                cc.NumeroCuponNuevo, cc.NumeroLoteNuevo, cc.IdPlanCuotaNuevo);
        return await query.ToListAsync(ct);
    }
}
