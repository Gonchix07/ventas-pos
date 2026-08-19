using Pos.Domain.Common;

namespace Pos.Domain.Entities;

public class Rol : AuditableEntity
{
    public int IdRol { get; set; }
    public string Descripcion { get; set; } = "";
    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    public ICollection<Permiso> Permisos { get; set; } = new List<Permiso>();
}

public class Usuario : AuditableEntity
{
    public int IdUsuario { get; set; }
    public string NombreUsuario { get; set; } = "";
    /// <summary>Hash BCrypt/PBKDF2 — nunca texto plano.</summary>
    public string ClaveHash { get; set; } = "";
    public bool Activo { get; set; } = true;
    public int IdRol { get; set; }
    public Rol? Rol { get; set; }

    /// <summary>Código numérico de 8 dígitos, único, que un cajero ingresa en el popup de
    /// autorización de supervisor (nota de crédito, anular artículo, abrir caja en otro puesto).
    /// Solo autoriza si además el rol del usuario dueño del código es Supervisor o Administrador —
    /// ver <see cref="Pos.Application.Abstractions.ISupervisorAuthService"/>.</summary>
    public string? CodigoSupervisor { get; set; }

    /// <summary>Intentos de login fallidos consecutivos (se resetea al loguear con éxito).</summary>
    public int IntentosFallidos { get; set; }
    /// <summary>Si tiene valor y es futuro, la cuenta está bloqueada por fuerza bruta hasta ese momento.</summary>
    public DateTime? BloqueadoHasta { get; set; }
}

/// <summary>
/// Refresh token de larga vida para renovar el access token (JWT, corto) sin pedir usuario/clave
/// de nuevo. Se guarda solo el hash (SHA-256) del valor real — el valor en sí únicamente lo tiene
/// el cliente. Rotación de un solo uso: cada refresh revoca este registro y crea uno nuevo; si un
/// token ya revocado se vuelve a presentar, es señal de robo (alguien más lo usó primero) y se
/// revocan todos los de ese usuario.
/// </summary>
public class RefreshToken : AuditableEntity
{
    public int IdRefreshToken { get; set; }
    public int IdUsuario { get; set; }
    public Usuario? Usuario { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiraUtc { get; set; }
    public DateTime? RevocadoUtc { get; set; }
    /// <summary>Contexto de caja resuelto en el login original — se preserva al rotar, para no
    /// tener que volver a resolver por NombrePc en cada refresh.</summary>
    public int? IdSucursal { get; set; }
    public int? IdCaja { get; set; }
}

public class Modulo : AuditableEntity
{
    public int IdModulo { get; set; }
    public string Descripcion { get; set; } = "";
    public ICollection<Permiso> Permisos { get; set; } = new List<Permiso>();
}

public class Permiso : AuditableEntity
{
    public int IdPermiso { get; set; }
    public int IdRol { get; set; }
    public Rol? Rol { get; set; }
    public int IdModulo { get; set; }
    public Modulo? Modulo { get; set; }
    public bool PuedeVer { get; set; } = true;
    public bool PuedeEditar { get; set; }
    public bool EsEspecial { get; set; }
}

/// <summary>
/// Auditoría de negocio: totalmente independiente, SIN claves foráneas (requisito SRS).
/// Sobrevive a cualquier borrado o cambio de esquema.
/// </summary>
public class MovimientoAuditoria
{
    public long IdMovimiento { get; set; }
    public DateTime FechaUtc { get; set; }
    public int? IdUsuario { get; set; }         // valor, sin FK
    public string? Usuario { get; set; }
    public string Modulo { get; set; } = "";
    public string Accion { get; set; } = "";
    public string? Entidad { get; set; }
    public string? EntidadId { get; set; }
    public string? DatosAntes { get; set; }     // JSON
    public string? DatosDespues { get; set; }   // JSON
    public string? Ip { get; set; }
    public string? Puesto { get; set; }
}
