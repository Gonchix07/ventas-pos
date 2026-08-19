using Pos.Domain.Enums;

namespace Pos.Domain.Services;

public record LineaPedido(
    int Indice, int IdArticulo, int IdSector, int IdLinea, int IdFamilia,
    int IdPresentacion, decimal Cantidad, decimal PrecioUnit);

public record AlcanceDef(
    int? IdCluster, int? IdLinea, int? IdSector, int? IdFamilia, int? IdArticulo, bool EsExcepcion);

/// <summary>Renglón de una Mix Canasta: artículo, cantidad y de qué canasta es (la que activa o la que se bonifica).</summary>
public record ItemCanastaDef(int IdArticulo, decimal Cantidad, RolItemCanasta Rol);

public record OfertaDef(
    int IdOferta, string Descripcion, bool Acumula, TipoOfertaEnum Tipo,
    int? IdPresentacionAccion, decimal? Porcentaje, decimal? MontoFijo,
    decimal? CantidadMin, decimal? CantidadBonif, IReadOnlyList<AlcanceDef> Alcances,
    IReadOnlyList<ItemCanastaDef>? Items = null);

public record OfertaAplicada(int IdOferta, string Descripcion, decimal Descuento);

public record ResultadoLinea(
    int Indice, decimal Bruto, decimal Descuento, decimal Neto, IReadOnlyList<OfertaAplicada> Ofertas);

/// <summary>
/// Motor de ofertas (lógica pura). Evalúa qué ofertas aplican a cada línea según su alcance
/// (cluster del cliente / sector-línea-familia-artículo), resuelve excepciones y acumulación,
/// y calcula el descuento.
/// Tipos: Descuento (%/monto por línea), 2x1, Segunda unidad al %, Bonificación legacy "N+M"
/// —todos por línea— y Mix Canasta, que es la única que mira el carrito completo.
/// </summary>
public static class MotorOfertas
{
    /// <summary>% bonificado de la 2ª unidad cuando la acción no lo especifica.</summary>
    public const decimal PorcentajeSegundaUnidadPorDefecto = 70m;

    public static IReadOnlyList<ResultadoLinea> Aplicar(
        IEnumerable<LineaPedido> lineas, IEnumerable<OfertaDef> ofertas, ISet<int> clustersCliente)
    {
        var lista = ofertas.ToList();
        var pedido = lineas.ToList();
        var resultado = new List<ResultadoLinea>();

        // Las canastas se resuelven antes que nada: son una condición sobre TODO el carrito, no
        // sobre la línea suelta. Lo que devuelven ya viene repartido por línea, así que después
        // entra en el mismo juego de acumulables / mejor-no-acumulable que el resto.
        var porCanasta = ResolverCanastas(pedido, lista, clustersCliente);

        foreach (var linea in pedido)
        {
            var bruto = linea.PrecioUnit * linea.Cantidad;

            var acumulables = new List<OfertaAplicada>();
            OfertaAplicada? mejorNoAcum = null;

            void Considerar(OfertaDef o, decimal desc)
            {
                if (desc <= 0m) return;
                var ap = new OfertaAplicada(o.IdOferta, o.Descripcion, desc);
                if (o.Acumula) acumulables.Add(ap);
                else if (mejorNoAcum is null || desc > mejorNoAcum.Descuento) mejorNoAcum = ap;
            }

            foreach (var o in lista.Where(o => o.Tipo != TipoOfertaEnum.MixCanasta))
            {
                if (!Aplica(o, linea, clustersCliente)) continue;
                Considerar(o, CalcularDescuento(o, linea));
            }

            if (porCanasta.TryGetValue(linea.Indice, out var deCanasta))
                foreach (var (oferta, desc) in deCanasta) Considerar(oferta, desc);

            var aplicadas = new List<OfertaAplicada>(acumulables);
            if (mejorNoAcum is not null) aplicadas.Add(mejorNoAcum);

            var descuento = Math.Min(aplicadas.Sum(a => a.Descuento), bruto);
            var neto = Redondear(bruto - descuento);
            resultado.Add(new ResultadoLinea(linea.Indice, Redondear(bruto), Redondear(descuento), neto, aplicadas));
        }

        return resultado;
    }

