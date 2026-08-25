using Microsoft.EntityFrameworkCore;
using Pos.Application.Abm;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// ABM de "Permisos por rol": qué módulos del menú principal ve cada rol. Solo gobierna
/// <c>Permiso.PuedeVer</c> (lo único que realmente se lee en runtime — ver
/// PermisoRepository.ModulosPorRolAsync, consumido en el login para armar la lista de módulos del
/// JWT). <c>PuedeEditar</c>/<c>EsEspecial</c> se mantienen en sync por prolijidad pero hoy ningún
/// código los consulta: la autorización real de cada endpoint sigue siendo el
/// <c>[Authorize(Roles=...)]</c> fijo de cada controller, esto NO lo reemplaza — es la visibilidad
/// del acceso directo del menú, no un sistema de permisos a nivel de API.
/// </summary>
public class PermisoAdminService : IPermisoAdminService
{
    private readonly PosDbContext _db;
    public PermisoAdminService(PosDbContext db) => _db = db;

    public async Task<MatrizPermisosDto> GetMatrizAsync(CancellationToken ct = default)
    {
        var modulos = await _db.Modulos.AsNoTracking().OrderBy(m => m.IdModulo)
            .Select(m => new ModuloDto(m.IdModulo, m.Descripcion)).ToListAsync(ct);
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.IdRol).ToListAsync(ct);
        var permisos = await _db.Permisos.AsNoTracking()
            .Where(p => p.PuedeVer)
            .Select(p => new { p.IdRol, p.IdModulo }).ToListAsync(ct);
        var habilitados = permisos.Select(p => (p.IdRol, p.IdModulo)).ToHashSet();

        var filas = roles.Select(r => new FilaPermisoRolDto(r.IdRol, r.Descripcion,
            modulos.Select(m => new CeldaPermisoDto(m.IdModulo, habilitados.Contains((r.IdRol, m.IdModulo)))).ToList()
        )).ToList();

        return new MatrizPermisosDto(modulos, filas);
    }

    public async Task ActualizarAsync(ActualizarPermisoInput input, CancellationToken ct = default)
    {
        var rol = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.IdRol == input.IdRol, ct)
            ?? throw new DomainException("ROL_INEXISTENTE", "El rol indicado no existe.");
        var modulo = await _db.Modulos.AsNoTracking().FirstOrDefaultAsync(m => m.IdModulo == input.IdModulo, ct)
            ?? throw new DomainException("MODULO_INEXISTENTE", "El módulo indicado no existe.");

        // Salvaguarda: sin esto, un Administrador podría sacarle a su propio rol el acceso al
        // módulo de Administración y quedar sin forma de revertirlo desde la app (esta misma
        // pantalla vive ahí adentro).
        if (rol.Descripcion == "Administrador" && modulo.Descripcion == "Administracion" && !input.PuedeVer)
            throw new DomainException("NO_PERMITIDO",
                "No se le puede quitar al rol Administrador el acceso al módulo de Administración.");

        var permiso = await _db.Permisos
            .FirstOrDefaultAsync(p => p.IdRol == input.IdRol && p.IdModulo == input.IdModulo, ct);
        if (permiso is null)
        {
            _db.Permisos.Add(new Permiso
            {
                IdRol = input.IdRol, IdModulo = input.IdModulo,
                PuedeVer = input.PuedeVer, PuedeEditar = input.PuedeVer
            });
        }
        else
        {
            permiso.PuedeVer = input.PuedeVer;
            permiso.PuedeEditar = input.PuedeVer;
        }
        await _db.SaveChangesAsync(ct);
    }
}
