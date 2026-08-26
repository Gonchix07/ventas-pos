using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Cierres;
using Pos.Application.Facturacion;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Búsqueda de comprobantes ya emitidos para el módulo de Reimpresión (Supervisor/Tesorero/
/// Administrador). Mismo criterio de búsqueda que <see cref="NotaCreditoService.BuscarAsync"/>
/// (número, cliente o CUIT + rango de fechas), pero sin el filtro por signo — acá interesan tanto
/// facturas como notas de crédito — ni los campos de saldo anulable, que no aplican.
///
/// La reimpresión en sí no vive acá: reusa <see cref="Pos.Application.Facturacion.IFacturacionService.ObtenerParaImprimirAsync"/>,
/// el mismo armado que ya se usa para la vista posterior a emitir — no reemite ni reabre nada
/// fiscal (ver ReimpresionController).
/// </summary>
public class ReimpresionService : IReimpresionService
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly CierreLoteEjecutor _ejecutor;

    public ReimpresionService(PosDbContext db, ICurrentUser currentUser, CierreLoteEjecutor ejecutor)
    {
        _db = db;
        _currentUser = currentUser;
        _ejecutor = ejecutor;
    }

    public async Task<IReadOnlyList<ComprobanteReimpresionDto>> BuscarAsync(int idSucursal, string texto,
        DateTime? desde, DateTime? hasta, string? tipo = null, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        texto = (texto ?? "").Trim();

        var q = from c in _db.CabecerasComprobantes.AsNoTracking()
                join t in _db.TiposComprobante.AsNoTracking() on c.IdTipoComprobante equals t.IdTipoComprobante
                where c.IdSucursal == idSucursal
                select new { c, t };

        // Descripciones fijas del seed: "Factura A/B", "Nota de Crédito A/B", "Presupuesto" — ver
        // DbSeeder.cs. StartsWith agrupa las dos letras (A/B) de Factura y Nota de Crédito en un
        // solo filtro del combo del frontend.
        q = tipo switch
        {
            "Factura" => q.Where(x => x.t.Descripcion.StartsWith("Factura")),
            "NotaCredito" => q.Where(x => x.t.Descripcion.StartsWith("Nota de Crédito")),
            "Presupuesto" => q.Where(x => x.t.Descripcion == "Presupuesto"),
            _ => q,
        };

        if (desde is not null) q = q.Where(x => x.c.Fecha >= desde.Value.Date);
        if (hasta is not null) q = q.Where(x => x.c.Fecha < hasta.Value.Date.AddDays(1));

        if (texto.Length > 0)
        {
            q = from x in q
                join cli in _db.Clientes.AsNoTracking() on x.c.IdCliente equals cli.IdCliente into gj
                from cli in gj.DefaultIfEmpty()
                where x.c.NumeroCompleto!.Contains(texto)
                      || (cli != null && (cli.Descripcion.Contains(texto) || cli.Cuit!.Contains(texto)))
                select x;
        }

        // Se ordena ANTES de proyectar: EF no traduce OrderBy sobre una proyección a record.
        var cabeceras = await q.OrderByDescending(x => x.c.Fecha).Take(100)
            .Select(x => new { x.c.IdComprobante, x.c.NumeroCompleto, x.c.Letra,
                               TipoDescripcion = x.t.Descripcion, x.c.Fecha, x.c.IdCliente,
                               x.c.Total, x.c.Estado })
            .ToListAsync(ct);

        var clientes = await DescripcionesClientesAsync(cabeceras.Select(c => c.IdCliente), ct);

        return cabeceras.Select(c => new ComprobanteReimpresionDto(idSucursal, c.IdComprobante,
            c.NumeroCompleto ?? "", c.Letra, c.TipoDescripcion, c.Fecha, c.IdCliente,
            c.IdCliente is int id ? clientes.GetValueOrDefault(id) : null,
            c.Total, c.Estado.ToString())).ToList();
    }

    public async Task<IReadOnlyList<RendicionReimpresionDto>> BuscarRendicionesAsync(int idSucursal, string texto,
        DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        texto = (texto ?? "").Trim();

        var q = from l in _db.LotesCaja.AsNoTracking()
                join u in _db.Usuarios.AsNoTracking() on l.IdUsuarioApertura equals u.IdUsuario into uj
                from u in uj.DefaultIfEmpty()
                where l.IdSucursal == idSucursal && l.Estado == EstadoLote.Cerrado
                select new { l, Cajero = u != null ? u.NombreUsuario : null };

        if (desde is not null) q = q.Where(x => x.l.FechaCierre >= desde.Value.Date);
        if (hasta is not null) q = q.Where(x => x.l.FechaCierre < hasta.Value.Date.AddDays(1));

        if (texto.Length > 0)
        {
            q = int.TryParse(texto, out var idLoteBuscado)
                ? q.Where(x => x.l.IdLote == idLoteBuscado || (x.Cajero != null && x.Cajero.Contains(texto)))
                : q.Where(x => x.Cajero != null && x.Cajero.Contains(texto));
        }

        // Se ordena ANTES de proyectar (mismo motivo que BuscarAsync): EF no traduce OrderBy sobre
        // una proyección a record. Tope de 100 lotes recientes — mismo criterio que BuscarAsync.
        var lotes = await q.OrderByDescending(x => x.l.FechaCierre).Take(100)
            .Select(x => new { x.l.IdLote, x.l.IdCaja, x.l.FechaCierre, x.l.FechaApertura, x.Cajero })
            .ToListAsync(ct);
        if (lotes.Count == 0) return Array.Empty<RendicionReimpresionDto>();

        var cajas = await _db.Cajas.AsNoTracking().Where(c => c.IdSucursal == idSucursal)
            .ToDictionaryAsync(c => c.IdCaja, c => c.Descripcion, ct);
        var idsLote = lotes.Select(l => l.IdLote).ToList();
        var cierres = await _db.CierresLotesCaja.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && idsLote.Contains(c.IdLote))
            .ToListAsync(ct);

        return lotes.Select(l =>
        {
            var filas = cierres.Where(c => c.IdLote == l.IdLote).ToList();
            return new RendicionReimpresionDto(idSucursal, l.IdLote, l.IdCaja,
                cajas.GetValueOrDefault(l.IdCaja, $"Caja {l.IdCaja}"), l.Cajero,
                l.FechaCierre ?? l.FechaApertura, filas.FirstOrDefault()?.NumeroCierre, filas.Sum(f => f.Total));
        }).ToList();
    }

    public async Task<RendicionImpresionDto?> ObtenerRendicionAsync(int idSucursal, int idLote, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var lote = await _db.LotesCaja.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IdSucursal == idSucursal && l.IdLote == idLote && l.Estado == EstadoLote.Cerrado, ct);
        if (lote is null) return null;

        var descripcionCaja = await _db.Cajas.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdCaja == lote.IdCaja)
            .Select(c => c.Descripcion).FirstOrDefaultAsync(ct) ?? $"Caja {lote.IdCaja}";
        var usuario = await _db.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == lote.IdUsuarioApertura)
            .Select(u => u.NombreUsuario).FirstOrDefaultAsync(ct) ?? "—";

        var acumulados = await _ejecutor.AcumularAsync(idSucursal, idLote, ct);
        var ingreso = await _ejecutor.IngresoInicialAsync(idSucursal, idLote, ct);
        var retiros = await _ejecutor.RetirosAsync(idSucursal, idLote, ct);
        var vueltos = await _ejecutor.VueltosAsync(idSucursal, idLote, ct);
        var anulaciones = await _ejecutor.AnulacionesAsync(idSucursal, idLote, ct);

        var medios = await _db.MediosPago.AsNoTracking().ToDictionaryAsync(m => m.IdMedioPago, m => m.Descripcion, ct);
        var filasCierre = await _db.CierresLotesCaja.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdLote == idLote).ToListAsync(ct);

        // Mismo criterio que TesoreriaService.GetDetalleLoteAsync: lo declarado es una foto fija de
        // lo que dijo el cajero al cerrar, la diferencia se recalcula contra el esperado ACTUAL (por
        // si Tesorería cargó una corrección después del cierre).
        var detalle = filasCierre.Select(f =>
        {
            var esperadoActual = acumulados.FirstOrDefault(a => a.IdMedioPago == f.IdMedioPago)?.Total ?? 0m;
            var eval = DiferenciaCierreReglas.Evaluar(f.Total, esperadoActual);
            return new CierreTurnoDetalleDto(f.IdMedioPago, medios.GetValueOrDefault(f.IdMedioPago, $"Medio {f.IdMedioPago}"),
                esperadoActual, f.Total, eval.Diferencia, eval.RequiereMotivo);
        }).ToList();

        string? motivoCierreDescripcion = null;
        if (lote.IdMotivoCierre is int idMotivo)
            motivoCierreDescripcion = await _db.MotivosCierre.AsNoTracking()
                .Where(m => m.IdMotivoCierre == idMotivo).Select(m => m.Descripcion).FirstOrDefaultAsync(ct);

        var arqueo = new ArqueoXResponse(idSucursal, idLote, lote.IdCaja, descripcionCaja, lote.FechaApertura,
            acumulados, acumulados.Sum(a => a.Total), null,
            anulaciones, anulaciones.Sum(a => a.Total),
            retiros, retiros.Sum(r => r.Monto),
            vueltos, vueltos.Sum(v => v.Monto),
            ingreso);

        var cierre = new CerrarTurnoResponse(idSucursal, idLote, filasCierre.FirstOrDefault()?.NumeroCierre ?? 0,
            lote.FechaCierre ?? lote.FechaApertura, detalle, detalle.Sum(d => d.Diferencia),
            anulaciones, anulaciones.Sum(a => a.Total));

        return new RendicionImpresionDto(arqueo, cierre, usuario, motivoCierreDescripcion,
            filasCierre.FirstOrDefault()?.ObservacionesCajero);
    }

    private async Task<Dictionary<int, string>> DescripcionesClientesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var lista = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (lista.Count == 0) return new Dictionary<int, string>();
        return await _db.Clientes.AsNoTracking().Where(c => lista.Contains(c.IdCliente))
            .ToDictionaryAsync(c => c.IdCliente, c => c.Descripcion, ct);
    }
}