    /// <summary>
    /// Para cada oferta Mix Canasta: cuenta cuántas veces entra en el carrito la canasta que ACTIVA
    /// (el mínimo entre sus renglones) y, si entra al menos una vez, bonifica al 100% las cantidades
    /// de la canasta PREMIADA sobre las líneas de esos artículos, al precio de cada línea.
    /// Las dos canastas pueden tener artículos distintos; lo que no esté en el carrito no se puede
    /// bonificar, así que el premio se recorta a las unidades realmente compradas.
    /// </summary>
    private static Dictionary<int, List<(OfertaDef Oferta, decimal Descuento)>> ResolverCanastas(
        IReadOnlyList<LineaPedido> lineas, IReadOnlyList<OfertaDef> ofertas, ISet<int> clusters)
    {
        var porLinea = new Dictionary<int, List<(OfertaDef, decimal)>>();

        foreach (var o in ofertas.Where(o => o.Tipo == TipoOfertaEnum.MixCanasta))
        {
            var items = o.Items ?? Array.Empty<ItemCanastaDef>();
            var condicion = items.Where(i => i.Rol == RolItemCanasta.Condicion && i.Cantidad > 0m).ToList();
            var premio = items.Where(i => i.Rol == RolItemCanasta.Bonificado && i.Cantidad > 0m).ToList();
            if (condicion.Count == 0 || premio.Count == 0) continue;

            // Solo cuentan las líneas dentro del alcance (típicamente: el cluster del cliente).
            var candidatas = lineas.Where(l => Aplica(o, l, clusters)).ToList();

            var repeticiones = condicion
                .Select(i => Math.Floor(Disponible(candidatas, i.IdArticulo) / i.Cantidad))
                .Min();
            if (repeticiones <= 0m) continue;

            foreach (var item in premio)
            {
                var gratis = repeticiones * item.Cantidad;
                foreach (var l in candidatas.Where(x => x.IdArticulo == item.IdArticulo))
                {
                    if (gratis <= 0m) break;
                    var unidades = Math.Min(gratis, l.Cantidad);
                    gratis -= unidades;
                    var desc = Redondear(unidades * l.PrecioUnit);
                    if (desc <= 0m) continue;
                    if (!porLinea.TryGetValue(l.Indice, out var lst))
                        porLinea[l.Indice] = lst = new List<(OfertaDef, decimal)>();
                    lst.Add((o, desc));
                }
            }
        }

        return porLinea;
    }

    private static decimal Disponible(IEnumerable<LineaPedido> lineas, int idArticulo) =>
        lineas.Where(l => l.IdArticulo == idArticulo).Sum(l => l.Cantidad);

    private static bool Aplica(OfertaDef o, LineaPedido l, ISet<int> clusters)
    {
        // Si la acción es específica de una presentación, la línea debe coincidir.
        if (o.IdPresentacionAccion.HasValue && o.IdPresentacionAccion.Value != l.IdPresentacion)
            return false;

        var inclusiones = o.Alcances.Where(a => !a.EsExcepcion).ToList();
        var exclusiones = o.Alcances.Where(a => a.EsExcepcion).ToList();

        // Sin inclusiones = alcance a toda la sucursal.
        var incluida = inclusiones.Count == 0 || inclusiones.Any(a => Match(a, l, clusters));
        var excluida = exclusiones.Any(a => Match(a, l, clusters));
        return incluida && !excluida;
    }

    private static bool Match(AlcanceDef a, LineaPedido l, ISet<int> clusters)
    {
        if (a.IdCluster.HasValue && !clusters.Contains(a.IdCluster.Value)) return false;
        if (a.IdArticulo.HasValue && a.IdArticulo.Value != l.IdArticulo) return false;
        if (a.IdFamilia.HasValue && a.IdFamilia.Value != l.IdFamilia) return false;
        if (a.IdLinea.HasValue && a.IdLinea.Value != l.IdLinea) return false;
        if (a.IdSector.HasValue && a.IdSector.Value != l.IdSector) return false;
        return true;
    }

    private static decimal CalcularDescuento(OfertaDef o, LineaPedido l)
    {
        switch (o.Tipo)
        {
            case TipoOfertaEnum.Descuento:
                if (o.Porcentaje is > 0)
                    return Redondear(l.PrecioUnit * l.Cantidad * o.Porcentaje.Value / 100m);
                if (o.MontoFijo is > 0)
                    return Redondear(Math.Min(o.MontoFijo.Value * l.Cantidad, l.PrecioUnit * l.Cantidad));
                return 0m;

            case TipoOfertaEnum.DosPorUno:
                // Cada par de unidades iguales regala una.
                return Redondear(Math.Floor(l.Cantidad / 2m) * l.PrecioUnit);

            case TipoOfertaEnum.SegundaUnidad:
                // Misma cuenta que el 2x1 pero la 2ª unidad se bonifica solo en parte.
                var porc = o.Porcentaje is > 0 ? o.Porcentaje.Value : PorcentajeSegundaUnidadPorDefecto;
                return Redondear(Math.Floor(l.Cantidad / 2m) * l.PrecioUnit * porc / 100m);

            case TipoOfertaEnum.Bonificacion:
                // Legacy "lleva (min+bonif), paga min": cada bloque regala CantidadBonif unidades.
                if (o.CantidadMin is > 0 && o.CantidadBonif is > 0)
                {
                    var bloque = o.CantidadMin.Value + o.CantidadBonif.Value;
                    var bloques = Math.Floor(l.Cantidad / bloque);
                    var gratis = bloques * o.CantidadBonif.Value;
                    return Redondear(gratis * l.PrecioUnit);
                }
                return 0m;

            case TipoOfertaEnum.MixCanasta:
            default:
                return 0m; // Se resuelve aparte, sobre el carrito completo (ver ResolverCanastas).
        }
    }

    private static decimal Redondear(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}
