using FluentValidation;
using MediatR;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Domain.Services;

namespace Pos.Application.Auth;

public record LoginResult(string Token, DateTime ExpiraUtc, string Usuario, string Rol,
                          int? IdSucursal, int? IdCaja, IReadOnlyList<string> Modulos,
                          string RefreshToken, DateTime RefreshExpiraUtc, string? Ip);

/// <summary>
/// IdEquipo: GUID del header X-Puesto-Id (identifica la PC física, ver PuestoCaja.IdentificadorEquipo).
/// Ip: solo se guarda como dato informativo/auditoría, ya no se usa para resolver la caja.
/// </summary>
public record LoginCommand(string Usuario, string Clave, string? IdEquipo, string? Ip)
    : IRequest<ApiResult<LoginResult>>, IAuditableRequest
{
    public string Modulo => "Auth";
    public string Accion => "Login";
}

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Usuario).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Clave).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, ApiResult<LoginResult>>
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IPuestoRepository _puestos;
    private readonly IPermisoRepository _permisos;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenGenerator _refreshGen;
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly RefreshTokenOptions _refreshOpt;

    public LoginCommandHandler(IUsuarioRepository usuarios, IPuestoRepository puestos,
        IPermisoRepository permisos, IPasswordHasher hasher, IJwtTokenGenerator jwt,
        IRefreshTokenGenerator refreshGen, IRefreshTokenRepository refreshRepo, RefreshTokenOptions refreshOpt)
    {
        _usuarios = usuarios;
        _puestos = puestos;
        _permisos = permisos;
        _hasher = hasher;
        _jwt = jwt;
        _refreshGen = refreshGen;
        _refreshRepo = refreshRepo;
        _refreshOpt = refreshOpt;
    }

    public async Task<ApiResult<LoginResult>> Handle(LoginCommand request, CancellationToken ct)
    {
        var usuario = await _usuarios.GetByUsernameAsync(request.Usuario, ct);
        if (usuario is null || !usuario.Activo)
            return ApiResult<LoginResult>.Fail("CREDENCIALES_INVALIDAS", "Usuario o clave incorrectos.");

        var ahora = DateTime.UtcNow;
        if (LoginLockoutReglas.EstaBloqueado(usuario.BloqueadoHasta, ahora))
            return ApiResult<LoginResult>.Fail("USUARIO_BLOQUEADO",
                $"Demasiados intentos fallidos. Intente nuevamente después de las {usuario.BloqueadoHasta:HH:mm} UTC.");

        if (!_hasher.Verify(request.Clave, usuario.ClaveHash))
        {
            await _usuarios.RegistrarIntentoFallidoAsync(usuario.IdUsuario, ct);
            return ApiResult<LoginResult>.Fail("CREDENCIALES_INVALIDAS", "Usuario o clave incorrectos.");
        }
        await _usuarios.RegistrarLoginExitosoAsync(usuario.IdUsuario, ct);

        ContextoCaja? caja = null;
        if (!string.IsNullOrWhiteSpace(request.IdEquipo))
            caja = await _puestos.ResolverCajaPorEquipoAsync(request.IdEquipo!, ct);

        var auth = new UsuarioAutenticado(usuario.IdUsuario, usuario.NombreUsuario,
            usuario.IdRol, usuario.Rol?.Descripcion ?? "");
        var modulos = await _permisos.ModulosPorRolAsync(usuario.IdRol, ct);
        var (token, expira) = _jwt.Generar(auth, caja?.IdSucursal, caja?.IdCaja, modulos);

        var (refreshToken, refreshHash) = _refreshGen.Generar();
        var refreshExpira = ahora.AddDays(_refreshOpt.Dias);
        await _refreshRepo.CrearAsync(usuario.IdUsuario, refreshHash, refreshExpira,
            caja?.IdSucursal, caja?.IdCaja, ct);

        return ApiResult<LoginResult>.Success(new LoginResult(
            token, expira, usuario.NombreUsuario, auth.Rol,
            caja?.IdSucursal, caja?.IdCaja, modulos, refreshToken, refreshExpira, request.Ip));
    }
}
