using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pos.Application.Abstractions;
using Pos.Application.Auth;

namespace Pos.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IPermisoRepository _permisos;
    public AuthController(ISender sender, IPermisoRepository permisos)
    {
        _sender = sender;
        _permisos = permisos;
    }

    /// <summary>
    /// Autenticación. Devuelve JWT + contexto de caja resuelto por <c>X-Puesto-Id</c> — un GUID
    /// que el navegador de cada PC de caja genera solo (primera carga) y persiste en su propio
    /// perfil kiosco (ver docs/08-puesto-caja.md), vinculado a un puesto desde el ABM Estructura
    /// de caja &gt; Puestos. Se dejó de resolver por la IP de origen del request: deja de ser
    /// confiable en cuanto hay NAT/VPN/proxy entre la PC y el servidor (sucursales remotas, o
    /// saltos entre VLANs en la propia LAN) — la IP se sigue guardando, pero solo como dato
    /// informativo/auditoría.
    /// Limitado por IP (ver política "login" en Program.cs) para mitigar fuerza bruta, además
    /// del bloqueo de cuenta por intentos fallidos (LoginLockoutReglas).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var idEquipo = ObtenerIdEquipo();
        var ip = ObtenerIpCliente();
        var result = await _sender.Send(new LoginCommand(req.Usuario, req.Clave, idEquipo, ip), ct);
        return result.Ok ? Ok(result) : Unauthorized(result);
    }

    /// <summary>GUID mandado por el frontend en el header X-Puesto-Id (ver client.ts/device.ts).
    /// Es un dato de cliente, no una credencial: solo decide qué caja se abre, no qué puede hacer
    /// el usuario — eso lo sigue controlando el JWT + permisos por rol.</summary>
    private string? ObtenerIdEquipo()
    {
        var valor = Request.Headers["X-Puesto-Id"].ToString();
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    /// <summary>Normaliza IPv4-mapeada-en-IPv6 (::ffff:192.168.x.x, típica en Kestrel sobre
    /// dual-stack) a IPv4 simple. Ya no se usa para resolver la caja (ver ObtenerIdEquipo), solo
    /// para el dato informativo/auditoría que se guarda junto al login.
    /// Nota: si en el futuro se agrega un reverse proxy delante de la API, esto necesita leer
    /// X-Forwarded-For en su lugar — hoy el navegador de cada caja pega directo contra la API.</summary>
    private string? ObtenerIpCliente()
    {
        var ip = HttpContext.Connection.RemoteIpAddress;
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.ToString();
    }

    /// <summary>
    /// Canjea un refresh token vigente por un access token nuevo (rota el refresh: de un solo
    /// uso). Se usa cuando el access token (corto, 15 min) vence, sin pedir usuario/clave de
    /// nuevo. Misma política de rate limiting que login: el refresh token es, en la práctica,
    /// una credencial de larga vida.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var result = await _sender.Send(new RefreshTokenCommand(req.RefreshToken), ct);
        return result.Ok ? Ok(result) : Unauthorized(result);
    }

    /// <summary>
    /// Revoca el refresh token del lado del servidor. Sin esto, borrar el token en el cliente no
    /// alcanza: seguiría siendo válido hasta su vencimiento (hasta 7 días) aunque el usuario ya
    /// haya cerrado sesión.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req, CancellationToken ct)
    {
        await _sender.Send(new LogoutCommand(req.RefreshToken), ct);
        return Ok(new { ok = true, data = true, error = (object?)null });
    }

    /// <summary>
    /// Contexto de la sesión actual (desde los claims del JWT + permisos del rol). Usado por el
    /// frontend para rehidratar la sesión al recargar la página (F5) sin perder idSucursal/idCaja
    /// reales ni caer en valores por defecto incorrectos.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var idRolStr = User.FindFirst("idRol")?.Value;
        var modulos = int.TryParse(idRolStr, out var idRol)
            ? await _permisos.ModulosPorRolAsync(idRol, ct)
            : Array.Empty<string>();

        return Ok(new
        {
            ok = true,
            data = new
            {
                usuario = User.FindFirst("usuario")?.Value,
                rol = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
                idSucursal = User.FindFirst("idSucursal")?.Value,
                idCaja = User.FindFirst("idCaja")?.Value,
                modulos,
                // IP actual del request (no la del login original) — así el usuario ve dónde
                // está parado AHORA, útil si abre la app desde otra PC con la misma sesión.
                ip = ObtenerIpCliente(),
            }
        });
    }
}

public record LoginRequest(string Usuario, string Clave);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);
