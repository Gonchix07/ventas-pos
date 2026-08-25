using Pos.Application.Common;

namespace Pos.Application.Abstractions;

/// <summary>Banco de imágenes de artículos (portal Hergo). Formato &lt;CodigoInterno&gt;_0.JPG.</summary>
public interface IImageBank
{
    Uri BuildImageUrl(string codigoInterno);
    Task<bool> ExistsAsync(string codigoInterno, CancellationToken ct);
}

/// <summary>Envío de reportes (cierre de caja por mail). Adaptador Mock en fase 1.</summary>
public interface IMailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct);
}

/// <summary>Integración futura con ERP para datos maestros (tablas (*) del SRS). Stub en fase 1.</summary>
public interface IErpGateway
{
    bool Enabled { get; }
}

/// <summary>Hash y verificación de contraseñas (BCrypt/PBKDF2).</summary>
public interface IPasswordHasher
{
    string Hash(string plain);
    bool Verify(string plain, string hash);
}

public record UsuarioAutenticado(int IdUsuario, string Usuario, int IdRol, string Rol);

/// <summary>Emisión de tokens JWT.</summary>
public interface IJwtTokenGenerator
{
    /// <paramref name="modulos"/>: los módulos que el rol puede ver (ver IPermisoRepository.ModulosPorRolAsync)
    /// — van como claims "modulo" en el token, para que los controllers puedan autorizar por
    /// módulo además de por rol fijo (ver Pos.Api.Common.ModuloAutorizadoAttribute).
    (string token, DateTime expiraUtc) Generar(UsuarioAutenticado usuario, int? idSucursal, int? idCaja,
        IReadOnlyList<string> modulos);
}

/// <summary>
/// Genera el valor opaco del refresh token (alta entropía, no es un JWT) y su hash para persistir
/// — nunca se guarda el valor plano en BD, solo el hash, igual que una contraseña pero sin
/// necesidad de un hash lento (BCrypt) porque no es un secreto de baja entropía adivinable.
/// </summary>
public interface IRefreshTokenGenerator
{
    (string token, string hash) Generar();
    /// <summary>Hashea un token recibido del cliente, para buscarlo en BD por hash.</summary>
    string Hash(string token);
}

/// <summary>Configuración de expiración de refresh tokens (Jwt:RefreshDias) — Infrastructure la
/// puebla desde IConfiguration; Application solo la consume, sin depender de Infrastructure.</summary>
public class RefreshTokenOptions
{
    public int Dias { get; set; } = 7;
}

/// <summary>Contexto del usuario/petición actual (para auditoría y autorización).</summary>
public interface ICurrentUser
{
    int? IdUsuario { get; }
    string? Usuario { get; }
    int? IdRol { get; }
    /// <summary>Nombre del rol ("Cajero", "Supervisor", ...), tal como viaja en el claim de rol del
    /// JWT. Usado para el control de supervisor: ver <see cref="ISupervisorAuthService"/>.</summary>
    string? Rol { get; }
    string? Ip { get; }
    string? Puesto { get; }

    /// <summary>
    /// Sucursal/caja resueltas al login a partir del nombre de PC (ver LoginCommand/ResolverCajaPorPc).
    /// Nulas cuando la sesión no está atada a un puesto físico (típico de Administrador/Tesorero
    /// operando desde cualquier PC). Cuando tienen valor, la sesión SOLO puede operar sobre esa
    /// sucursal/caja — ver <see cref="CurrentUserAuthorizationExtensions"/>.
    /// </summary>
    int? IdSucursal { get; }
    int? IdCaja { get; }
}

/// <summary>
/// Chequeos de autorización a nivel de recurso (BOLA/IDOR): cuando la sesión está atada a una
/// sucursal/caja específica, cualquier idSucursal/idCaja recibido del cliente (query/body) DEBE
/// coincidir; si la sesión no está atada a ninguna (Administrador/Tesorero desde PC no registrada),
/// no se restringe.
/// </summary>
public static class CurrentUserAuthorizationExtensions
{
    public static void AsegurarSucursal(this ICurrentUser user, int idSucursal)
    {
        if (user.IdSucursal is int propia && propia != idSucursal)
            throw new AccesoDenegadoException("SUCURSAL_NO_AUTORIZADA",
                "No tenés acceso a la sucursal solicitada.");
    }

    public static void AsegurarCaja(this ICurrentUser user, int idCaja)
    {
        if (user.IdCaja is int propia && propia != idCaja)
            throw new AccesoDenegadoException("CAJA_NO_AUTORIZADA",
                "No tenés acceso a la caja solicitada.");
    }

    /// <summary>
    /// Para consultas con filtro de sucursal opcional (ej. dashboard de tesorería): si la sesión
    /// está atada a una sucursal, fuerza el filtro a esa sucursal (no permite "ver todas" ni pedir
    /// otra); si no está atada a ninguna (Tesorero/Administrador típico), respeta lo pedido tal cual.
    /// </summary>
    public static int? AplicarAlcanceSucursal(this ICurrentUser user, int? idSucursalSolicitada)
    {
        if (user.IdSucursal is int propia)
        {
            if (idSucursalSolicitada is int pedida && pedida != propia)
                throw new AccesoDenegadoException("SUCURSAL_NO_AUTORIZADA",
                    "No tenés acceso a la sucursal solicitada.");
            return propia;
        }
        return idSucursalSolicitada;
    }
}

/// <summary>
/// Control de supervisor: ciertas acciones de caja (nota de crédito, anular un artículo del
/// carrito, abrir una caja en un puesto que no es el propio) requieren que se autoricen con un
/// código de 8 dígitos dado de alta en un usuario Supervisor/Administrador. El código se pide de
/// nuevo cada vez — no queda "recordado" entre acciones — y no hace falta si quien está logueado
/// ya es Supervisor o Administrador.
/// </summary>
public interface ISupervisorAuthService
{
    /// <exception cref="DomainException">
    /// CODIGO_SUPERVISOR_REQUERIDO si no se mandó código y hace falta; CODIGO_SUPERVISOR_INVALIDO
    /// si el código no corresponde a ningún Supervisor/Administrador activo.
    /// </exception>
    Task ExigirAsync(string? codigoSupervisor, CancellationToken ct = default);
}

/// <summary>Escritura en la auditoría de negocio independiente (MovimientoAuditoria).</summary>
public interface IAuditLogger
{
    Task LogAsync(string modulo, string accion, string? entidad = null, string? entidadId = null,
                  string? datosAntes = null, string? datosDespues = null, CancellationToken ct = default);
}

/// <summary>Unidad de trabajo / control transaccional sobre la base de datos.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
