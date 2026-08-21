using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Facturacion;
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

    public ReimpresionService(PosDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ComprobanteReimpresionDto>> BuscarAsync(int idSucursal, string texto,
        DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        texto = (texto ?? "").Trim();

        var q = from c in _db.CabecerasComprobantes.AsNoTracking()
                join t in _db.TiposComprobante.AsNoTracking() on c.IdTipoComprobante equals t.IdTipoComprobante
                where c.IdSucursal == idSucursal
                select new { c, t };

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

    private async Task<Dictionary<int, string>> DescripcionesClientesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var lista = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (lista.Count == 0) return new Dictionary<int, string>();
        return await _db.Clientes.AsNoTracking().Where(c => lista.Contains(c.IdCliente))
            .ToDictionaryAsync(c => c.IdCliente, c => c.Descripcion, ct);
    }
}
