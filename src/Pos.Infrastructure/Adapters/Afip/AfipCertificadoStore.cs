using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;
using Pos.Infrastructure.Storage;

namespace Pos.Infrastructure.Adapters.Afip;

/// <summary>
/// Lee el certificado .pfx de una empresa (subido desde el ABM de Empresas, ver
/// <c>EstructuraService.SubirCertificadoAsync</c>) para firmar el TRA de WSAA. Mismo archivo y
/// misma contraseña cifrada que ya usa esa pantalla — no hay un certificado separado "para CAE",
/// es el mismo que se cargó una sola vez.
///
/// Vive como singleton (igual que <see cref="AfipWsaaClient"/>, que cachea el Token/Sign entre
/// requests), pero <see cref="PosDbContext"/> es scoped — por eso se resuelve por
/// <see cref="IServiceScopeFactory"/> en cada llamada en vez de inyectarse directo.
/// </summary>
public class AfipCertificadoStore
{
    // Mismo purpose que EstructuraService: si cambia, todo lo cifrado con el purpose viejo deja de
    // poder descifrarse. No tocar sin migrar los certificados ya subidos.
    private const string DataProtectionPurpose = "Pos.CertificadoCae";

    private readonly IServiceScopeFactory _scopes;
    private readonly StorageOptions _storage;
    private readonly IDataProtector _protector;

    public AfipCertificadoStore(IServiceScopeFactory scopes, StorageOptions storage, IDataProtectionProvider dataProtection)
    {
        _scopes = scopes;
        _storage = storage;
        _protector = dataProtection.CreateProtector(DataProtectionPurpose);
    }

    private string RutaCertificado(int idEmpresa) => Path.Combine(_storage.CertificadosPath, $"empresa-{idEmpresa}.pfx");

    /// <summary>Cuit de la empresa, tal como está cargado en el ABM (sin guiones).</summary>
    public async Task<string> ObtenerCuitAsync(int idEmpresa, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        var cuit = await db.Empresas.AsNoTracking().Where(e => e.IdEmpresa == idEmpresa)
            .Select(e => e.Cuit).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(cuit))
            throw new DomainException("EMPRESA_SIN_CUIT", "La empresa no tiene CUIT configurado en el ABM.");
        return cuit.Replace("-", "").Trim();
    }

    /// <summary>
    /// Certificado con clave privada, listo para firmar. No se cachea (se abre en el momento —
    /// solo hace falta al renovar el Token/Sign de WSAA, cada ~12hs por empresa, no en cada
    /// factura): mantener un handle de clave privada vivo por tiempo indefinido en memoria no vale
    /// el ahorro de una lectura de disco ocasional. EphemeralKeySet: no persiste la clave en el
    /// almacén de certificados del usuario del proceso.
    /// </summary>
    public async Task<X509Certificate2> ObtenerCertificadoAsync(int idEmpresa, CancellationToken ct)
    {
        var ruta = RutaCertificado(idEmpresa);
        if (!File.Exists(ruta))
            throw new DomainException("CERTIFICADO_NO_CONFIGURADO",
                $"La empresa {idEmpresa} no tiene un certificado cargado (ABM de Empresas).");

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        var passwordProtegida = await db.Empresas.AsNoTracking().Where(e => e.IdEmpresa == idEmpresa)
            .Select(e => e.CertificadoPasswordProtegida).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(passwordProtegida))
            throw new DomainException("CERTIFICADO_NO_CONFIGURADO",
                $"La empresa {idEmpresa} no tiene la contraseña del certificado guardada.");

        var password = _protector.Unprotect(passwordProtegida);
        var bytes = await File.ReadAllBytesAsync(ruta, ct);
        return new X509Certificate2(bytes, password, X509KeyStorageFlags.EphemeralKeySet);
    }
}
