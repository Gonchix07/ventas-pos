using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Adapters;

/// <summary>De dónde sale la configuración de Brevo en cada envío.</summary>
public interface IBrevoConfigProvider
{
    Task<BrevoMailOptions> ObtenerAsync(CancellationToken ct);
}

/// <summary>
/// Lo cargado en Configuraciones (tabla ConexionMail, API key cifrada) manda; si esa tabla está
/// vacía o sin key, se usa lo del servidor (Mail:* de appsettings / user-secrets / variables de
/// entorno). Se lee en cada envío — un cambio en la pantalla rige de inmediato, sin reiniciar la API.
/// </summary>
public class BrevoConfigProvider : IBrevoConfigProvider
{
    public const string DataProtectionPurpose = "Pos.ConexionMail";

    private readonly PosDbContext _db;
    private readonly IDataProtector _protector;
    private readonly BrevoMailOptions _servidor;

    public BrevoConfigProvider(PosDbContext db, IDataProtectionProvider dataProtection, BrevoMailOptions servidor)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(DataProtectionPurpose);
        _servidor = servidor;
    }

    public async Task<BrevoMailOptions> ObtenerAsync(CancellationToken ct)
    {
        var r = new BrevoMailOptions { ApiKey = _servidor.ApiKey, FromEmail = _servidor.FromEmail, FromName = _servidor.FromName };
        var fila = await _db.ConexionesMail.AsNoTracking().SingleOrDefaultAsync(ct);
        if (fila is null) return r;

        if (!string.IsNullOrWhiteSpace(fila.FromEmail)) r.FromEmail = fila.FromEmail;
        if (!string.IsNullOrWhiteSpace(fila.FromName)) r.FromName = fila.FromName;
        if (!string.IsNullOrEmpty(fila.ApiKeyProtegida))
        {
            try { r.ApiKey = _protector.Unprotect(fila.ApiKeyProtegida); }
            catch (CryptographicException)
            {
                // Pasa si se perdió el anillo de claves de Data Protection (otro servidor / perfil de
                // Windows). No se cae al respaldo en silencio: sería mandar con una cuenta distinta
                // a la que el usuario cargó sin que se entere.
                throw new DomainException("MAIL_NO_CONFIGURADO",
                    "No se pudo leer la API key de Brevo guardada. Volvé a cargarla en Administración > Configuraciones.");
            }
        }
        return r;
    }
}
