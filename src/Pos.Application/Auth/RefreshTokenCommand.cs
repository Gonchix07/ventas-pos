using FluentValidation;
using MediatR;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Domain.Services;

namespace Pos.Application.Auth;

/// <summary>
/// Canjea un refresh token vigente por un access token nuevo (y rota el refresh: se revoca el
/// presentado y se emite uno nuevo — de un solo uso). Ver Domain.Entities.RefreshToken.
/// </summary>
public record RefreshTokenCommand(string RefreshToken)
    : IRequest<ApiResult<LoginResult>>, IAuditableRequest
{
    public string Modulo => "Auth";
    public string Accion => "Refresh";
}

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, ApiResult<LoginResult>>
{
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly IUsuarioRepository _usuarios;
    private readonly IPermisoRepository _permisos;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenGenerator _refreshGen;
    private readonly RefreshTokenOptions _refreshOpt;

    public RefreshTokenCommandHandler(IRefreshTokenRepository refreshRepo, IUsuarioRepository usuarios,
        IPermisoRepository permisos, IJwtTokenGenerator jwt, IRefreshTokenGenerator refreshGen,
        RefreshTokenOptions refreshOpt)
    {
        _refreshRepo = refreshRepo;
        _usuarios = usuarios;
        _permisos = permisos;
        _jwt = jwt;
        _refreshGen = refreshGen;
        _refreshOpt = refreshOpt;
    }

    public async Task<ApiResult<LoginResult>> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var hash = _refreshGen.Hash(request.RefreshToken);
        var existente = await _refreshRepo.BuscarPorHashAsync(hash, ct);
        if (existente is null)
            return ApiResult<LoginResult>.Fail("REFRESH_INVALIDO", "La sesión no es válida. Inicie sesión de nuevo.");

        var ahora = DateTime.UtcNow;

        if (RefreshTokenReglas.YaFueUsado(existente.RevocadoUtc))
        {
            // Un token ya rotado (de un solo uso) se está presentando de nuevo: indicio de que
            // alguien más ya lo usó (robo). Se revocan TODOS los tokens del usuario — fuerza a
            // reloguearse en todos los dispositivos, no solo el sospechoso.
            await _refreshRepo.RevocarTodosDeUsuarioAsync(existente.IdUsuario, ct);
            return ApiResult<LoginResult>.Fail("REFRESH_INVALIDO", "La sesión no es válida. Inicie sesión de nuevo.");
        }

        if (RefreshTokenReglas.EstaVencido(existente.ExpiraUtc, ahora))
            return ApiResult<LoginResult>.Fail("REFRESH_VENCIDO", "La sesión venció. Inicie sesión de nuevo.");

        var usuario = await _usuarios.GetByIdAsync(existente.IdUsuario, ct);
        if (usuario is null || !usuario.Activo)
            return ApiResult<LoginResult>.Fail("REFRESH_INVALIDO", "La sesión no es válida. Inicie sesión de nuevo.");

        // Rotación: se revoca el token canjeado ANTES de emitir el nuevo (de un solo uso real).
        await _refreshRepo.RevocarAsync(existente, ct);

        var auth = new UsuarioAutenticado(usuario.IdUsuario, usuario.NombreUsuario,
            usuario.IdRol, usuario.Rol?.Descripcion ?? "");
        var modulos = await _permisos.ModulosPorRolAsync(usuario.IdRol, ct);
        var (token, expira) = _jwt.Generar(auth, existente.IdSucursal, existente.IdCaja, modulos);

        var (nuevoRefresh, nuevoHash) = _refreshGen.Generar();
        var nuevaExpiraRefresh = ahora.AddDays(_refreshOpt.Dias);
        await _refreshRepo.CrearAsync(usuario.IdUsuario, nuevoHash, nuevaExpiraRefresh,
            existente.IdSucursal, existente.IdCaja, ct);

        // Ip null a propósito: el refresh no vuelve a resolver caja (reusa existente.IdSucursal/
        // IdCaja del login original, ver comentario de arriba) — el frontend de todos modos
        // muestra la IP actual vía /auth/me, no la de este response.
        return ApiResult<LoginResult>.Success(new LoginResult(
            token, expira, usuario.NombreUsuario, auth.Rol,
            existente.IdSucursal, existente.IdCaja, modulos, nuevoRefresh, nuevaExpiraRefresh, null));
    }
}
