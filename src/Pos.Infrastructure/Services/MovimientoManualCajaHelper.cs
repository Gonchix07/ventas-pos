using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Escritura común de un movimiento de caja manual (Ingreso/Retiro/Vuelto/CorreccionTesoreria):
/// siempre es un <see cref="MovimientoPago"/> + un <see cref="MovimientoCaja"/> con
/// <c>IdComprobante = null</c> — no hay comprobante fiscal detrás, a diferencia de una venta o una
/// nota de crédito. Lo usan RetiroCajaService (retiro del cajero), CajaService (fondo inicial al
/// abrir turno), FacturacionService (vuelto, inline por ahora) y TesoreriaService (corrección).
///
/// No abre transacción propia ni valida nada de negocio (lote existente, monto válido, permisos):
/// eso es responsabilidad de cada llamador, que conoce sus propias reglas (ej. el cajero solo puede
/// tocar su lote abierto de hoy; Tesorería puede tocar cualquier lote, incluso cerrado).
/// </summary>
internal static class MovimientoManualCajaHelper
{
    public static async Task<MovimientoCaja> RegistrarAsync(PosDbContext db,
        int idSucursal, int idCaja, int idLote, int idUsuario,
        int idMedioPago, decimal monto, TipoMovimientoManual tipo, string? concepto,
        CancellationToken ct)
    {
        var movPago = new MovimientoPago { IdMedioPago = idMedioPago, Total = monto, Redondeo = 0 };
        db.MovimientosPagos.Add(movPago);
        await db.SaveChangesAsync(ct); // IdMovPagos es identity, lo asigna la BD

        var idMov = (await db.MovimientosCaja.Where(m => m.IdSucursal == idSucursal)
            .Select(m => m.IdMovCaja).MaxAsync(x => (int?)x, ct) ?? 0) + 1;
        var movCaja = new MovimientoCaja
        {
            IdSucursal = idSucursal, IdMovCaja = idMov, IdUsuario = idUsuario, IdCaja = idCaja,
            IdComprobante = null, IdLote = idLote, IdMovPagos = movPago.IdMovPagos,
            Estado = "Confirmado", Fecha = DateTime.UtcNow, Concepto = concepto, TipoManual = tipo,
        };
        db.MovimientosCaja.Add(movCaja);
        await db.SaveChangesAsync(ct);
        return movCaja;
    }
}
