using MediatR;
using Pos.Application.Abstractions;
using Pos.Application.Common;

namespace Pos.Application.Auth;

/// <summary>
/// Revoca el refresh token del lado del servidor al desloguearse — sin esto, borrar el token del
/// lado del cliente (localStorage) no invalida nada: el refresh token seguía siendo válido hasta
/// su vencimiento aunque el usuario ya "cerró sesión".
/// </summary>
public record LogoutCommand(string RefreshToken) : IRequest<ApiResult<bool>>, IAuditableRequest
{
    public string Modulo => "Auth";
    public string Accion => "Logout";
}

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, ApiResult<bool>>
{
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly IRefreshTokenGenerator _refreshGen;

    public LogoutCommandHandler(IRefreshTokenRepository refreshRepo, IRefreshTokenGenerator refreshGen)
    {
        _refreshRepo = refreshRepo;
        _refreshGen = refreshGen;
    }

    public async Task<ApiResult<bool>> Handle(LogoutCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ApiResult<bool>.Success(true);

        var hash = _refreshGen.Hash(request.RefreshToken);
        var existente = await _refreshRepo.BuscarPorHashAsync(hash, ct);
        // Idempotente: si no existe o ya estaba revocado, el resultado desde la perspectiva del
        // cliente es el mismo (queda deslogueado) — no hace falta que sea un error.
        if (existente is not null && existente.RevocadoUtc is null)
            await _refreshRepo.RevocarAsync(existente, ct);

        return ApiResult<bool>.Success(true);
    }
}
