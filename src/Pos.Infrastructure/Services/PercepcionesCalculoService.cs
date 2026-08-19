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
            return new PercepcionesResultado(0, 0, 0, 0, Array.Empty<decimal>());

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

        // Un artículo con impuesto interno va Exento a los efectos de IVA (pedido explícito del
        // usuario): ni discrimina IVA propio ni entra en la base de percepción de IVA al 21%/10,5%.
        var alicuotasEfectivas = new List<decimal>(lineas.Count);
        decimal netoAl21 = 0m, netoAl105 = 0m, netoTotal = 0m;
        foreach (var l in lineas)
        {
            var alicuotaArticulo = alicuotasArticulo.GetValueOrDefault(l.IdPresentacion);
            var tieneImpuestoInterno = l.IdListaPrecio is int idl
                && impuestoInterno.TryGetValue((idl, l.IdPresentacion), out var ii) && ii > 0;
            var alicuotaEfectiva = tieneImpuestoInterno ? 0m : alicuotaArticulo;
            alicuotasEfectivas.Add(alicuotaEfectiva);

            var importe = l.Precio * l.Cantidad - l.Descuento;
            var (neto, _) = DesglioIva.Calcular(importe, alicuotaEfectiva);
            netoTotal += neto;
            if (alicuotaEfectiva == 0.21m) netoAl21 += neto;
            else if (alicuotaEfectiva == 0.105m) netoAl105 += neto;
        }

        var minimos = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == "MinimoPercepcionIVA21" || c.Clave == "MinimoPercepcionIVA10.5"
                || c.Clave == "MinimoPercepcionIIBB")
            .ToDictionaryAsync(c => c.Clave, c => c.Valor, ct);
        decimal Minimo(string clave) =>
            minimos.TryGetValue(clave, out var v) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                ? n : 0m;

        var minimoIva21 = Minimo("MinimoPercepcionIVA21");
        var minimoIva105 = Minimo("MinimoPercepcionIVA10.5");
        var minimoIibb = Minimo("MinimoPercepcionIIBB");

        var cuit = idCliente is int idc
            ? await _db.Clientes.AsNoTracking().Where(c => c.IdCliente == idc).Select(c => c.Cuit).FirstOrDefaultAsync(ct)
            : null;

        // Excepción de percepción de IVA: si el CUIT del cliente está en el padrón, no se calcula
        // ninguna de las dos percepciones de IVA (IIBB es independiente, no lo mira esta excepción).
        var exceptuadoIva = !string.IsNullOrWhiteSpace(cuit)
            && await _db.PadronExcepcionPercepcionesIva.AsNoTracking().AnyAsync(p => p.Cuit == cuit, ct);

        var percepcionIva21 = exceptuadoIva ? 0m : PercepcionesReglas.CalcularPercepcionIva21(netoAl21, minimoIva21);
        var percepcionIva105 = exceptuadoIva ? 0m : PercepcionesReglas.CalcularPercepcionIva105(netoAl105, minimoIva105);

        var alicuotaIibb = !string.IsNullOrWhiteSpace(cuit)
            ? await _db.PadronIngresosBrutos.AsNoTracking().Where(p => p.Cuit == cuit)
                .Select(p => (decimal?)p.Percepcion).FirstOrDefaultAsync(ct)
            : null;
        var percepcionIibb = alicuotaIibb is decimal aIibb
            ? PercepcionesReglas.CalcularPercepcionIibb(netoTotal, aIibb, minimoIibb)
            : 0m;

        return new PercepcionesResultado(percepcionIva21, percepcionIva105, percepcionIibb,
            percepcionIva21 + percepcionIva105 + percepcionIibb, alicuotasEfectivas,
            netoAl21, netoAl105, netoTotal);
    }
}
