using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Caja;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Retiro de efectivo del turno del cajero: resta del efectivo esperado en la rendición (arqueo X
/// / cierre de turno) igual que una nota de crédito, pero sin comprobante — es plata que sale de la
/// caja para enviarse a otro lado, no una devolución al cliente. Ver CierreLoteEjecutor.RetirosAsync
/// para cómo se muestra en la rendición.
/// </summary>
public class RetiroCajaService : IRetiroCajaService
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    public RetiroCajaService(PosDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<RetiroEfectivoResponse> RegistrarAsync(int idSucursal, int idCaja, RetiroEfectivoRequest req,
        CancellationToken ct = default)
    {
        await CajaAccesoHelper.AsegurarCajaOperableAsync(_db, _currentUser, idSucursal, idCaja, false, ct);

        if (req.Monto <= 0)
            throw new DomainException("MONTO_INVALIDO", "El monto a retirar debe ser mayor a cero.");

        var idUsuario = _currentUser.IdUsuario ?? 0;
        var lote = await _db.LotesCaja.AsNoTracking().FirstOrDefaultAsync(l =>
            l.IdSucursal == idSucursal && l.IdCaja == idCaja && l.IdUsuarioApertura == idUsuario &&
            l.Estado == EstadoLote.Abierto && l.FechaApertura.Date == DateTime.UtcNow.Date, ct)
            ?? throw new DomainException("SIN_LOTE_ABIERTO", "No hay un lote abierto para esta caja.");

        var efectivo = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
            .FirstOrDefaultAsync(m => m.TipoPago!.Fuente == FuentePago.Efectivo, ct)
            ?? throw new DomainException("SIN_MEDIO_EFECTIVO", "No hay un medio de pago de tipo Efectivo configurado.");

        var concepto = string.IsNullOrWhiteSpace(req.Concepto) ? "Retiro" : $"Retiro: {req.Concepto.Trim()}";

        // Mismo mecanismo que una nota de crédito (MovimientoPago negativo + MovimientoCaja), pero
        // con IdComprobante null: no hay ningún comprobante fiscal detrás de un retiro.
        var movPago = new MovimientoPago { IdMedioPago = efectivo.IdMedioPago, Total = -req.Monto, Redondeo = 0 };
        _db.MovimientosPagos.Add(movPago);
        await _db.SaveChangesAsync(ct); // IdMovPagos es identity, lo asigna la BD

        var idMov = (await _db.MovimientosCaja.Where(m => m.IdSucursal == idSucursal)
            .Select(m => m.IdMovCaja).MaxAsync(x => (int?)x, ct) ?? 0) + 1;
        var fecha = DateTime.UtcNow;
        _db.MovimientosCaja.Add(new MovimientoCaja
        {
            IdSucursal = idSucursal, IdMovCaja = idMov, IdUsuario = idUsuario, IdCaja = idCaja,
            IdComprobante = null, IdLote = lote.IdLote, IdMovPagos = movPago.IdMovPagos,
            Estado = "Confirmado", Fecha = fecha, Concepto = concepto,
        });
        await _db.SaveChangesAsync(ct);

        return new RetiroEfectivoResponse(idSucursal, idMov, req.Monto, concepto, fecha);
    }
}
