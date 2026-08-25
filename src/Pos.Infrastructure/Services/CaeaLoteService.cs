using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions.Fiscal;
using Pos.Application.Common;
using Pos.Application.Facturacion;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Módulo "Facturación CAEA": lista los comprobantes emitidos en contingencia (CAEA, ver
/// FacturacionService/NotaCreditoService — el CAE de ARCA no respondió y se usó el CAEA precargado
/// de <c>ICaeaCargadoService</c>) que todavía no se informaron a ARCA, y permite subir el lote
/// (FECAEARegInformativo, obligatorio dentro de las 48hs de emitidos).
///
/// Un comprobante con CAE normal nunca aparece acá — ya quedó autorizado al pedirlo. Solo
/// <c>CabeceraComprobante.EsCaea == true</c> necesita este paso extra.
/// </summary>
public class CaeaLoteService : ICaeaLoteService
{
    private readonly PosDbContext _db;
    private readonly IFiscalService _fiscal;

    public CaeaLoteService(PosDbContext db, IFiscalService fiscal)
    {
        _db = db;
        _fiscal = fiscal;
    }

    public async Task<IReadOnlyList<LoteCaeaPendienteDto>> ListarPendientesAsync(CancellationToken ct = default)
    {
        var grupos = await _db.CabecerasComprobantes.AsNoTracking()
            .Where(c => c.EsCaea && c.Cae != null && c.FechaInformadoCaeaUtc == null && c.IdPuntoVenta != null)
            .GroupBy(c => new { c.IdSucursal, IdPuntoVenta = c.IdPuntoVenta!.Value, c.IdTipoComprobante, Caea = c.Cae! })
            .Select(g => new
            {
                g.Key.IdSucursal, g.Key.IdPuntoVenta, g.Key.IdTipoComprobante, g.Key.Caea,
                Cantidad = g.Count(), Total = g.Sum(x => x.Total),
                FechaDesde = g.Min(x => x.Fecha), FechaHasta = g.Max(x => x.Fecha)
            })
            .OrderBy(g => g.FechaDesde)
            .ToListAsync(ct);

        if (grupos.Count == 0) return Array.Empty<LoteCaeaPendienteDto>();

        var idsSucursal = grupos.Select(g => g.IdSucursal).Distinct().ToList();
        var sucursales = await _db.Sucursales.AsNoTracking()
            .Where(s => idsSucursal.Contains(s.IdSucursal))
            .ToDictionaryAsync(s => s.IdSucursal, s => s.Descripcion, ct);

        var puntosVenta = await _db.PuntosVenta.AsNoTracking()
            .Where(p => idsSucursal.Contains(p.IdSucursal))
            .ToDictionaryAsync(p => (p.IdSucursal, p.IdPuntoVenta), p => p.NumeroPuntoVenta, ct);

        var idsTipo = grupos.Select(g => g.IdTipoComprobante).Distinct().ToList();
        var tipos = await _db.TiposComprobante.AsNoTracking()
            .Where(t => idsTipo.Contains(t.IdTipoComprobante))
            .ToDictionaryAsync(t => t.IdTipoComprobante, t => t, ct);

        return grupos.Select(g =>
        {
            var tipo = tipos.GetValueOrDefault(g.IdTipoComprobante);
            return new LoteCaeaPendienteDto(
                g.IdSucursal, sucursales.GetValueOrDefault(g.IdSucursal, ""), g.IdPuntoVenta,
                puntosVenta.GetValueOrDefault((g.IdSucursal, g.IdPuntoVenta)),
                g.IdTipoComprobante, tipo?.Descripcion ?? "", tipo?.Letra,
                g.Caea, g.Cantidad, g.Total, g.FechaDesde, g.FechaHasta);
        }).ToList();
    }

    public async Task<IReadOnlyList<ComprobanteCaeaDto>> ListarComprobantesAsync(int idSucursal, int idPuntoVenta,
        int idTipoComprobante, string caea, CancellationToken ct = default)
    {
        var rows = await _db.CabecerasComprobantes.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdPuntoVenta == idPuntoVenta
                     && c.IdTipoComprobante == idTipoComprobante && c.Cae == caea
                     && c.EsCaea && c.FechaInformadoCaeaUtc == null)
            .OrderBy(c => c.NumeroCompleto)
            .Select(c => new { c.IdComprobante, c.NumeroCompleto, c.Letra, c.Fecha, c.Total, c.IdCliente })
            .ToListAsync(ct);

        var idsCliente = rows.Where(r => r.IdCliente is not null).Select(r => r.IdCliente!.Value).Distinct().ToList();
        var clientes = idsCliente.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Clientes.AsNoTracking().Where(c => idsCliente.Contains(c.IdCliente))
                .ToDictionaryAsync(c => c.IdCliente, c => c.Descripcion, ct);

        return rows.Select(r => new ComprobanteCaeaDto(idSucursal, r.IdComprobante, r.NumeroCompleto, r.Letra,
            r.Fecha, r.Total, r.IdCliente is int id ? clientes.GetValueOrDefault(id) : null)).ToList();
    }

    public async Task<InformarLoteCaeaResponse> InformarLoteAsync(InformarLoteCaeaRequest req, CancellationToken ct = default)
    {
        var sucursal = await _db.Sucursales.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IdSucursal == req.IdSucursal, ct)
            ?? throw new DomainException("NO_ENCONTRADO", "La sucursal no existe.");
        var puntoVenta = await _db.PuntosVenta.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdSucursal == req.IdSucursal && p.IdPuntoVenta == req.IdPuntoVenta, ct)
            ?? throw new DomainException("PUNTO_VENTA_INEXISTENTE", "El punto de venta no existe.");
        var tipoComprobante = await _db.TiposComprobante.AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdTipoComprobante == req.IdTipoComprobante, ct)
            ?? throw new DomainException("TIPO_COMPROBANTE_INEXISTENTE", "El tipo de comprobante no existe.");

        // Se traen con tracking: si ARCA acepta el lote, se marcan y se guardan en el mismo método.
        var cabeceras = await _db.CabecerasComprobantes
            .Where(c => c.IdSucursal == req.IdSucursal && c.IdPuntoVenta == req.IdPuntoVenta
                     && c.IdTipoComprobante == req.IdTipoComprobante && c.Cae == req.Caea
                     && c.EsCaea && c.FechaInformadoCaeaUtc == null)
            .OrderBy(c => c.NumeroCompleto)
            .ToListAsync(ct);
        if (cabeceras.Count == 0)
            throw new DomainException("NADA_PARA_INFORMAR", "No hay comprobantes pendientes para ese lote.");

        var idsComprobante = cabeceras.Select(c => c.IdComprobante).ToList();
        var detalles = await _db.DetallesComprobantes.AsNoTracking()
            .Where(d => d.IdSucursal == req.IdSucursal && idsComprobante.Contains(d.IdComprobante))
            .ToListAsync(ct);
        var detallesPorComprobante = detalles.GroupBy(d => d.IdComprobante)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DetalleComprobante>)g.ToList());

        var idsCliente = cabeceras.Where(c => c.IdCliente is not null).Select(c => c.IdCliente!.Value).Distinct().ToList();
        var clientes = idsCliente.Count == 0
            ? new Dictionary<int, Cliente>()
            : await _db.Clientes.AsNoTracking().Include(c => c.CondicionIva)
                .Where(c => idsCliente.Contains(c.IdCliente)).ToDictionaryAsync(c => c.IdCliente, ct);

        var letra = tipoComprobante.Letra ?? "B";
        var lote = cabeceras.Select(c =>
        {
            var det = detallesPorComprobante.GetValueOrDefault(c.IdComprobante, Array.Empty<DetalleComprobante>());
            var cliente = c.IdCliente is int idCli ? clientes.GetValueOrDefault(idCli) : null;

            var tributos = new List<TributoFiscal>();
            // BaseImponible en 0: no se persistió la base original de cada percepción (solo el
            // importe final, ver CabeceraComprobante), y FECAEARegInformativo no la usa — el body
            // que arma AfipWsfeClient.InformarLoteCaeaAsync solo manda ImpTrib (el total), nunca el
            // detalle por tributo. Si el día de mañana ARCA empezara a exigirlo habría que guardar
            // las bases al emitir.
            if (c.PercepcionIva21 > 0)
                tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIva, "PERCEPCION IVA 21%", 0m, c.PercepcionIva21));
            if (c.PercepcionIva105 > 0)
                tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIva, "PERCEPCION IVA 10,5%", 0m, c.PercepcionIva105));
            if (c.PercepcionIibb > 0)
                tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIibb, "PERCEPCION IIBB", 0m, c.PercepcionIibb));

            var numero = long.Parse((c.NumeroCompleto ?? "0-0").Split('-')[1]);
            return new ComprobanteFiscal(sucursal.IdEmpresa, puntoVenta.NumeroPuntoVenta,
                tipoComprobante.Descripcion, c.Letra ?? letra, numero, cliente?.Cuit ?? cliente?.Documento,
                c.Neto, c.Iva, c.Total, c.Fecha, req.IdSucursal,
                Cliente: cliente is null ? null : ConstruirClienteFiscal(cliente),
                Items: det.Select(d => new ItemFiscal(d.DescripcionTicket, d.Cantidad, d.PrecioUnit,
                    d.AlicuotaIva, d.Descuento, null)).ToList(),
                Tributos: tributos, CodigoArca: tipoComprobante.CodigoArca);
        }).ToList();

        var resultado = await _fiscal.InformarComprobantesCaeaAsync(sucursal.IdEmpresa, req.Caea, lote, ct);
        if (!resultado.Ok)
            return new InformarLoteCaeaResponse(false, resultado.Error, 0);

        foreach (var c in cabeceras) c.FechaInformadoCaeaUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new InformarLoteCaeaResponse(true, null, cabeceras.Count);
    }

    // Mismo criterio que NotaCreditoService/FacturacionService (duplicado a propósito, cada
    // servicio arma su ClienteFiscal desde su propio origen de datos): Consumidor Final no
    // identifica al cliente ante el controlador/ARCA aunque el POS lo tenga cargado.
    private static ClienteFiscal ConstruirClienteFiscal(Cliente cliente)
    {
        var responsabilidad = ResponsabilidadFiscalDesde(cliente.CondicionIva?.Descripcion, cliente.CondicionIva?.Letra);
        if (responsabilidad == ResponsabilidadIvaFiscal.ConsumidorFinal)
            return new ClienteFiscal("CONSUMIDOR FINAL", null, TipoDocumentoFiscal.Ninguno, responsabilidad, null);

        return new ClienteFiscal(cliente.Descripcion, cliente.Cuit ?? cliente.Documento,
            string.IsNullOrWhiteSpace(cliente.Cuit) ? TipoDocumentoFiscal.Dni : TipoDocumentoFiscal.Cuit,
            responsabilidad, cliente.Domicilio);
    }

    private static ResponsabilidadIvaFiscal ResponsabilidadFiscalDesde(string? condIva, string? letra)
    {
        var d = (condIva ?? "").ToUpperInvariant();
        if (d.Contains("INSCRIPTO") && !d.Contains("NO INSCRIPTO")) return ResponsabilidadIvaFiscal.ResponsableInscripto;
        if (d.Contains("MONOTRIBUT")) return d.Contains("SOCIAL")
            ? ResponsabilidadIvaFiscal.MonotributoSocial : ResponsabilidadIvaFiscal.Monotributo;
        if (d.Contains("EXENTO")) return ResponsabilidadIvaFiscal.Exento;
        if (d.Contains("NO RESPONSABLE")) return ResponsabilidadIvaFiscal.NoResponsable;
        if (d.Contains("CONSUMIDOR")) return ResponsabilidadIvaFiscal.ConsumidorFinal;
        return string.Equals(letra, "A", StringComparison.OrdinalIgnoreCase)
            ? ResponsabilidadIvaFiscal.ResponsableInscripto
            : ResponsabilidadIvaFiscal.ConsumidorFinal;
    }
}
