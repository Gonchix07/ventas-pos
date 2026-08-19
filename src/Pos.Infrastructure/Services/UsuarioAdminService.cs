using Microsoft.EntityFrameworkCore;
using Pos.Application.Abm;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class UsuarioAdminService : IUsuarioAdminService
{
    private readonly PosDbContext _db;
    private readonly IPasswordHasher _hasher;

    public UsuarioAdminService(PosDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<IReadOnlyList<RolDto>> GetRolesAsync(CancellationToken ct = default) =>
        (await _db.Roles.AsNoTracking().OrderBy(r => r.IdRol).ToListAsync(ct))
            .Select(r => new RolDto(r.IdRol, r.Descripcion)).ToList();

    public async Task<IReadOnlyList<UsuarioDto>> GetAllAsync(CancellationToken ct = default)
    {
        var query =
            from u in _db.Usuarios.AsNoTracking()
            join r in _db.Roles.AsNoTracking() on u.IdRol equals r.IdRol into rj
            from r in rj.DefaultIfEmpty()
            orderby u.NombreUsuario
            select new UsuarioDto(u.IdUsuario, u.NombreUsuario, u.IdRol, r != null ? r.Descripcion : null, u.Activo, u.CodigoSupervisor);
        return await query.ToListAsync(ct);
    }

    /// <summary>Null pasa (no todos los usuarios tienen código); si viene cargado, debe ser exactamente
    /// 8 dígitos y no estar ya usado por otro usuario — ver el índice único en PosDbContext.</summary>
    private async Task<string?> ValidarCodigoSupervisorAsync(string? codigo, int? idUsuarioActual, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        var limpio = codigo.Trim();
        if (limpio.Length != 8 || !limpio.All(char.IsDigit))
            throw new DomainException("CODIGO_SUPERVISOR_FORMATO", "El código de supervisor debe tener exactamente 8 dígitos.");
        if (await _db.Usuarios.AnyAsync(u => u.CodigoSupervisor == limpio && u.IdUsuario != (idUsuarioActual ?? 0), ct))
            throw new DomainException("CODIGO_SUPERVISOR_DUPLICADO", "Ese código de supervisor ya está en uso por otro usuario.");
        return limpio;
    }

    public async Task<int> CreateAsync(UsuarioCreateInput input, CancellationToken ct = default)
    {
        var nombre = input.NombreUsuario.Trim();
        if (await _db.Usuarios.AnyAsync(u => u.NombreUsuario == nombre, ct))
            throw new DomainException("USUARIO_DUPLICADO", $"Ya existe el usuario {nombre}.");
        if (string.IsNullOrWhiteSpace(input.Clave) || input.Clave.Length < 6)
            throw new DomainException("CLAVE_DEBIL", "La clave debe tener al menos 6 caracteres.");
        var codigoSupervisor = await ValidarCodigoSupervisorAsync(input.CodigoSupervisor, null, ct);

        var u = new Usuario
        {
            NombreUsuario = nombre,
            ClaveHash = _hasher.Hash(input.Clave),
            IdRol = input.IdRol,
            Activo = input.Activo,
            CodigoSupervisor = codigoSupervisor
        };
        _db.Usuarios.Add(u);
        await _db.SaveChangesAsync(ct);
        return u.IdUsuario;
    }

    public async Task<bool> UpdateAsync(int id, UsuarioUpdateInput input, CancellationToken ct = default)
    {
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == id, ct);
        if (u is null) return false;
        var nombre = input.NombreUsuario.Trim();
        if (await _db.Usuarios.AnyAsync(x => x.NombreUsuario == nombre && x.IdUsuario != id, ct))
            throw new DomainException("USUARIO_DUPLICADO", $"Ya existe el usuario {nombre}.");
        u.NombreUsuario = nombre;
        u.IdRol = input.IdRol;
        u.Activo = input.Activo;
        u.CodigoSupervisor = await ValidarCodigoSupervisorAsync(input.CodigoSupervisor, id, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ResetClaveAsync(int id, ResetClaveInput input, CancellationToken ct = default)
    {
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == id, ct);
        if (u is null) return false;
        if (string.IsNullOrWhiteSpace(input.NuevaClave) || input.NuevaClave.Length < 6)
            throw new DomainException("CLAVE_DEBIL", "La clave debe tener al menos 6 caracteres.");
        u.ClaveHash = _hasher.Hash(input.NuevaClave);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == id, ct);
        if (u is null) return false;
        if (u.NombreUsuario == "admin")
            throw new DomainException("PROTEGIDO", "No se puede eliminar el usuario administrador inicial.");
        _db.Usuarios.Remove(u);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // El borrado es físico y el DbContext usa DeleteBehavior.Restrict en todas las FKs, así
            // que un usuario que ya operó (turnos, refresh tokens, auditoría) no se puede eliminar.
            // Sin esto la FK escapaba como 500 genérico en vez de decir qué hacer.
            throw new DomainException("EN_USO",
                "El usuario ya tiene movimientos registrados (turnos, ventas, sesiones o auditoría). " +
                "Desactivalo en vez de eliminarlo.");
        }
        return true;
    }
}
