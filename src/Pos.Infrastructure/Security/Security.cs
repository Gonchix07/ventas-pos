using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Pos.Application.Abstractions;

namespace Pos.Infrastructure.Security;

public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string plain) => BCrypt.Net.BCrypt.HashPassword(plain, workFactor: 12);
    public bool Verify(string plain, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(plain, hash); }
        catch { return false; }
    }
}

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "Pos";
    public string Audience { get; set; } = "PosClients";
    public int Minutos { get; set; } = 60;
}

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _opt;
    public JwtTokenGenerator(JwtOptions opt) => _opt = opt;

    public (string token, DateTime expiraUtc) Generar(UsuarioAutenticado usuario, int? idSucursal, int? idCaja)
    {
        var expira = DateTime.UtcNow.AddMinutes(_opt.Minutos);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.IdUsuario.ToString()),
            new("usuario", usuario.Usuario),
            new("idRol", usuario.IdRol.ToString()),
            new(ClaimTypes.Role, usuario.Rol),
        };
        if (idSucursal is not null) claims.Add(new("idSucursal", idSucursal.ToString()!));
        if (idCaja is not null) claims.Add(new("idCaja", idCaja.ToString()!));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer, audience: _opt.Audience,
            claims: claims, expires: expira, signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}

/// <summary>
/// Token opaco de alta entropía (256 bits aleatorios, no un JWT) + SHA-256 para el hash que se
/// persiste. No usa BCrypt (a diferencia de las contraseñas): no hace falta un hash lento para un
/// valor que ya es imposible de adivinar por fuerza bruta.
/// </summary>
public class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public (string token, string hash) Generar()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, Hash(token));
    }

    public string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
