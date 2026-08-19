using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Precios;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class ListaPrecioService : IListaPrecioService
{
    /// <summary>Tope de filas del listado de precios (mismo criterio que Clientes).</summary>
    public const int MaxResultados = 50;

    private readonly PosDbContext _db;
    public ListaPrecioService(PosDbContext db) => _db = db;

    private static string TipoDesc(TipoListaPrecio t) => t switch
    {
        TipoListaPrecio.Folder => "Folder",
        TipoListaPrecio.Temporal => "Temporal",
        _ => "Base"
    };

    public async Task<IReadOnlyList<ListaPrecioDto>> GetAllAsync(CancellationToken ct = default)
    {
        var listas = await _db.ListasPrecios.AsNoTracking()
            .OrderByDescending(l => l.Prioridad).ThenBy(l => l.CodigoInterno).ToListAsync(ct);

        var sucursales = await _db.Sucursales.AsNoTracking().ToDictionaryAsync(s => s.IdSucursal, s => s.Descripcion, ct);
        var counts = await _db.Precios.AsNoTracking()
            .GroupBy(p => p.IdListaPrecio).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);

        return listas.Select(l => new ListaPrecioDto(
            l.IdListaPrecio, l.IdSucursal, sucursales.GetValueOrDefault(l.IdSucursal),
            l.CodigoInterno, (int)l.Tipo, TipoDesc(l.Tipo), l.Prioridad,
            l.FechaInicio, l.FechaFin, counts.GetValueOrDefault(l.IdListaPrecio))).ToList();
    }

    public async Task<ListaPrecioDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var l = await _db.ListasPrecios.AsNoTracking().FirstOrDefaultAsync(x => x.IdListaPrecio == id, ct);
        if (l is null) return null;
        var suc = await _db.Sucursales.AsNoTracking().Where(s => s.IdSucursal == l.IdSucursal)
            .Select(s => s.Descripcion).FirstOrDefaultAsync(ct);
        var count = await _db.Precios.CountAsync(p => p.IdListaPrecio == id, ct);
        return new ListaPrecioDto(l.IdListaPrecio, l.IdSucursal, suc, l.CodigoInterno,
            (int)l.Tipo, TipoDesc(l.Tipo), l.Prioridad, l.FechaInicio, l.FechaFin, count);
    }

    public async Task<int> CreateAsync(ListaPrecioInput input, CancellationToken ct = default)
    {
        if (await _db.ListasPrecios.AnyAsync(l => l.IdSucursal == input.IdSucursal && l.CodigoInterno == input.CodigoInterno, ct))
            throw new DomainException("CODIGO_DUPLICADO", $"Ya existe una lista {input.CodigoInterno} en esa sucursal.");

        var lista = new ListaPrecio
        {
            IdSucursal = input.IdSucursal,
            CodigoInterno = input.CodigoInterno.Trim(),
            Tipo = Enum.IsDefined(typeof(TipoListaPrecio), input.Tipo) ? (TipoListaPrecio)input.Tipo : TipoListaPrecio.Base,
            Prioridad = input.Prioridad,
            FechaInicio = input.FechaInicio,
            FechaFin = input.FechaFin
        };
        _db.ListasPrecios.Add(lista);
        await _db.SaveChangesAsync(ct);
        return lista.IdListaPrecio;
    }

    public async Task<bool> UpdateAsync(int id, ListaPrecioInput input, CancellationToken ct = default)
    {
        var lista = await _db.ListasPrecios.FirstOrDefaultAsync(l => l.IdListaPrecio == id, ct);
        if (lista is null) return false;
        lista.IdSucursal = input.IdSucursal;
        lista.CodigoInterno = input.CodigoInterno.Trim();
        lista.Tipo = Enum.IsDefined(typeof(TipoListaPrecio), input.Tipo) ? (TipoListaPrecio)input.Tipo : TipoListaPrecio.Base;
        lista.Prioridad = input.Prioridad;
        lista.FechaInicio = input.FechaInicio;
        lista.FechaFin = input.FechaFin;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var lista = await _db.ListasPrecios.FirstOrDefaultAsync(l => l.IdListaPrecio == id, ct);
        if (lista is null) return false;
        var precios = _db.Precios.Where(p => p.IdListaPrecio == id);
        _db.Precios.RemoveRange(precios);
        _db.ListasPrecios.Remove(lista);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<PrecioDto>> GetPreciosAsync(int idListaPrecio, string? texto = null,
        IReadOnlyList<int>? idsArticulos = null, CancellationToken ct = default)
    {
        var precios = _db.Precios.AsNoTracking().Where(x => x.IdListaPrecio == idListaPrecio);
        var articulos = _db.Articulos.AsNoTracking();

        // Consulta puntual por artículo (la usa el buscador para saber cuáles ya tienen precio): no
        // se topea porque el llamador pide un puñado de ids concretos.
        var puntual = idsArticulos is { Count: > 0 };
        if (puntual) articulos = articulos.Where(a => idsArticulos!.Contains(a.IdArticulo));

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            articulos = articulos.Where(a => a.Descripcion.Contains(t) || a.CodigoInterno.Contains(t));
        }

        var query =
            from p in precios
            join pr in _db.Presentaciones.AsNoTracking() on p.IdPresentacion equals pr.IdPresentacion
            join a in articulos on pr.IdArticulo equals a.IdArticulo
            orderby a.Descripcion, pr.UnidadXBulto
            select new PrecioDto(p.IdPresentacion, a.IdArticulo, a.CodigoInterno, a.Descripcion,
                pr.DescripcionTicket, pr.UnidadXBulto, p.PrecioFinal, p.ImpuestoInterno);

        // Tope: la pantalla es para buscar y corregir precios puntuales, no para volcar la lista
        // entera (AZUL tiene >16.000 precios y se renderizaban todos).
        if (!puntual) query = query.Take(MaxResultados);
        return await query.ToListAsync(ct);
    }

    public async Task<bool> UpsertPrecioAsync(int idListaPrecio, int idPresentacion, PrecioInput input, CancellationToken ct = default)
    {
        if (input.PrecioFinal < 0)
            throw new DomainException("PRECIO_INVALIDO", "El precio no puede ser negativo.");
        if (input.ImpuestoInterno < 0)
            throw new DomainException("IMPUESTO_INVALIDO", "El impuesto interno no puede ser negativo.");

        if (!await _db.ListasPrecios.AnyAsync(l => l.IdListaPrecio == idListaPrecio, ct))
            return false;
        var pres = await _db.Presentaciones.FirstOrDefaultAsync(p => p.IdPresentacion == idPresentacion, ct);
        if (pres is null)
            throw new DomainException("PRESENTACION_INEXISTENTE", "La presentación no existe.");

        var precio = await _db.Precios.FirstOrDefaultAsync(
            p => p.IdListaPrecio == idListaPrecio && p.IdPresentacion == idPresentacion, ct);

        if (precio is null)
        {
            _db.Precios.Add(new Precio
            {
                IdListaPrecio = idListaPrecio,
                IdPresentacion = idPresentacion,
                IdArticulo = pres.IdArticulo,
                PrecioFinal = input.PrecioFinal,
                ImpuestoInterno = input.ImpuestoInterno
            });
        }
        else
        {
            precio.PrecioFinal = input.PrecioFinal;
            precio.ImpuestoInterno = input.ImpuestoInterno;
            precio.IdArticulo = pres.IdArticulo;
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<PrecioAplicadoDto>?> UpsertPrecioArticuloAsync(
        int idListaPrecio, int idArticulo, PrecioArticuloInput input, CancellationToken ct = default)
    {
        if (input.PrecioUnitario < 0)
            throw new DomainException("PRECIO_INVALIDO", "El precio no puede ser negativo.");
        if (input.ImpuestoInternoUnitario < 0)
            throw new DomainException("IMPUESTO_INVALIDO", "El impuesto interno no puede ser negativo.");

        if (!await _db.ListasPrecios.AnyAsync(l => l.IdListaPrecio == idListaPrecio, ct))
            return null;

        var presentaciones = await _db.Presentaciones
            .Where(p => p.IdArticulo == idArticulo).ToListAsync(ct);
        if (presentaciones.Count == 0)
            throw new DomainException("SIN_PRESENTACIONES",
                "El artículo no tiene presentaciones a las que asignarles precio.");

        var existentes = await _db.Precios
            .Where(p => p.IdListaPrecio == idListaPrecio && p.IdArticulo == idArticulo).ToListAsync(ct);

        var aplicados = new List<PrecioAplicadoDto>();
        foreach (var pres in presentaciones.OrderBy(p => p.UnidadXBulto))
        {
            var precioFinal = PrecioPorBulto.Calcular(input.PrecioUnitario, pres.UnidadXBulto);
            var impuesto = PrecioPorBulto.Calcular(input.ImpuestoInternoUnitario, pres.UnidadXBulto);

            var precio = existentes.FirstOrDefault(p => p.IdPresentacion == pres.IdPresentacion);
            if (precio is null)
            {
                _db.Precios.Add(new Precio
                {
                    IdListaPrecio = idListaPrecio,
                    IdPresentacion = pres.IdPresentacion,
                    IdArticulo = idArticulo,
                    PrecioFinal = precioFinal,
                    ImpuestoInterno = impuesto
                });
            }
            else
            {
                precio.PrecioFinal = precioFinal;
                precio.ImpuestoInterno = impuesto;
            }

            aplicados.Add(new PrecioAplicadoDto(pres.IdPresentacion, pres.DescripcionTicket,
                pres.UnidadXBulto, precioFinal, impuesto));
        }

        // Un solo SaveChanges: o quedan todas las presentaciones del artículo o ninguna (evita
        // dejar el artículo con precios a medias si algo falla).
        await _db.SaveChangesAsync(ct);
        return aplicados;
    }

    public async Task<bool> DeletePrecioAsync(int idListaPrecio, int idPresentacion, CancellationToken ct = default)
    {
        var precio = await _db.Precios.FirstOrDefaultAsync(
            p => p.IdListaPrecio == idListaPrecio && p.IdPresentacion == idPresentacion, ct);
        if (precio is null) return false;
        _db.Precios.Remove(precio);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
