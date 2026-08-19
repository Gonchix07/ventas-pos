using Microsoft.EntityFrameworkCore;
using Pos.Application.Abm;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class OfertaAdminService : IOfertaAdminService
{
    private readonly PosDbContext _db;
    public OfertaAdminService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<OfertaListItem>> GetAllAsync(int idSucursal, CancellationToken ct = default)
    {
        var ofertas = await _db.CabecerasOfertas.AsNoTracking()
            .Where(o => o.IdSucursal == idSucursal)
            .Include(o => o.Alcances).Include(o => o.Acciones)
            .OrderBy(o => o.IdOferta).ToListAsync(ct);

        return ofertas.Select(o => new OfertaListItem(o.IdSucursal, o.IdOferta, o.Descripcion,
            o.FechaInicio, o.FechaFin, o.Acumula, o.PermiteConvenio,
            o.Alcances.Count, o.Acciones.Count)).ToList();
    }

    public async Task<OfertaDetail?> GetByIdAsync(int idSucursal, int idOferta, CancellationToken ct = default)
    {
        var o = await _db.CabecerasOfertas.AsNoTracking()
            .Where(x => x.IdSucursal == idSucursal && x.IdOferta == idOferta)
            .Include(x => x.Alcances)
            .Include(x => x.Acciones).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(ct);
        if (o is null) return null;

        // Los artículos se muestran por descripción (el ABM elige contra un buscador, no por Id).
        var ids = o.Alcances.Where(a => a.IdArticulo.HasValue).Select(a => a.IdArticulo!.Value)
            .Concat(o.Acciones.SelectMany(a => a.Items).Select(i => i.IdArticulo))
            .Distinct().ToList();
        var descripciones = ids.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Articulos.AsNoTracking().Where(a => ids.Contains(a.IdArticulo))
                .ToDictionaryAsync(a => a.IdArticulo, a => a.CodigoInterno + " · " + a.Descripcion, ct);

        string? Desc(int? id) => id.HasValue && descripciones.TryGetValue(id.Value, out var d) ? d : null;

        return new OfertaDetail(o.IdSucursal, o.IdOferta, o.Descripcion, o.FechaInicio, o.FechaFin,
            o.Acumula, o.PermiteConvenio,
            o.Alcances.Select(a => new AlcanceDto(a.IdCluster, a.IdLinea, a.IdSector, a.IdFamilia, a.IdArticulo,
                a.EsExcepcion, Desc(a.IdArticulo))).ToList(),
            o.Acciones.Select(a => new AccionDto(a.IdTipoOferta, a.IdPresentacion, a.Porcentaje, a.MontoFijo,
                a.CantidadMin, a.CantidadBonif,
                a.Items.OrderBy(i => i.Rol).ThenBy(i => i.IdItem)
                    .Select(i => new ItemCanastaDto(i.IdArticulo, i.Cantidad, i.Rol, Desc(i.IdArticulo))).ToList()
            )).ToList());
    }

    public async Task<int> CreateAsync(int idSucursal, OfertaInput input, CancellationToken ct = default)
    {
        await ValidarAsync(input, ct);

        var next = (await _db.CabecerasOfertas.Where(o => o.IdSucursal == idSucursal)
            .Select(o => o.IdOferta).MaxAsync(x => (int?)x, ct) ?? 0) + 1;

        var cab = new CabeceraOferta
        {
            IdSucursal = idSucursal,
            IdOferta = next,
            IdAccion = 0, // columna legacy del SRS; no se usa
            Descripcion = input.Descripcion.Trim(),
            FechaInicio = input.FechaInicio,
            FechaFin = input.FechaFin,
            Acumula = input.Acumula,
            PermiteConvenio = input.PermiteConvenio,
            Alcances = MapAlcances(idSucursal, next, input.Alcances),
            Acciones = MapAcciones(idSucursal, next, input.Acciones)
        };
        _db.CabecerasOfertas.Add(cab);
        await _db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<bool> UpdateAsync(int idSucursal, int idOferta, OfertaInput input, CancellationToken ct = default)
    {
        await ValidarAsync(input, ct);

        var cab = await _db.CabecerasOfertas
            .Where(x => x.IdSucursal == idSucursal && x.IdOferta == idOferta)
            .Include(x => x.Alcances).Include(x => x.Acciones).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(ct);
        if (cab is null) return false;

        cab.Descripcion = input.Descripcion.Trim();
        cab.FechaInicio = input.FechaInicio;
        cab.FechaFin = input.FechaFin;
        cab.Acumula = input.Acumula;
        cab.PermiteConvenio = input.PermiteConvenio;

        _db.AlcancesOfertas.RemoveRange(cab.Alcances);
        _db.ItemsOfertas.RemoveRange(cab.Acciones.SelectMany(a => a.Items));
        _db.AccionesOfertas.RemoveRange(cab.Acciones);
        cab.Alcances = MapAlcances(idSucursal, idOferta, input.Alcances);
        cab.Acciones = MapAcciones(idSucursal, idOferta, input.Acciones);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int idSucursal, int idOferta, CancellationToken ct = default)
    {
        var cab = await _db.CabecerasOfertas
            .Where(x => x.IdSucursal == idSucursal && x.IdOferta == idOferta)
            .Include(x => x.Alcances).Include(x => x.Acciones).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(ct);
        if (cab is null) return false;
        _db.AlcancesOfertas.RemoveRange(cab.Alcances);
        _db.ItemsOfertas.RemoveRange(cab.Acciones.SelectMany(a => a.Items));
        _db.AccionesOfertas.RemoveRange(cab.Acciones);
        _db.CabecerasOfertas.Remove(cab);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static List<AlcanceOferta> MapAlcances(int idSucursal, int idOferta, List<AlcanceDto> input) =>
        input.Select(a => new AlcanceOferta
        {
            IdSucursal = idSucursal, IdOferta = idOferta,
            IdCluster = a.IdCluster, IdLinea = a.IdLinea, IdSector = a.IdSector,
            IdFamilia = a.IdFamilia, IdArticulo = a.IdArticulo, EsExcepcion = a.EsExcepcion
        }).ToList();

    private static List<AccionOferta> MapAcciones(int idSucursal, int idOferta, List<AccionDto> input) =>
        input.Select(a => new AccionOferta
        {
            IdSucursal = idSucursal, IdOferta = idOferta,
            IdTipoOferta = a.IdTipoOferta, IdPresentacion = a.IdPresentacion,
            Porcentaje = a.Porcentaje, MontoFijo = a.MontoFijo,
            CantidadMin = a.CantidadMin, CantidadBonif = a.CantidadBonif,
            // Los items se agregan por navegación: el IdAccion recién existe cuando EF guarda la acción.
            Items = (a.Items ?? new List<ItemCanastaDto>()).Select(i => new ItemOferta
            {
                IdSucursal = idSucursal, IdOferta = idOferta,
                IdArticulo = i.IdArticulo, Cantidad = i.Cantidad, Rol = i.Rol
            }).ToList()
        }).ToList();

    /// <summary>
    /// Valida la oferta contra el comportamiento de cada tipo (el <c>Codigo</c> de TiposOferta,
    /// no su Id ni su descripción).
    /// </summary>
    private async Task ValidarAsync(OfertaInput input, CancellationToken ct)
    {
        if (input.Acciones.Count == 0)
            throw new DomainException("ACCION_REQUERIDA", "La oferta necesita al menos una acción.");
        if (input.FechaFin < input.FechaInicio)
            throw new DomainException("VIGENCIA_INVALIDA", "La vigencia termina antes de empezar.");

        var idsTipo = input.Acciones.Select(a => a.IdTipoOferta).Distinct().ToList();
        var tipos = await _db.TiposOferta.AsNoTracking().Where(t => idsTipo.Contains(t.IdTipoOferta))
            .ToDictionaryAsync(t => t.IdTipoOferta, t => (TipoOfertaEnum)t.Codigo, ct);

        foreach (var a in input.Acciones)
        {
            if (!tipos.TryGetValue(a.IdTipoOferta, out var tipo))
                throw new DomainException("TIPO_OFERTA_INEXISTENTE", "El tipo de oferta elegido no existe.");

            var items = a.Items ?? new List<ItemCanastaDto>();

            switch (tipo)
            {
                case TipoOfertaEnum.Descuento:
                    if (a.Porcentaje is not > 0 && a.MontoFijo is not > 0)
                        throw new DomainException("DESCUENTO_SIN_VALOR", "El descuento necesita un porcentaje mayor a 0.");
                    if (a.Porcentaje is > 100)
                        throw new DomainException("PORCENTAJE_INVALIDO", "El porcentaje de descuento no puede superar 100.");
                    break;

                case TipoOfertaEnum.SegundaUnidad:
                    if (a.Porcentaje is not null && (a.Porcentaje <= 0 || a.Porcentaje > 100))
                        throw new DomainException("PORCENTAJE_INVALIDO", "El porcentaje bonificado de la 2ª unidad debe estar entre 1 y 100.");
                    break;

                case TipoOfertaEnum.MixCanasta:
                    var condicion = items.Where(i => i.Rol == (int)RolItemCanasta.Condicion).ToList();
                    var premio = items.Where(i => i.Rol == (int)RolItemCanasta.Bonificado).ToList();
                    if (condicion.Count == 0)
                        throw new DomainException("CANASTA_SIN_CONDICION", "La canasta que activa la oferta necesita al menos un artículo.");
                    if (premio.Count == 0)
                        throw new DomainException("CANASTA_SIN_PREMIO", "La canasta bonificada necesita al menos un artículo.");
                    if (items.Any(i => i.Rol != (int)RolItemCanasta.Condicion && i.Rol != (int)RolItemCanasta.Bonificado))
                        throw new DomainException("ROL_INVALIDO", "Cada artículo de la canasta tiene que ser de la que activa o de la bonificada.");
                    if (items.Any(i => i.Cantidad <= 0))
                        throw new DomainException("CANTIDAD_INVALIDA", "Las cantidades de la canasta deben ser mayores a 0.");
                    // Repetido dentro de la MISMA canasta; el mismo artículo puede estar en las dos
                    // (ej. "llevá 3 de A y te bonificamos 1 de A").
                    if (condicion.Select(i => i.IdArticulo).Distinct().Count() != condicion.Count ||
                        premio.Select(i => i.IdArticulo).Distinct().Count() != premio.Count)
                        throw new DomainException("ARTICULO_DUPLICADO", "Hay un artículo repetido en la misma canasta.");
                    if (items.Any(i => i.IdArticulo <= 0))
                        throw new DomainException("ARTICULO_REQUERIDO", "Falta elegir el artículo en algún renglón de la canasta.");
                    break;
            }

            if (tipo != TipoOfertaEnum.MixCanasta && items.Count > 0)
                throw new DomainException("ITEMS_NO_APLICAN", "Solo Mix Canasta lleva lista de artículos con cantidades.");
        }

        // Los artículos referenciados (alcance y canasta) tienen que existir.
        var idsArticulo = input.Alcances.Where(x => x.IdArticulo.HasValue).Select(x => x.IdArticulo!.Value)
            .Concat(input.Acciones.SelectMany(a => a.Items ?? new List<ItemCanastaDto>()).Select(i => i.IdArticulo))
            .Distinct().ToList();
        if (idsArticulo.Count > 0)
        {
            var existentes = await _db.Articulos.AsNoTracking()
                .Where(x => idsArticulo.Contains(x.IdArticulo)).Select(x => x.IdArticulo).ToListAsync(ct);
            if (existentes.Count != idsArticulo.Count)
                throw new DomainException("ARTICULO_INEXISTENTE", "Alguno de los artículos elegidos no existe.");
        }
    }
}
