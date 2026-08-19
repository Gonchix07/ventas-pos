using Pos.Domain.Entities;

namespace Pos.Application.Abstractions;

public interface IUsuarioRepository
{
    Task<Usuario?> GetByUsernameAsync(string usuario, CancellationToken ct);

    /// <summary>Recarga el usuario (con su rol) al canjear un refresh token, sin pasar por usuario/clave.</summary>
    Task<Usuario?> GetByIdAsync(int idUsuario, CancellationToken ct);

    /// <summary>Incrementa el contador de intentos fallidos y, si llegó al máximo, bloquea la cuenta.</summary>
    Task RegistrarIntentoFallidoAsync(int idUsuario, CancellationToken ct);

    /// <summary>Resetea el contador de intentos fallidos y el bloqueo (login exitoso).</summary>
    Task RegistrarLoginExitosoAsync(int idUsuario, CancellationToken ct);
}

/// <summary>Persistencia de refresh tokens (ver Domain.Entities.RefreshToken para las reglas de rotación).</summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken> CrearAsync(int idUsuario, string tokenHash, DateTime expiraUtc,
        int? idSucursal, int? idCaja, CancellationToken ct);

    /// <summary>Null si no existe ningún token con ese hash (nunca se busca por valor plano).</summary>
    Task<RefreshToken?> BuscarPorHashAsync(string tokenHash, CancellationToken ct);

    Task RevocarAsync(RefreshToken token, CancellationToken ct);

    /// <summary>Revoca TODOS los tokens activos de un usuario — se usa cuando se detecta reuso de
    /// un token ya rotado (indicio de robo).</summary>
    Task RevocarTodosDeUsuarioAsync(int idUsuario, CancellationToken ct);
}

public record ContextoCaja(int IdSucursal, int IdCaja);

public interface IPuestoRepository
{
    /// <summary>Resuelve la caja física a partir de la IP de origen del login. Null si no está
    /// mapeada. La IP la determina el servidor (no el navegador) — ver AuthController.</summary>
    Task<ContextoCaja?> ResolverCajaPorIpAsync(string ip, CancellationToken ct);
}

public interface IPermisoRepository
{
    /// <summary>Descripciones de módulos habilitados para un rol.</summary>
    Task<IReadOnlyList<string>> ModulosPorRolAsync(int idRol, CancellationToken ct);
}
