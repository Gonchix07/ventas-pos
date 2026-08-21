using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Percepciones;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class PercepcionesCalculoService : IPercepcionesCalculoService
{
    private readonly PosDbContext _db;
    public PercepcionesCalculoService(PosDbContext db) => _db = db;

    public async Task<PercepcionesResultado> CalcularAsync(int idSucursal, int? idCliente,
        IReadOnlyList<LineaParaPercepcion> lineas, CancellationToken ct = default)
    {
        if (lineas.Count == 0)
            return new PercepcionesResultado(0, 0, 0, 0, Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>());

        // Alícuota "de catálogo" del artículo (la misma consulta que ya hace FacturacionService).
        var idsPres = lineas.Select(l => l.IdPresentacion).Distinct().ToList();
        var alicuotasArticulo = await (
            from pr in _db.Presentaciones.AsNoTracking().Where(p => idsPres.Contains(p.IdPresentacion))
            join a in _db.Articulos.AsNoTracking() on pr.IdArticulo equals a.IdArticulo
            join m in _db.ModosIva.AsNoTracking() on a.IdModoIva equals m.IdModoIva
            select new { pr.IdPresentacion, m.Alicuota }
        ).ToDictionaryAsync(x => x.IdPresentacion, x => x.Alicuota, ct);

        // Impuesto interno: vive en Precios (por lista+presentación), no en el artículo — hay que
        // mirar la MISMA lista de la que salió el precio que se cobró en cada línea.
        var idsListas = lineas.Where(l => l.IdListaPrecio is int).Select(l => l.IdListaPrecio!.Value).Distinct().ToList();
        var impuestoInterno = idsListas.Count == 0
            ? new Dictionary<(int Lista, int Presentacion), decimal>()
            : (await _db.Precios.AsNoTracking()
                .Where(p => idsListas.Contains(p.IdListaPrecio) && idsPres.Contains(p.IdPresentacion))
                .Select(p => new { p.IdListaPrecio, p.IdPresentacion, p.ImpuestoInterno })
                .ToListAsync(ct))
                .ToDictionary(p => (p.IdListaPrecio, p.IdPresentacion), p => p.ImpuestoInterno);

        // El Impuesto Interno es un monto FIJO por unidad (vive en Precio.ImpuestoInterno, no un
        // % ni una alícuota) y se resta de la base ANTES de discriminar IVA — el artículo sigue
        // llevando su alícuota real de catálogo (ya no se trata como Exento: eso era una regla
        // anterior, revertida a pedido — un artículo con impuesto interno también discrimina IVA
        // normal, solo que sobre "precio final − impuesto interno" en vez de sobre el precio final).
        var alicuotasPorLinea = new List<decimal>(lineas.Count);
        var netoPorLinea = new List<decimal>(lineas.Count);
        var ivaPorLinea = new List<decimal>(lineas.Count);
        decimal netoAl21 = 0m, netoAl105 = 0m, netoTotal = 0m, impuestoInternoTotal = 0m;
        foreach (var l in lineas)
        {
            var alicuota = alicuotasArticulo.GetValueOrDefault(l.IdPresentacion);
            var iiUnitario = l.IdListaPrecio is int idl
                ? impuestoInterno.GetValueOrDefault((idl, l.IdPresentacion))
                : 0m;
            var iiLinea = iiUnitario * l.Cantidad;
            alicuotasPorLinea.Add(alicuota);

            var importe = l.Precio * l.Cantidad - l.Descuento;
            var (neto, iva) = DesglioIva.Calcular(importe - iiLinea, alicuota);
            netoPorLinea.Add(neto);
            ivaPorLinea.Add(iva);
            netoTotal += neto;
            impuestoInternoTotal += iiLinea;
            if (alicuota == 0.21m) netoAl21 += neto;
            else if (alicuota == 0.105m) netoAl105 += neto;
        }

        var config = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == "MinimoPercepcionIVA21" || c.Clave == "MinimoPercepcionIVA10.5"
                || c.Clave == "MinimoPercepcionIIBB" || c.Clave == "AlicuotaIibbPorDefecto")
            .ToDictionaryAsync(c => c.Clave, c => c.Valor, ct);
        decimal ConfigDecimal(string clave, decimal porDefecto) =>
            config.TryGetValue(clave, out var v) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                ? n : porDefecto;

        var minimoIva21 = ConfigDecimal("MinimoPercepcionIVA21", 0m);
        var minimoIva105 = ConfigDecimal("MinimoPercepcionIVA10.5", 0m);
        var minimoIibb = ConfigDecimal("MinimoPercepcionIIBB", 0m);
        // Alícuota general que se percibe cuando el cliente tiene CUIT pero no está en el padrón de
        // IIBB — "no está en el padrón" es "no inscripto", no "exento": la ley prevé justamente una
        // alícuota general/máxima para ese caso, no percepción cero. Configurable (varía por
        // jurisdicción); 8% si no se cargó nada en Configuracion.
        var alicuotaIibbPorDefecto = ConfigDecimal("AlicuotaIibbPorDefecto", 8m);

        var datosCliente = idCliente is int idc
            ? await _db.Clientes.AsNoTracking().Where(c => c.IdCliente == idc)
                .Select(c => new { c.Cuit, c.CondicionIva!.CodigoInterno })
                .FirstOrDefaultAsync(ct)
            : null;
        var cuit = datosCliente?.Cuit;

        // Percepción de IVA (RG 2408/3337 AFIP): solo corresponde a Responsables Inscriptos en IVA.
        // Consumidor Final, Monotributista y Exento NO son sujetos de esta percepción, tengan o no
        // CUIT cargado — antes se cobraba a cualquier cliente con CUIT que no estuviera en el padrón
        // de excepción, lo que percibía indebidamente a consumidores finales.
        var esResponsableInscripto = string.Equals(datosCliente?.CodigoInterno, "RI", StringComparison.OrdinalIgnoreCase);

        // Excepción de percepción de IVA: si el CUIT del cliente está en el padrón, no se calcula
        // ninguna de las dos percepciones de IVA (IIBB es independiente, no lo mira esta excepción).
        var exceptuadoIva = !esResponsableInscripto || (!string.IsNullOrWhiteSpace(cuit)
            && await _db.PadronExcepcionPercepcionesIva.AsNoTracking().AnyAsync(p => p.Cuit == cuit, ct));

        var percepcionIva21 = exceptuadoIva ? 0m : PercepcionesReglas.CalcularPercepcionIva21(netoAl21, minimoIva21);
        var percepcionIva105 = exceptuadoIva ? 0m : PercepcionesReglas.CalcularPercepcionIva105(netoAl105, minimoIva105);

        decimal? alicuotaIibb = null;
        if (!string.IsNullOrWhiteSpace(cuit))
        {
            alicuotaIibb = await _db.PadronIngresosBrutos.AsNoTracking().Where(p => p.Cuit == cuit)
                .Select(p => (decimal?)p.Percepcion).FirstOrDefaultAsync(ct);
            // Cliente con CUIT pero SIN fila en el padrón: no es "exento", es "no inscripto" — se le
            // percibe la alícuota general por defecto en vez de no percibir nada.
            alicuotaIibb ??= alicuotaIibbPorDefecto;
        }
        // IIBB se calcula sobre Neto + Impuesto Interno (no solo el Neto): el impuesto interno se
        // restó de la base para discriminar IVA, pero para IIBB vuelve a formar parte de la base.
        var baseIibb = netoTotal + impuestoInternoTotal;
        var percepcionIibb = alicuotaIibb is decimal aIibb
            ? PercepcionesReglas.CalcularPercepcionIibb(baseIibb, aIibb, minimoIibb)
            : 0m;
        // Si no superó el mínimo (percepcionIibb quedó en 0) no corresponde mostrar "alícuota
        // aplicada" — no se aplicó ninguna.
        var alicuotaIibbAplicada = percepcionIibb > 0 ? (alicuotaIibb ?? 0m) : 0m;

        return new PercepcionesResultado(percepcionIva21, percepcionIva105, percepcionIibb,
            percepcionIva21 + percepcionIva105 + percepcionIibb, alicuotasPorLinea, netoPorLinea, ivaPorLinea,
            netoAl21, netoAl105, baseIibb, alicuotaIibbAplicada);
    }
}
