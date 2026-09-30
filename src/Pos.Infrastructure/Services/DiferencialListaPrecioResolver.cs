using Microsoft.EntityFrameworkCore;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Resuelve el precio de las listas Tipo=Enlazada de una sucursal para una presentación puntual:
/// como no tienen filas propias en Precios, arma el candidato tomando el precio de su ListaBase (si
/// esa lista tiene precio cargado para la presentación — si no, la Enlazada no compite) y
/// aplicándole el % de <see cref="Pos.Domain.Entities.DiferencialListaPrecio"/> que matchee: artículo
/// puntual primero, si no hay la línea del artículo, si no hay ninguno 0%. Compartido por
/// PricingService (venta en Caja) y EtiquetaService (impresión de etiquetas) para no duplicar la
/// regla de prioridad artículo &gt; línea en dos lugares.
/// </summary>
public static class DiferencialListaPrecioResolver
{
    /// <summary>Un candidato por cada lista Enlazada de la sucursal que pudo resolver precio (su
    /// ListaBase tiene fila para esta presentación); las que no, se omiten en vez de competir con 0.</summary>
    public static async Task<List<CandidatoPrecio>> ResolverCandidatosAsync(
        PosDbContext db, int idSucursal, int idPresentacion, CancellationToken ct)
    {
        var enlazadas = await db.ListasPrecios.AsNoTracking()
            .Where(l => l.IdSucursal == idSucursal && l.Tipo == TipoListaPrecio.Enlazada && l.IdListaBase != null)
            .ToListAsync(ct);
        if (enlazadas.Count == 0) return new List<CandidatoPrecio>();

        var articulo = await db.Presentaciones.AsNoTracking()
            .Where(pr => pr.IdPresentacion == idPresentacion)
            .Join(db.Articulos.AsNoTracking(), pr => pr.IdArticulo, a => a.IdArticulo,
                (pr, a) => new { a.IdArticulo, a.IdLinea })
            .FirstOrDefaultAsync(ct);
        if (articulo is null) return new List<CandidatoPrecio>();

        var resultado = new List<CandidatoPrecio>();
        foreach (var lista in enlazadas)
        {
            var precioBase = await db.Precios.AsNoTracking()
                .Where(p => p.IdListaPrecio == lista.IdListaBase!.Value && p.IdPresentacion == idPresentacion)
                .Select(p => new { p.PrecioFinal, p.ImpuestoInterno })
                .FirstOrDefaultAsync(ct);
            if (precioBase is null) continue; // la base no tiene precio acá: la Enlazada tampoco.

            var porcentaje = await ObtenerPorcentajeAsync(db, lista.IdListaPrecio, articulo.IdArticulo, articulo.IdLinea, ct);
            var precioFinal = Math.Round(precioBase.PrecioFinal * (1 + porcentaje / 100m), 4, MidpointRounding.AwayFromZero);
            resultado.Add(new CandidatoPrecio(lista.Tipo, lista.Prioridad, lista.FechaInicio, lista.FechaFin,
                precioFinal, precioBase.ImpuestoInterno, lista.IdListaPrecio));
        }
        return resultado;
    }

    /// <summary>% de recargo a aplicar: el del artículo puntual si hay uno cargado, si no el de su
    /// línea, si no hay ninguno de los dos 0 (la Enlazada cobra igual que la Base para ese artículo).</summary>
    public static async Task<decimal> ObtenerPorcentajeAsync(
        PosDbContext db, int idListaPrecio, int idArticulo, int? idLinea, CancellationToken ct)
    {
        var porArticulo = await db.DiferencialesListaPrecio.AsNoTracking()
            .Where(d => d.IdListaPrecio == idListaPrecio && d.IdArticulo == idArticulo)
            .Select(d => (decimal?)d.Porcentaje).FirstOrDefaultAsync(ct);
        if (porArticulo is decimal pa) return pa;

        if (idLinea is int linea)
        {
            var porLinea = await db.DiferencialesListaPrecio.AsNoTracking()
                .Where(d => d.IdListaPrecio == idListaPrecio && d.IdLinea == linea)
                .Select(d => (decimal?)d.Porcentaje).FirstOrDefaultAsync(ct);
            if (porLinea is decimal pl) return pl;
        }
        return 0m;
    }

    /// <summary>Precio final de una única lista (puntual, no todo el ranking de candidatos) para una
    /// presentación puntual: si es Enlazada, delega en su base + diferencial; si no, el de la propia
    /// tabla Precios. Usado por EtiquetaService para "precio por tipo de tarjeta" (una sola lista
    /// puntual, no la resolución completa por prioridad).</summary>
    public static async Task<(decimal PrecioFinal, decimal ImpuestoInterno)?> ResolverPrecioUnicoAsync(
        PosDbContext db, int idListaPrecio, int idPresentacion, CancellationToken ct)
    {
        var lista = await db.ListasPrecios.AsNoTracking()
            .Where(l => l.IdListaPrecio == idListaPrecio)
            .Select(l => new { l.Tipo, l.IdListaBase })
            .FirstOrDefaultAsync(ct);
        if (lista is null) return null;

        if (lista.Tipo != TipoListaPrecio.Enlazada || lista.IdListaBase is null)
        {
            var precio = await db.Precios.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdListaPrecio == idListaPrecio && p.IdPresentacion == idPresentacion, ct);
            return precio is null ? null : (precio.PrecioFinal, precio.ImpuestoInterno);
        }

        var precioBase = await db.Precios.AsNoTracking()
            .Where(p => p.IdListaPrecio == lista.IdListaBase.Value && p.IdPresentacion == idPresentacion)
            .Select(p => new { p.PrecioFinal, p.ImpuestoInterno })
            .FirstOrDefaultAsync(ct);
        if (precioBase is null) return null;

        var articulo = await db.Presentaciones.AsNoTracking()
            .Where(pr => pr.IdPresentacion == idPresentacion)
            .Join(db.Articulos.AsNoTracking(), pr => pr.IdArticulo, a => a.IdArticulo,
                (pr, a) => new { a.IdArticulo, a.IdLinea })
            .FirstOrDefaultAsync(ct);
        if (articulo is null) return null;

        var porcentaje = await ObtenerPorcentajeAsync(db, idListaPrecio, articulo.IdArticulo, articulo.IdLinea, ct);
        var precioFinal = Math.Round(precioBase.PrecioFinal * (1 + porcentaje / 100m), 4, MidpointRounding.AwayFromZero);
        return (precioFinal, precioBase.ImpuestoInterno);
    }
}
