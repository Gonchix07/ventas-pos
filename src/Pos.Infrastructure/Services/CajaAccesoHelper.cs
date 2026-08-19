using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Autorización de caja para las operaciones de venta. Reemplaza al chequeo directo
/// <c>ICurrentUser.AsegurarCaja</c> en el módulo de Caja, que ataba al cajero a la caja resuelta por
/// la IP de su PC: si esa PC se rompe, el cajero no podía retomar ni cerrar su propio turno desde
/// otra máquina (el lote quedaba inaccesible con las ventas adentro).
///
/// Sigue vigente la protección contra operar la caja de OTRO: se habilita únicamente cuando el
/// usuario logueado es el dueño del lote abierto de esa caja, o cuando la caja no tiene turno propio
/// todavía y lo que se quiere es abrir uno (una PC nueva sin puesto configurado).
/// </summary>
internal static class CajaAccesoHelper
{
    /// <param name="paraApertura">
    /// true en la apertura de caja: alcanza con que la caja exista en la sucursal autorizada, porque
    /// todavía no hay lote propio al que atarse. El lote es por cajero, así que abrir uno acá no
    /// interfiere con el turno de otro cajero en la misma caja física.
    /// </param>
    public static async Task AsegurarCajaOperableAsync(PosDbContext db, ICurrentUser user,
        int idSucursal, int idCaja, bool paraApertura, CancellationToken ct)
    {
        user.AsegurarSucursal(idSucursal);

        // Caso normal: la caja que resolvió el login por la IP del puesto.
        if (user.IdCaja is null || user.IdCaja == idCaja) return;

        var idUsuario = user.IdUsuario ?? 0;
        var tieneLotePropio = await db.LotesCaja.AsNoTracking().AnyAsync(l =>
            l.IdSucursal == idSucursal && l.IdCaja == idCaja && l.IdUsuarioApertura == idUsuario &&
            l.Estado == EstadoLote.Abierto && l.FechaApertura.Date == DateTime.UtcNow.Date, ct);
        if (tieneLotePropio) return;

        if (paraApertura &&
            await db.Cajas.AsNoTracking().AnyAsync(c => c.IdSucursal == idSucursal && c.IdCaja == idCaja, ct))
            return;

        throw new AccesoDenegadoException("CAJA_NO_AUTORIZADA",
            "No tenés un turno abierto en esa caja.");
    }
}
