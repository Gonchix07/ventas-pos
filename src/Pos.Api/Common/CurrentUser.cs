using System.Security.Claims;
using Pos.Application.Abstractions;

namespace Pos.Api.Common;

/// <summary>Contexto del usuario/petición actual, tomado del JWT y del HttpContext.</summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public int? IdUsuario =>
        int.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User?.FindFirst("sub")?.Value, out var id) ? id : null;

    public string? Usuario => User?.FindFirst("usuario")?.Value;

    public int? IdRol =>
        int.TryParse(User?.FindFirst("idRol")?.Value, out var r) ? r : null;

    public string? Rol => User?.FindFirst(ClaimTypes.Role)?.Value;

    public string? Ip => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? Puesto => _accessor.HttpContext?.Request.Headers["X-Puesto"].FirstOrDefault();

    public int? IdSucursal =>
        int.TryParse(User?.FindFirst("idSucursal")?.Value, out var s) ? s : null;

    public int? IdCaja =>
        int.TryParse(User?.FindFirst("idCaja")?.Value, out var c) ? c : null;
}
