using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class SupervisorAuthService : ISupervisorAuthService
{
    // Roles que ya tienen la autoridad por sí mismos: no necesitan que nadie los autorice.
    private static readonly HashSet<string> RolesSinControl = new(StringComparer.OrdinalIgnoreCase)
    {
        "Supervisor", "Administrador",
    };

    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    public SupervisorAuthService(PosDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task ExigirAsync(string? codigoSupervisor, CancellationToken ct = default)
    {
        if (_currentUser.Rol is not null && RolesSinControl.Contains(_currentUser.Rol))
            return;

        if (string.IsNullOrWhiteSpace(codigoSupervisor))
            throw new DomainException("CODIGO_SUPERVISOR_REQUERIDO", "Esta acción requiere autorización de un supervisor.");

        // El código autoriza esta única acción: se valida en el momento contra la base y no queda
        // "recordado" en ningún lado (ni sesión, ni token) para la próxima vez.
        var codigo = codigoSupervisor.Trim();
        var autoriza = await _db.Usuarios.AsNoTracking().AnyAsync(u =>
            u.Activo && u.CodigoSupervisor == codigo &&
            u.Rol != null && (u.Rol.Descripcion == "Supervisor" || u.Rol.Descripcion == "Administrador"), ct);

        if (!autoriza)
            throw new DomainException("CODIGO_SUPERVISOR_INVALIDO", "El código de supervisor no es válido.");
    }
}
