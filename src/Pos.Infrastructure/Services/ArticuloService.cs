using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Catalogo;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class ArticuloService : IArticuloService
{
    /// <summary>Tope duro de filas del listado (el frontend lo avisa cuando se topea).</summary>
    public const int MaxResultados = 500;

    private readonly PosDbContext _db;
    private readonly IImageBank _images;

    public ArticuloService(PosDbContext db, IImageBank images)
    {
        _db = db;
        _images = images;
    }

    public async Task<IReadOnlyList<ArticuloListItem>> GetAllAsync(ArticuloFiltro? filtro = null, CancellationToken ct = default)
    {
        var q = _db.Articulos.AsNoTracking()
            .Include(a => a.Sector).Include(a => a.Linea).Include(a => a.Familia).Include(a => a.ModoIva)
            .Include(a => a.Presentaciones).ThenInclude(p => p.Barras)
            .AsQueryable();

        if (filtro is not null)
        {
            if (!string.IsNullOrWhiteSpace(filtro.Texto))
            {
                // Se busca por código, descripción y código de barra (el operador escanea el
                // producto para encontrarlo, igual que en la búsqueda de Caja).
                var f = filtro.Texto.Trim();
                q = q.Where(a => a.Descripcion.Contains(f) || a.CodigoInterno.Contains(f)
                    || a.Presentaciones.Any(p => p.Barras.Any(b => b.CodigoBarra.Contains(f))));
            }
            if (filtro.IdSector is int s) q = q.Where(a => a.IdSector == s);
            if (filtro.IdLinea is int l) q = q.Where(a => a.IdLinea == l);
            if (filtro.IdFamilia is int fam) q = q.Where(a => a.IdFamilia == fam);
            if (filtro.Activo is bool act) q = q.Where(a => a.Activo == act);
        }

        // Tope duro igual que ClienteService: el ABM es para buscar y editar, no para volcar el
        // catálogo entero (un mayorista puede tener miles de artículos). Quien llame puede pedir
        // menos (filtro.Max) — nunca más.
        var tope = Math.Clamp(filtro?.Max ?? MaxResultados, 1, MaxResultados);
        var articulos = await q.OrderBy(a => a.Descripcion).Take(tope).ToListAsync(ct);

        return articulos.Select(a =>
        {
            var barras = a.Presentaciones.SelectMany(p => p.Barras).ToList();
            return new ArticuloListItem(
                a.IdArticulo, a.CodigoInterno, a.Descripcion,
                a.IdSector, a.IdLinea, a.IdFamilia, a.IdModoIva,
                a.Sector?.Descripcion, a.Linea?.Descripcion, a.Familia?.Descripcion, a.ModoIva?.Descripcion,
                (int)a.UnidadMedida, a.ContenidoNetoUnitario,
                a.Presentaciones.Count, barras.Count, barras.FirstOrDefault()?.CodigoBarra,
                a.Activo, _images.BuildImageUrl(a.CodigoInterno).ToString());
        }).ToList();
    }

    public async Task<ArticuloDetail?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var a = await _db.Articulos.AsNoTracking()
            .Include(x => x.Presentaciones).ThenInclude(p => p.Barras)
            .FirstOrDefaultAsync(x => x.IdArticulo == id, ct);
        if (a is null) return null;

        var presentaciones = a.Presentaciones.Select(p => new PresentacionDto(
            p.IdPresentacion, p.UnidadXBulto, p.DescripcionTicket,
            p.Barras.Select(b => new BarraDto(b.IdBarra, b.CodigoBarra, (int)b.Tipo)).ToList())).ToList();

        return new ArticuloDetail(a.IdArticulo, a.CodigoInterno, a.Descripcion,
            a.IdSector, a.IdLinea, a.IdFamilia, a.IdModoIva, a.Activo,
            _images.BuildImageUrl(a.CodigoInterno).ToString(),
            (int)a.UnidadMedida, a.ContenidoNetoUnitario, presentaciones);
    }

    public async Task<int> CreateAsync(ArticuloInput input, CancellationToken ct = default)
    {
        if (await _db.Articulos.AnyAsync(a => a.CodigoInterno == input.CodigoInterno, ct))
            throw new DomainException("CODIGO_DUPLICADO", $"Ya existe un artículo con código {input.CodigoInterno}.");
        await ValidarFamiliaDelSectorAsync(input.IdSector, input.IdFamilia, ct);

        var articulo = new Articulo
        {
            CodigoInterno = input.CodigoInterno.Trim(),
            Descripcion = input.Descripcion.Trim(),
            IdSector = input.IdSector,
            IdLinea = input.IdLinea,
            IdFamilia = input.IdFamilia,
            IdModoIva = input.IdModoIva,
            Activo = input.Activo,
            UnidadMedida = MapUnidadMedida(input.UnidadMedida),
            ContenidoNetoUnitario = input.ContenidoNetoUnitario,
            Presentaciones = MapPresentaciones(input.Presentaciones)
        };
        _db.Articulos.Add(articulo);
        await _db.SaveChangesAsync(ct);
        return articulo.IdArticulo;
    }

    public async Task<bool> UpdateAsync(int id, ArticuloInput input, CancellationToken ct = default)
    {
        var articulo = await _db.Articulos.FirstOrDefaultAsync(a => a.IdArticulo == id, ct);
        if (articulo is null) return false;
        await ValidarFamiliaDelSectorAsync(input.IdSector, input.IdFamilia, ct);

        // Se actualiza SOLO la cabecera. Las presentaciones/barras no se reemplazan en el update
        // porque pueden estar referenciadas por Precios/Comprobantes (FK). Su edición requiere un
        // endpoint dedicado con merge por id (pendiente de Fase 1).
        articulo.CodigoInterno = input.CodigoInterno.Trim();
        articulo.Descripcion = input.Descripcion.Trim();
        articulo.IdSector = input.IdSector;
        articulo.IdLinea = input.IdLinea;
        articulo.IdFamilia = input.IdFamilia;
        articulo.IdModoIva = input.IdModoIva;
        articulo.Activo = input.Activo;
        articulo.UnidadMedida = MapUnidadMedida(input.UnidadMedida);
        articulo.ContenidoNetoUnitario = input.ContenidoNetoUnitario;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var articulo = await _db.Articulos.FirstOrDefaultAsync(a => a.IdArticulo == id, ct);
        if (articulo is null) return false;
        articulo.Activo = false; // baja lógica
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// La familia cuelga de un sector, así que no puede elegirse una familia de OTRO sector (el
    /// front ya filtra el combo, esto cierra la puerta por API). La familia sin sector —el cajón
    /// "SIN FAMILIA" de los artículos sin clasificar— se acepta con cualquier sector.
    /// </summary>
    private async Task ValidarFamiliaDelSectorAsync(int idSector, int idFamilia, CancellationToken ct)
    {
        var familia = await _db.Familias.AsNoTracking().Include(f => f.Sector)
            .FirstOrDefaultAsync(f => f.IdFamilia == idFamilia, ct);
        if (familia is null)
            throw new DomainException("FAMILIA_INEXISTENTE", "La familia indicada no existe.");
        if (familia.IdSector is int s && s != idSector)
            throw new DomainException("FAMILIA_DE_OTRO_SECTOR",
                $"La familia \"{familia.Descripcion}\" pertenece al sector {familia.Sector?.Descripcion}.");
    }

    private static UnidadMedida MapUnidadMedida(int valor) =>
        Enum.IsDefined(typeof(UnidadMedida), valor) ? (UnidadMedida)valor : UnidadMedida.Ninguna;

    private static List<Presentacion> MapPresentaciones(List<PresentacionInput> input) =>
        input.Select(p => new Presentacion
        {
            UnidadXBulto = p.UnidadXBulto <= 0 ? 1m : p.UnidadXBulto,
            DescripcionTicket = p.DescripcionTicket,
            Barras = p.Barras.Select(b => new Barra
            {
                CodigoBarra = b.CodigoBarra.Trim(),
                Tipo = Enum.IsDefined(typeof(TipoBarra), b.Tipo) ? (TipoBarra)b.Tipo : TipoBarra.Ean13
            }).ToList()
        }).ToList();
}
