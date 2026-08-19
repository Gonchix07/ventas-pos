using Microsoft.EntityFrameworkCore;
using Pos.Application.Abm;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class OfertaMedioPagoAdminService : IOfertaMedioPagoAdminService
{
    private readonly PosDbContext _db;
    public OfertaMedioPagoAdminService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<OfertaMedioPagoDto>> GetAllAsync(int idSucursal, CancellationToken ct = default)
    {
        var query =
            from o in _db.OfertasMedioPago.AsNoTracking().Where(x => x.IdSucursal == idSucursal)
            join m in _db.MediosPago.AsNoTracking() on o.IdMedioPago equals m.IdMedioPago into mj
            from m in mj.DefaultIfEmpty()
            join p in _db.PlanesCuota.AsNoTracking() on o.IdPlanCuota equals p.IdPlan into pj
            from p in pj.DefaultIfEmpty()
            orderby o.IdOfertaMedioPago
            select new OfertaMedioPagoDto(o.IdSucursal, o.IdOfertaMedioPago, o.Descripcion,
                o.IdMedioPago, m != null ? m.Descripcion : null, o.IdPlanCuota, p != null ? p.Denominacion : null,
                o.Porcentaje, o.TopeMaximo, o.Activo, o.FechaInicio, o.FechaFin);
        return await query.ToListAsync(ct);
    }

    public async Task<int> CreateAsync(int idSucursal, OfertaMedioPagoInput input, CancellationToken ct = default)
    {
        await ValidarAsync(idSucursal, null, input, ct);
        var next = (await _db.OfertasMedioPago.Where(o => o.IdSucursal == idSucursal)
            .Select(o => o.IdOfertaMedioPago).MaxAsync(x => (int?)x, ct) ?? 0) + 1;

        _db.OfertasMedioPago.Add(new OfertaMedioPago
        {
            IdSucursal = idSucursal, IdOfertaMedioPago = next, Descripcion = input.Descripcion.Trim(),
            IdMedioPago = input.IdMedioPago, IdPlanCuota = input.IdPlanCuota,
            Porcentaje = input.Porcentaje, TopeMaximo = input.TopeMaximo, Activo = input.Activo,
            FechaInicio = input.FechaInicio.Date, FechaFin = input.FechaFin.Date
        });
        await _db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<bool> UpdateAsync(int idSucursal, int id, OfertaMedioPagoInput input, CancellationToken ct = default)
    {
        await ValidarAsync(idSucursal, id, input, ct);
        var o = await _db.OfertasMedioPago.FirstOrDefaultAsync(x => x.IdSucursal == idSucursal && x.IdOfertaMedioPago == id, ct);
        if (o is null) return false;

        o.Descripcion = input.Descripcion.Trim();
        o.IdMedioPago = input.IdMedioPago;
        o.IdPlanCuota = input.IdPlanCuota;
        o.Porcentaje = input.Porcentaje;
        o.TopeMaximo = input.TopeMaximo;
        o.Activo = input.Activo;
        o.FechaInicio = input.FechaInicio.Date;
        o.FechaFin = input.FechaFin.Date;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int idSucursal, int id, CancellationToken ct = default)
    {
        var o = await _db.OfertasMedioPago.FirstOrDefaultAsync(x => x.IdSucursal == idSucursal && x.IdOfertaMedioPago == id, ct);
        if (o is null) return false;
        _db.OfertasMedioPago.Remove(o);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Evita ambigüedad al cobrar: dos ofertas ACTIVAS para el mismo medio+plan (o dos generales del
    /// mismo medio) con vigencias que se superponen harían indistinguible cuál aplica un día dado —
    /// ver OfertaMedioPagoReglas.Resolver, que asume una sola coincidencia por combinación. Fuera de
    /// las fechas en común no hay pisada: dos ofertas del mismo medio+plan en meses distintos conviven bien.
    /// </summary>
    private async Task ValidarAsync(int idSucursal, int? idExcluir, OfertaMedioPagoInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Descripcion))
            throw new DomainException("DESCRIPCION_REQUERIDA", "La descripción es obligatoria.");
        if (input.Porcentaje is <= 0 or > 100)
            throw new DomainException("PORCENTAJE_INVALIDO", "El porcentaje debe estar entre 0 y 100.");
        if (input.TopeMaximo <= 0)
            throw new DomainException("TOPE_INVALIDO", "El tope máximo debe ser mayor a 0.");
        if (input.FechaFin.Date < input.FechaInicio.Date)
            throw new DomainException("VIGENCIA_INVALIDA", "La vigencia termina antes de empezar.");

        if (!await _db.MediosPago.AnyAsync(m => m.IdMedioPago == input.IdMedioPago, ct))
            throw new DomainException("MEDIO_PAGO_INEXISTENTE", "El medio de pago no existe.");
        if (input.IdPlanCuota is int idPlan &&
            !await _db.PlanesCuota.AnyAsync(p => p.IdPlan == idPlan && p.IdMedioPago == input.IdMedioPago, ct))
            throw new DomainException("PLAN_INEXISTENTE", "El plan de cuotas no existe para ese medio de pago.");

        if (input.Activo)
        {
            var inicio = input.FechaInicio.Date;
            var fin = input.FechaFin.Date;
            // Superposición de rangos [inicio,fin] y [o.FechaInicio,o.FechaFin]: inicio <= finOtra Y otraInicio <= fin.
            var conflicto = await _db.OfertasMedioPago.AsNoTracking()
                .Where(o => o.IdSucursal == idSucursal && o.Activo && o.IdOfertaMedioPago != (idExcluir ?? 0)
                    && o.IdMedioPago == input.IdMedioPago && o.IdPlanCuota == input.IdPlanCuota
                    && inicio <= o.FechaFin && o.FechaInicio <= fin)
                .AnyAsync(ct);
            if (conflicto)
                throw new DomainException("OFERTA_DUPLICADA",
                    "Ya hay una oferta activa para ese medio de pago y esa cantidad de cuotas con vigencia superpuesta.");
        }
    }
}
