using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Abstractions.Interfase;
using Pos.Application.Caja;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Retiro del turno del cajero (efectivo por defecto; cualquier medio si se indica IdMedioPago):
/// resta del esperado de ese medio en la rendición (arqueo X / cierre de turno) igual que una nota
/// de crédito, pero sin comprobante — es plata que sale de la caja para enviarse a otro lado, no una
/// devolución al cliente. Ver CierreLoteEjecutor.RetirosAsync para cómo se muestra en la rendición.
/// </summary>
public class RetiroCajaService : IRetiroCajaService
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IInterfaseContableService _interfase;
    public RetiroCajaService(PosDbContext db, ICurrentUser currentUser, IInterfaseContableService interfase)
    {
        _db = db;
        _currentUser = currentUser;
        _interfase = interfase;
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

        int idMedioPago;
        bool esEfectivo;
        if (req.IdMedioPago is int idm)
        {
            var medio = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
                .FirstOrDefaultAsync(m => m.IdMedioPago == idm, ct)
                ?? throw new DomainException("MEDIO_INEXISTENTE", "El medio de pago indicado no existe.");
            idMedioPago = idm;
            esEfectivo = medio.TipoPago!.Fuente == FuentePago.Efectivo;
        }
        else
        {
            var efectivo = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
                .FirstOrDefaultAsync(m => m.TipoPago!.Fuente == FuentePago.Efectivo, ct)
                ?? throw new DomainException("SIN_MEDIO_EFECTIVO", "No hay un medio de pago de tipo Efectivo configurado.");
            idMedioPago = efectivo.IdMedioPago;
            esEfectivo = true;
        }

        var concepto = string.IsNullOrWhiteSpace(req.Concepto) ? "Retiro" : $"Retiro: {req.Concepto.Trim()}";

        // Mismo mecanismo que una nota de crédito (MovimientoPago negativo + MovimientoCaja), pero
        // con IdComprobante null: no hay ningún comprobante fiscal detrás de un retiro.
        var movCaja = await MovimientoManualCajaHelper.RegistrarAsync(_db, idSucursal, idCaja, lote.IdLote,
            idUsuario, idMedioPago, -req.Monto, TipoMovimientoManual.Retiro, concepto, ct);

        // Interfase contable (best-effort): un retiro de EFECTIVO es una entrega a tesorería, y
        // también se refleja en cja_movi — mismos tipo/rubro fijos que el cierre de turno (ver
        // InterfaseContableReglas), pero con su propio texto de detalle. Los retiros de otros
        // medios (poco frecuentes) no generan fila: no representan una entrega física de efectivo.
        if (esEfectivo)
        {
            var codigoEmpresaRetiro = await _db.Sucursales.AsNoTracking()
                .Where(s => s.IdSucursal == idSucursal)
                .Select(s => s.Empresa!.CodigoInterno).FirstOrDefaultAsync(ct) ?? "";
            await _interfase.RegistrarCierreCajaAsync(new CjaMoviInterfase(
                Fecha: movCaja.Fecha,
                Detalle: InterfaseContableReglas.DetalleRetiro(idCaja, _currentUser.Usuario ?? ""),
                Efectivo: req.Monto, Cheques: 0m, Tarjetas: 0m, Otros: 0m,
                NroCaja: InterfaseContableReglas.CajaCodigo(idCaja),
                Cajero: InterfaseContableReglas.CajeroCodigo(idUsuario),
                Empresa: codigoEmpresaRetiro, IdVentaSalon: null), ct);
        }

        return new RetiroEfectivoResponse(idSucursal, movCaja.IdMovCaja, idMedioPago, req.Monto, concepto,
            movCaja.Fecha);
    }
}
