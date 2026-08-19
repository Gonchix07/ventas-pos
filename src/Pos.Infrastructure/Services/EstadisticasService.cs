using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Estadisticas;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Dashboard de estadísticas de ventas (módulo Ventas del Admin). Todo se recalcula en cada consulta
/// directamente contra Comprobantes/Operaciones — no hay tablas de agregados propias todavía. Para los
/// volúmenes actuales (una cadena mayorista, no miles de sucursales) el costo de recorrer un período de
/// hasta un año es aceptable; si el catálogo de comprobantes crece mucho, esto es candidato a resolverse
/// con una tabla de resumen diario en vez de recalcular desde el detalle en cada request.
/// </summary>
public class EstadisticasService : IEstadisticasService
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    public EstadisticasService(PosDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<EstadisticasVentasResponse> GetVentasAsync(PeriodoEstadisticas periodo, int? idSucursal, CancellationToken ct = default)
    {
        idSucursal = _currentUser.AplicarAlcanceSucursal(idSucursal);
        var (desde, hasta) = ResolverRango(periodo);

        var facturas = FacturasValidas(desde, hasta, idSucursal);
        var notasCredito = ComprobantesValidos(desde, hasta, idSucursal, signo: -1);

        var totalFacturas = await facturas.SumAsync(c => (decimal?)c.Total, ct) ?? 0m;
        var cantidadTickets = await facturas.CountAsync(ct);
        var totalNc = await notasCredito.SumAsync(c => (decimal?)c.Total, ct) ?? 0m;
        var cantidadNc = await notasCredito.CountAsync(ct);
        var cantidadClientes = await facturas.Where(c => c.IdCliente != null)
            .Select(c => c.IdCliente).Distinct().CountAsync(ct);

        var totalDescuentos = await (
            from d in _db.DetallesComprobantes.AsNoTracking()
            join c in facturas on new { d.IdSucursal, d.IdComprobante } equals new { c.IdSucursal, c.IdComprobante }
            select (decimal?)d.Descuento
        ).SumAsync(ct) ?? 0m;

        var resumen = new ResumenVentasDto(totalFacturas - totalNc, cantidadTickets, cantidadClientes,
            cantidadTickets > 0 ? totalFacturas / cantidadTickets : 0m, totalDescuentos, cantidadNc, totalNc);

        var evolucion = await CalcularEvolucionAsync(periodo, desde, hasta, facturas, notasCredito, ct);
        var (familias, sectores, productos) = await CalcularFamiliasSectoresYProductosAsync(facturas, ct);
        var topClientes = await CalcularTopClientesAsync(facturas, ct);
        var ofertas = await CalcularEfectividadOfertasAsync(facturas, ct);

        return new EstadisticasVentasResponse(periodo, desde, hasta, resumen,
            familias, evolucion, sectores, productos, topClientes, ofertas);
    }

    // ---------- Rango de fechas ----------

    private static (DateTime Desde, DateTime Hasta) ResolverRango(PeriodoEstadisticas periodo)
    {
        var hoy = DateTime.UtcNow.Date;
        var hasta = hoy.AddDays(1); // exclusivo: incluye todo "hoy"
        return periodo switch
        {
            PeriodoEstadisticas.Hoy => (hoy, hasta),
            PeriodoEstadisticas.Ultimos7Dias => (hoy.AddDays(-6), hasta),
            PeriodoEstadisticas.Ultimos30Dias => (hoy.AddDays(-29), hasta),
            PeriodoEstadisticas.UltimoAnio => (hoy.AddYears(-1).AddDays(1), hasta),
            _ => (hoy, hasta),
        };
    }

    // ---------- Consultas base ----------

    private IQueryable<CabeceraComprobante> ComprobantesValidos(DateTime desde, DateTime hasta, int? idSucursal, int signo)
    {
        var q =
            from c in _db.CabecerasComprobantes.AsNoTracking()
            join t in _db.TiposComprobante.AsNoTracking() on c.IdTipoComprobante equals t.IdTipoComprobante
            where t.Signo == signo && c.Fecha >= desde && c.Fecha < hasta && c.Estado != EstadoComprobante.Iniciado
            select c;
        if (idSucursal.HasValue) q = q.Where(c => c.IdSucursal == idSucursal.Value);
        return q;
    }

    // Signo +1: comprobantes de venta (facturas/tickets), lo que factura el negocio.
    private IQueryable<CabeceraComprobante> FacturasValidas(DateTime desde, DateTime hasta, int? idSucursal) =>
        ComprobantesValidos(desde, hasta, idSucursal, signo: 1);

    // ---------- Evolución (serie temporal) ----------

    private async Task<List<VentaPorPeriodoDto>> CalcularEvolucionAsync(PeriodoEstadisticas periodo, DateTime desde, DateTime hasta,
        IQueryable<CabeceraComprobante> facturas, IQueryable<CabeceraComprobante> notasCredito, CancellationToken ct)
    {
        // "Hoy" se agrupa por hora (24 puntos); el resto por día, salvo "Último año" que se agrupa
        // por mes (12 puntos) — un día por punto en un año serían 365 barras, ilegibles en un gráfico.
        if (periodo == PeriodoEstadisticas.Hoy)
        {
            var porHoraF = await facturas.GroupBy(c => c.Fecha.Hour)
                .Select(g => new { Clave = g.Key, Total = g.Sum(x => x.Total) }).ToDictionaryAsync(x => x.Clave, x => x.Total, ct);
            var porHoraNc = await notasCredito.GroupBy(c => c.Fecha.Hour)
                .Select(g => new { Clave = g.Key, Total = g.Sum(x => x.Total) }).ToDictionaryAsync(x => x.Clave, x => x.Total, ct);
            return Enumerable.Range(0, 24)
                .Select(h => new VentaPorPeriodoDto($"{h:00}:00", porHoraF.GetValueOrDefault(h) - porHoraNc.GetValueOrDefault(h)))
                .ToList();
        }

        if (periodo == PeriodoEstadisticas.UltimoAnio)
        {
            var porMesF = await facturas.GroupBy(c => new { c.Fecha.Year, c.Fecha.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.Total) }).ToListAsync(ct);
            var porMesNc = await notasCredito.GroupBy(c => new { c.Fecha.Year, c.Fecha.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.Total) })
                .ToDictionaryAsync(x => (x.Year, x.Month), x => x.Total, ct);
            var puntos = new List<VentaPorPeriodoDto>();
            for (var m = new DateTime(desde.Year, desde.Month, 1); m < hasta; m = m.AddMonths(1))
            {
                var totalF = porMesF.Where(x => x.Year == m.Year && x.Month == m.Month).Sum(x => x.Total);
                var totalNc = porMesNc.GetValueOrDefault((m.Year, m.Month));
                puntos.Add(new VentaPorPeriodoDto(m.ToString("MM/yyyy"), totalF - totalNc));
            }
            return puntos;
        }

        var porDiaF = await facturas.GroupBy(c => c.Fecha.Date)
            .Select(g => new { Fecha = g.Key, Total = g.Sum(x => x.Total) }).ToDictionaryAsync(x => x.Fecha, x => x.Total, ct);
        var porDiaNc = await notasCredito.GroupBy(c => c.Fecha.Date)
            .Select(g => new { Fecha = g.Key, Total = g.Sum(x => x.Total) }).ToDictionaryAsync(x => x.Fecha, x => x.Total, ct);

        var dias = new List<VentaPorPeriodoDto>();
        for (var f = desde; f < hasta; f = f.AddDays(1))
            dias.Add(new VentaPorPeriodoDto(f.ToString("dd/MM"), porDiaF.GetValueOrDefault(f) - porDiaNc.GetValueOrDefault(f)));
        return dias;
    }

    // ---------- Familias más vendidas / sectores más consumidos / productos más vendidos ----------

    private async Task<(List<FamiliaVendidaDto> Familias, List<SectorConsumidoDto> Sectores, List<ProductoVendidoDto> Productos)>
        CalcularFamiliasSectoresYProductosAsync(IQueryable<CabeceraComprobante> facturas, CancellationToken ct)
    {
        var detalles = await (
            from d in _db.DetallesComprobantes.AsNoTracking()
            join c in facturas on new { d.IdSucursal, d.IdComprobante } equals new { c.IdSucursal, c.IdComprobante }
            join p in _db.Presentaciones.AsNoTracking() on d.IdPresentacion equals p.IdPresentacion
            join a in _db.Articulos.AsNoTracking() on p.IdArticulo equals a.IdArticulo
            select new { d.Cantidad, d.Importe, a.IdArticulo, a.CodigoInterno, a.Descripcion, a.IdSector, a.IdFamilia }
        ).ToListAsync(ct);

        if (detalles.Count == 0)
            return (new List<FamiliaVendidaDto>(), new List<SectorConsumidoDto>(), new List<ProductoVendidoDto>());

        var totalGeneral = detalles.Sum(x => x.Importe);
        var sectorNombres = await _db.Sectores.AsNoTracking().ToDictionaryAsync(s => s.IdSector, s => s.Descripcion, ct);
        var familiaNombres = await _db.Familias.AsNoTracking().ToDictionaryAsync(f => f.IdFamilia, f => f.Descripcion, ct);

        var familias = detalles.GroupBy(x => x.IdFamilia)
            .Select(g => new FamiliaVendidaDto(g.Key, familiaNombres.GetValueOrDefault(g.Key, $"Familia {g.Key}"),
                g.Sum(x => x.Importe), g.Sum(x => x.Cantidad),
                totalGeneral > 0 ? g.Sum(x => x.Importe) / totalGeneral * 100m : 0m))
            .OrderByDescending(f => f.Total)
            .Take(10)
            .ToList();

        var sectores = detalles.GroupBy(x => x.IdSector)
            .Select(g => new SectorConsumidoDto(g.Key, sectorNombres.GetValueOrDefault(g.Key, $"Sector {g.Key}"),
                g.Sum(x => x.Importe), g.Sum(x => x.Cantidad),
                totalGeneral > 0 ? g.Sum(x => x.Importe) / totalGeneral * 100m : 0m))
            .OrderByDescending(s => s.Total)
            .Take(8)
            .ToList();

        var productos = detalles.GroupBy(x => x.IdArticulo)
            .Select(g => new ProductoVendidoDto(g.Key, g.First().CodigoInterno, g.First().Descripcion,
                g.Sum(x => x.Cantidad), g.Sum(x => x.Importe)))
            .OrderByDescending(p => p.Total)
            .Take(10)
            .ToList();

        return (familias, sectores, productos);
    }

    // ---------- Top clientes ----------

    private async Task<List<TopClienteDto>> CalcularTopClientesAsync(IQueryable<CabeceraComprobante> facturas, CancellationToken ct)
    {
        var agrupado = await facturas.GroupBy(c => c.IdCliente)
            .Select(g => new { IdCliente = g.Key, Total = g.Sum(x => x.Total), Cantidad = g.Count() })
            .OrderByDescending(x => x.Total)
            .Take(10)
            .ToListAsync(ct);

        var ids = agrupado.Where(x => x.IdCliente.HasValue).Select(x => x.IdCliente!.Value).ToList();
        var nombres = await _db.Clientes.AsNoTracking().Where(c => ids.Contains(c.IdCliente))
            .ToDictionaryAsync(c => c.IdCliente, c => c.NombreFantasia ?? c.Descripcion, ct);

        return agrupado
            .Select(x => new TopClienteDto(x.IdCliente,
                x.IdCliente.HasValue ? nombres.GetValueOrDefault(x.IdCliente.Value, $"Cliente {x.IdCliente}") : "Consumidor final",
                x.Total, x.Cantidad))
            .ToList();
    }

    // ---------- Efectividad de ofertas ----------

    private async Task<List<OfertaEfectividadDto>> CalcularEfectividadOfertasAsync(
        IQueryable<CabeceraComprobante> facturas, CancellationToken ct)
    {
        var lineasConOferta = await (
            from c in facturas
            where c.IdOperacion != null
            join d in _db.DetallesOperaciones.AsNoTracking()
                on new { c.IdSucursal, IdOperacion = c.IdOperacion!.Value } equals new { d.IdSucursal, d.IdOperacion }
            where d.OfertasAplicadas != null && d.OfertasAplicadas != "" && d.OfertasAplicadas != "[]"
            select new { d.OfertasAplicadas, d.Descuento, Importe = d.Precio * d.Cantidad - d.Descuento }
        ).ToListAsync(ct);

        var acumulado = new Dictionary<string, (int Veces, decimal Descuento, decimal Importe)>();
        foreach (var linea in lineasConOferta)
        {
            List<string>? nombres;
            try { nombres = JsonSerializer.Deserialize<List<string>>(linea.OfertasAplicadas!); }
            catch (JsonException) { continue; } // trace corrupto o de un formato viejo: se descarta, no rompe el dashboard
            if (nombres is null || nombres.Count == 0) continue;

            // El descuento de la línea no distingue cuánto aportó cada oferta cuando se acumulan
            // varias: se reparte por igual entre las que aparecen. El importe de línea, en cambio, se
            // cuenta completo para cada una — mide alcance (a cuánta venta llegó), no se puede sumar
            // entre ofertas de la misma línea sin duplicar plata.
            var descuentoPorOferta = linea.Descuento / nombres.Count;
            foreach (var nombre in nombres)
            {
                (int Veces, decimal Descuento, decimal Importe) actual =
                    acumulado.TryGetValue(nombre, out var previo) ? previo : (0, 0m, 0m);
                acumulado[nombre] = (actual.Veces + 1, actual.Descuento + descuentoPorOferta, actual.Importe + linea.Importe);
            }
        }

        return acumulado
            .Select(kv => new OfertaEfectividadDto(kv.Key, kv.Value.Veces, kv.Value.Descuento, kv.Value.Importe))
            .OrderByDescending(o => o.DescuentoOtorgado)
            .Take(10)
            .ToList();
    }
}
