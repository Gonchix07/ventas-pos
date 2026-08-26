using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Domain.Entities;

namespace Pos.Infrastructure.Persistence;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly PosDbContext _db;
    public UsuarioRepository(PosDbContext db) => _db = db;

    public Task<Usuario?> GetByUsernameAsync(string usuario, CancellationToken ct) =>
        _db.Usuarios.Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.NombreUsuario == usuario, ct);

    public Task<Usuario?> GetByIdAsync(int idUsuario, CancellationToken ct) =>
        _db.Usuarios.Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario, ct);

    public async Task RegistrarIntentoFallidoAsync(int idUsuario, CancellationToken ct)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario, ct);
        if (usuario is null) return;

        usuario.IntentosFallidos = Pos.Domain.Services.LoginLockoutReglas.SiguienteIntento(usuario.IntentosFallidos);
        if (Pos.Domain.Services.LoginLockoutReglas.DebeBloquear(usuario.IntentosFallidos))
            usuario.BloqueadoHasta = Pos.Domain.Services.LoginLockoutReglas.CalcularBloqueoHasta(DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RegistrarLoginExitosoAsync(int idUsuario, CancellationToken ct)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario, ct);
        if (usuario is null) return;

        usuario.IntentosFallidos = 0;
        usuario.BloqueadoHasta = null;
        await _db.SaveChangesAsync(ct);
    }
}

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly PosDbContext _db;
    public RefreshTokenRepository(PosDbContext db) => _db = db;

    public async Task<RefreshToken> CrearAsync(int idUsuario, string tokenHash, DateTime expiraUtc,
        int? idSucursal, int? idCaja, CancellationToken ct)
    {
        var entidad = new RefreshToken
        {
            IdUsuario = idUsuario,
            TokenHash = tokenHash,
            ExpiraUtc = expiraUtc,
            IdSucursal = idSucursal,
            IdCaja = idCaja
        };
        _db.RefreshTokens.Add(entidad);
        await _db.SaveChangesAsync(ct);
        return entidad;
    }

    public Task<RefreshToken?> BuscarPorHashAsync(string tokenHash, CancellationToken ct) =>
        _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

    public async Task RevocarAsync(RefreshToken token, CancellationToken ct)
    {
        // Puede venir "suelto" (no trackeado por este contexto, ej. si el caller lo obtuvo en
        // otro scope) — se re-adjunta por si acaso antes de marcar el cambio.
        if (_db.Entry(token).State == EntityState.Detached)
            _db.RefreshTokens.Attach(token);
        token.RevocadoUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevocarTodosDeUsuarioAsync(int idUsuario, CancellationToken ct)
    {
        var activos = await _db.RefreshTokens
            .Where(r => r.IdUsuario == idUsuario && r.RevocadoUtc == null)
            .ToListAsync(ct);
        var ahora = DateTime.UtcNow;
        foreach (var r in activos) r.RevocadoUtc = ahora;
        await _db.SaveChangesAsync(ct);
    }
}

public class PuestoRepository : IPuestoRepository
{
    private readonly PosDbContext _db;
    public PuestoRepository(PosDbContext db) => _db = db;

    public async Task<ContextoCaja?> ResolverCajaPorEquipoAsync(string identificadorEquipo, CancellationToken ct)
    {
        var puesto = await _db.PuestosCaja
            .FirstOrDefaultAsync(p => p.IdentificadorEquipo == identificadorEquipo, ct);
        if (puesto is null) return null;

        var caja = await _db.Cajas.FirstOrDefaultAsync(
            c => c.IdSucursal == puesto.IdSucursal && c.IdPuestoAsignado == puesto.IdPuestoAsignado, ct);

        return caja is null ? null : new ContextoCaja(caja.IdSucursal, caja.IdCaja);
    }
}

public class PermisoRepository : IPermisoRepository
{
    private readonly PosDbContext _db;
    public PermisoRepository(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> ModulosPorRolAsync(int idRol, CancellationToken ct)
    {
        return await _db.Permisos
            .Where(p => p.IdRol == idRol && p.PuedeVer)
            .Join(_db.Modulos, p => p.IdModulo, m => m.IdModulo, (p, m) => m.Descripcion)
            .Distinct()
            .ToListAsync(ct);
    }
}
