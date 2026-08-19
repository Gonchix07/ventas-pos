using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Abstractions.Fiscal;
using Pos.Application.Cierres;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Cierre Z del controlador fiscal (reporte "CerrarJornadaFiscal" del equipo Hasar de la caja
/// física). Deliberadamente independiente del lote de caja: no exige turno propio abierto —así un
/// supervisor lo puede disparar sin tener que abrir uno— y no le importa cuántos cajeros tengan
/// hoy un lote abierto en esa misma caja (ver CierreCajaService para el cierre de TURNO).
/// Autorización por código de supervisor, no por rol de login (ver ISupervisorAuthService): la idea
/// es que un cajero también pueda ejecutarlo si un supervisor le da el código, igual que anular un
/// artículo o emitir una nota de crédito.
/// </summary>
public class CierreZFiscalService : ICierreZFiscalService
{
    private readonly PosDbContext _db;
    private readonly IFiscalPrinter _impresora;
    private readonly ICurrentUser _currentUser;
    private readonly ISupervisorAuthService _supervisorAuth;

    public CierreZFiscalService(PosDbContext db, IFiscalPrinter impresora, ICurrentUser currentUser,
        ISupervisorAuthService supervisorAuth)
    {
        _db = db;
        _impresora = impresora;
        _currentUser = currentUser;
        _supervisorAuth = supervisorAuth;
    }

    public async Task<CierreZFiscalResponse> EjecutarAsync(int idSucursal, int idCaja, CierreZFiscalRequest req,
        CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        await _supervisorAuth.ExigirAsync(req.CodigoSupervisor, ct);

        if (!await _db.Cajas.AsNoTracking().AnyAsync(c => c.IdSucursal == idSucursal && c.IdCaja == idCaja, ct))
            throw new DomainException("CAJA_INEXISTENTE", "La caja indicada no existe en la sucursal.");

        var resultado = await _impresora.CierreZAsync(idSucursal, idCaja, ct);

        // Se audita tanto el éxito como el fallo: a diferencia del cierre de turno (donde el Z era
        // "best-effort" porque el negocio ya había quedado persistido), acá el Z ES la operación —
        // si falla, se informa como error, pero igual queda registrado que se intentó.
        var registro = new CierreZFiscal
        {
            IdSucursal = idSucursal,
            IdCaja = idCaja,
            IdUsuario = _currentUser.IdUsuario ?? 0,
            FechaHoraUtc = DateTime.UtcNow,
            Ok = resultado.Ok,
            NumeroFiscal = resultado.NumeroFiscal,
            Referencia = resultado.Referencia,
            Error = resultado.Error,
        };
        _db.CierresZFiscal.Add(registro);
        await _db.SaveChangesAsync(ct);

        if (!resultado.Ok)
            throw new DomainException("CIERRE_Z_FALLO",
                resultado.Error ?? "No se pudo ejecutar el cierre Z en el controlador fiscal.");

        return new CierreZFiscalResponse(idSucursal, idCaja, registro.FechaHoraUtc, resultado.NumeroFiscal);
    }
}
