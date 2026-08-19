using Microsoft.EntityFrameworkCore;
using Pos.Application.Catalogo;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// ABM de familias. Reemplaza al CRUD genérico de lookup porque la familia ya no es una tabla plana
/// {Id, Descripcion}: cuelga de un sector, y el nombre solo es único DENTRO del sector (DESODORANTES
/// existe en PERFUMERIA y en LIMPIEZA, y son familias distintas).
/// </summary>
public class FamiliaService : IFamiliaService
{
    private readonly PosDbContext _db;

    public FamiliaService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<FamiliaDto>> GetAllAsync(int? idSector = null, CancellationToken ct = default)
    {
        var q = _db.Familias.AsNoTracking().Include(f => f.Sector).AsQueryable();
        if (idSector is int s) q = q.Where(f => f.IdSector == s);

        // Se ordena por sector y después por familia: con nombres repetidos entre sectores, la lista
        // suelta alfabética no deja distinguir cuál es cuál.
        return await q
            .OrderBy(f => f.Sector != null ? f.Sector.Descripcion : "")
            .ThenBy(f => f.Descripcion)
            .Select(f => new FamiliaDto(f.IdFamilia, f.Descripcion, f.IdSector,
                f.Sector != null ? f.Sector.Descripcion : null))
            .ToListAsync(ct);
    }

    public async Task<FamiliaDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var f = await _db.Familias.AsNoTracking().Include(x => x.Sector)
            .FirstOrDefaultAsync(x => x.IdFamilia == id, ct);
        return f is null ? null : Map(f);
    }

    public async Task<FamiliaDto> CreateAsync(FamiliaInput input, CancellationToken ct = default)
    {
        var descripcion = (input.Descripcion ?? "").Trim();
        if (descripcion.Length == 0)
            throw new DomainException("DESCRIPCION_REQUERIDA", "La descripción es obligatoria.");
        // En el alta el sector es obligatorio: una familia nueva sin sector no aparecería en ningún
        // filtro. La única fila que puede quedar sin sector es la legada "SIN FAMILIA".
        if (input.IdSector is not int idSector)
            throw new DomainException("SECTOR_REQUERIDO", "Hay que elegir el sector de la familia.");

        await ValidarSectorAsync(idSector, ct);
        await ValidarDuplicadoAsync(descripcion, idSector, null, ct);

        var familia = new Familia { Descripcion = descripcion, IdSector = idSector };
        _db.Familias.Add(familia);
        await _db.SaveChangesAsync(ct);
        return new FamiliaDto(familia.IdFamilia, familia.Descripcion, familia.IdSector,
            await DescripcionSectorAsync(idSector, ct));
    }

    public async Task<FamiliaDto?> UpdateAsync(int id, FamiliaInput input, CancellationToken ct = default)
    {
        var familia = await _db.Familias.FirstOrDefaultAsync(f => f.IdFamilia == id, ct);
        if (familia is null) return null;

        var descripcion = (input.Descripcion ?? "").Trim();
        if (descripcion.Length == 0)
            throw new DomainException("DESCRIPCION_REQUERIDA", "La descripción es obligatoria.");
        if (input.IdSector is int idSector) await ValidarSectorAsync(idSector, ct);
        await ValidarDuplicadoAsync(descripcion, input.IdSector, id, ct);

        familia.Descripcion = descripcion;
        familia.IdSector = input.IdSector;
        await _db.SaveChangesAsync(ct);

        return new FamiliaDto(familia.IdFamilia, familia.Descripcion, familia.IdSector,
            familia.IdSector is int s ? await DescripcionSectorAsync(s, ct) : null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var familia = await _db.Familias.FirstOrDefaultAsync(f => f.IdFamilia == id, ct);
        if (familia is null) return false;

        // Borrado físico: se corta antes de que la FK tire un 500 genérico (el DbContext usa
        // DeleteBehavior.Restrict global).
        if (await _db.Articulos.AnyAsync(a => a.IdFamilia == id, ct))
            throw new DomainException("EN_USO", "La familia tiene artículos asignados.");
        if (await _db.AlcancesOfertas.AnyAsync(a => a.IdFamilia == id, ct))
            throw new DomainException("EN_USO", "La familia está usada como alcance de una oferta.");

        _db.Familias.Remove(familia);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task ValidarSectorAsync(int idSector, CancellationToken ct)
    {
        if (!await _db.Sectores.AnyAsync(s => s.IdSector == idSector, ct))
            throw new DomainException("SECTOR_INEXISTENTE", "El sector indicado no existe.");
    }

    private async Task ValidarDuplicadoAsync(string descripcion, int? idSector, int? idActual, CancellationToken ct)
    {
        var duplicada = await _db.Familias.AnyAsync(f =>
            f.IdSector == idSector && f.Descripcion == descripcion &&
            (idActual == null || f.IdFamilia != idActual), ct);
        if (duplicada)
            throw new DomainException("FAMILIA_DUPLICADA", "Ya existe una familia con ese nombre en el sector.");
    }

    private async Task<string?> DescripcionSectorAsync(int idSector, CancellationToken ct) =>
        await _db.Sectores.AsNoTracking().Where(s => s.IdSector == idSector)
            .Select(s => s.Descripcion).FirstOrDefaultAsync(ct);

    private static FamiliaDto Map(Familia f) =>
        new(f.IdFamilia, f.Descripcion, f.IdSector, f.Sector?.Descripcion);
}
