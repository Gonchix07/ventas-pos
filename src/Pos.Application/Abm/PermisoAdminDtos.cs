namespace Pos.Application.Abm;

// ---- Permisos por rol (acceso a los módulos del menú principal) ----

public record ModuloDto(int IdModulo, string Descripcion);

/// <summary>Si el rol puede ver (y por lo tanto entrar a) este módulo desde el menú principal —
/// ver Pos.Infrastructure.Persistence.Repositories.PermisoRepository.ModulosPorRolAsync, que es lo
/// que realmente arma la lista `Modulos` del login/JWT que consume el frontend.</summary>
public record CeldaPermisoDto(int IdModulo, bool PuedeVer);

public record FilaPermisoRolDto(int IdRol, string RolDescripcion, List<CeldaPermisoDto> Celdas);

public record MatrizPermisosDto(List<ModuloDto> Modulos, List<FilaPermisoRolDto> Roles);

public record ActualizarPermisoInput(int IdRol, int IdModulo, bool PuedeVer);

public interface IPermisoAdminService
{
    Task<MatrizPermisosDto> GetMatrizAsync(CancellationToken ct = default);
    Task ActualizarAsync(ActualizarPermisoInput input, CancellationToken ct = default);
}
