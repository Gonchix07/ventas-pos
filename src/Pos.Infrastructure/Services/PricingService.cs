using Microsoft.EntityFrameworkCore;
using Pos.Application.Pricing;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class PricingService : IPricingService
{
    private readonly PosDbContext _db;
    public PricingService(PosDbContext db) => _db = db;

    public async Task<ResolverPrecioResponse> ResolverPrecioAsync(ResolverPrecioRequest req, CancellationToken ct = default)
    {
        var fecha = DateTime.Now;

        // Candidatos: precios de la presentación en listas de la sucursal.
        var candidatos = await (
            from p in _db.Precios.AsNoTracking().Where(x => x.IdPresentacion == req.IdPresentacion)
            join l in _db.ListasPrecios.AsNoTracking().Where(x => x.IdSucursal == req.IdSucursal)
                on p.IdListaPrecio equals l.IdListaPrecio
            select new CandidatoPrecio(l.Tipo, l.Prioridad, l.FechaInicio, l.FechaFin, p.PrecioFinal,
                p.ImpuestoInterno, l.IdListaPrecio)
        ).ToListAsync(ct);

        ConvenioInfo? convenio = null;
        if (req.IdCliente is int idc)
        {
            var cv = await _db.Convenios.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdSucursal == req.IdSucursal && x.IdCliente == idc, ct);

            // Lista propia del cliente: la del Convenio si tiene una asignada; si no, la de su tipo
            // de tarjeta activa — mismo resguardo que ya usa la pantalla de búsqueda de cliente en
            // CajaService.BuscarClienteAsync (ver comentario ahí) para decidir qué lista MOSTRARLE.
            // Sin este resguardo ACÁ, un cliente que está en una lista solo por el tipo de tarjeta
            // (sin Convenio propio) nunca cobraba el precio de esa lista: caía en el ranking
            // genérico de todas las listas de la sucursal, que puede ganarle otra lista Base — el
            // cajero veía "AZUL" en pantalla pero la venta se cobraba de otra lista.
            var idListaPropia = cv?.IdListaPrecio;
            if (idListaPropia is null)
            {
                idListaPropia = await (
                    from t in _db.TarjetasClientes.AsNoTracking().Where(t => t.IdCliente == idc && t.Activa)
                    join tt in _db.TiposTarjeta.AsNoTracking() on t.IdTipoTarjeta equals tt.IdTipoTarjeta
                    where tt.IdListaPrecio != null
                    select tt.IdListaPrecio
                ).FirstOrDefaultAsync(ct);
            }

            decimal? precioLista = null;
            if (idListaPropia is int idl)
            {
                precioLista = await _db.Precios.AsNoTracking()
                    .Where(p => p.IdListaPrecio == idl && p.IdPresentacion == req.IdPresentacion)
                    .Select(p => (decimal?)p.PrecioFinal).FirstOrDefaultAsync(ct);
                if (precioLista is null) idListaPropia = null; // sin precio ahí: no es el origen real
            }

            if (cv is not null || precioLista is not null)
                convenio = new ConvenioInfo(cv?.Descuento ?? 0m, precioLista, idListaPropia);
        }

        var r = CalculadoraPrecios.Resolver(candidatos, fecha, convenio);
        // TieneConvenio = el convenio se APLICÓ a este precio (no "el cliente tiene convenio"): con un
        // precio de folder el convenio no entra, y la caja cobra PrecioVigente.
        return new ResolverPrecioResponse(r.Encontrado, r.PrecioVigente, r.ImpuestoInterno,
            r.PrecioConvenio, r.AplicoConvenio, r.IdListaPrecio);
    }

    public async Task<AplicarOfertasResponse> AplicarOfertasAsync(AplicarOfertasRequest req, CancellationToken ct = default)
    {
        var fecha = DateTime.Now;

        // Datos de artículo para cada presentación pedida.
        var idsPres = req.Lineas.Select(l => l.IdPresentacion).Distinct().ToList();
        var infoPres = await (
            from pr in _db.Presentaciones.AsNoTracking().Where(x => idsPres.Contains(x.IdPresentacion))
            join a in _db.Articulos.AsNoTracking() on pr.IdArticulo equals a.IdArticulo
            select new { pr.IdPresentacion, a.IdArticulo, a.IdSector, a.IdLinea, a.IdFamilia }
        ).ToDictionaryAsync(x => x.IdPresentacion, ct);

        var lineas = new List<LineaPedido>();
        for (int i = 0; i < req.Lineas.Count; i++)
        {
            var l = req.Lineas[i];
            if (!infoPres.TryGetValue(l.IdPresentacion, out var info)) continue;
            lineas.Add(new LineaPedido(i, info.IdArticulo, info.IdSector, info.IdLinea, info.IdFamilia,
                l.IdPresentacion, l.Cantidad, l.PrecioUnit));
        }

        // Ofertas vigentes de la sucursal, con alcances y acciones (con los items de canasta).
        var cabeceras = await _db.CabecerasOfertas.AsNoTracking()
            .Where(o => o.IdSucursal == req.IdSucursal && o.FechaInicio <= fecha && o.FechaFin >= fecha)
            .Include(o => o.Alcances)
            .Include(o => o.Acciones).ThenInclude(a => a.Items)
            .ToListAsync(ct);

        // El comportamiento sale del Codigo del tipo (tabla chica, se trae entera), NO de los campos
        // cargados ni del IdTipoOferta, que es un id de datos y puede diferir entre instalaciones.
        var codigos = await _db.TiposOferta.AsNoTracking()
            .ToDictionaryAsync(t => t.IdTipoOferta, t => (TipoOfertaEnum)t.Codigo, ct);

        var ofertas = new List<OfertaDef>();
        foreach (var cab in cabeceras)
        {
            var alcances = cab.Alcances.Select(a =>
                new AlcanceDef(a.IdCluster, a.IdLinea, a.IdSector, a.IdFamilia, a.IdArticulo, a.EsExcepcion)).ToList();

            foreach (var ac in cab.Acciones)
            {
                if (!codigos.TryGetValue(ac.IdTipoOferta, out var tipo)) continue;
                var items = ac.Items
                    .Select(i => new ItemCanastaDef(i.IdArticulo, i.Cantidad, (RolItemCanasta)i.Rol)).ToList();
                ofertas.Add(new OfertaDef(cab.IdOferta, cab.Descripcion, cab.Acumula, tipo,
                    ac.IdPresentacion, ac.Porcentaje, ac.MontoFijo, ac.CantidadMin, ac.CantidadBonif,
                    alcances, items));
            }
        }

        ISet<int> clusters = req.IdCliente is int idc2
            ? (await _db.ClusterClientes.AsNoTracking().Where(c => c.IdCliente == idc2)
                .Select(c => c.IdCluster).ToListAsync(ct)).ToHashSet()
            : new HashSet<int>();

        var resultado = MotorOfertas.Aplicar(lineas, ofertas, clusters);

        var lineasResp = resultado.Select(r =>
        {
            var orig = req.Lineas[r.Indice];
            return new LineaOfertaResponse(orig.IdPresentacion, orig.Cantidad, orig.PrecioUnit,
                r.Bruto, r.Descuento, r.Neto,
                r.Ofertas.Select(o => new OfertaAplicadaDto(o.IdOferta, o.Descripcion, o.Descuento)).ToList());
        }).ToList();

        return new AplicarOfertasResponse(lineasResp,
            lineasResp.Sum(l => l.Bruto), lineasResp.Sum(l => l.Descuento), lineasResp.Sum(l => l.Neto));
    }
}
