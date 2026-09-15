using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Sistema;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Info de versión/build para Admin > Sistema > Versión. El commit se lee EN VIVO corriendo `git`
/// contra el directorio donde corre el proceso (funciona porque git busca el `.git` hacia arriba
/// desde el WorkingDirectory) — no hace falta embeber nada en el build ni tener un pipeline de CI.
/// Si el server no tiene git instalado, o corre desde un deploy que solo copió los binarios (sin
/// `.git`), los campos de git quedan null y el resto de la info se muestra igual.
/// </summary>
public class SistemaVersionService : ISistemaVersionService
{
    public Task<VersionInfoDto> ObtenerAsync(CancellationToken ct = default)
    {
        // GetEntryAssembly (no GetExecutingAssembly): este código vive en Pos.Infrastructure, pero
        // la versión que importa es la del ejecutable real (Pos.Api, <Version> en su .csproj).
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        var efCore = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "?";
        // Mismo nombre que usa el propio host de ASP.NET Core para elegir el entorno
        // (Development/Staging/Production) — leerla de la variable evita depender del paquete de
        // hosting abstractions acá (Pos.Infrastructure es una librería, no un proyecto Web).
        var entorno = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var (hash, hashCorto, fecha, mensaje, rama) = LeerGit();

        return Task.FromResult(new VersionInfoDto(
            version, entorno, RuntimeInformation.FrameworkDescription, efCore,
            Environment.MachineName, hash, hashCorto, fecha, mensaje, rama));
    }

    private static (string? Hash, string? HashCorto, string? Fecha, string? Mensaje, string? Rama) LeerGit()
    {
        // rev-parse HEAD primero: si esto falla (no hay git, o no hay .git), no tiene sentido
        // intentar los demás comandos — se corta acá.
        var hash = EjecutarGit("rev-parse HEAD");
        if (string.IsNullOrWhiteSpace(hash)) return (null, null, null, null, null);

        return (hash,
            EjecutarGit("rev-parse --short HEAD"),
            EjecutarGit("log -1 --format=%cI"),
            EjecutarGit("log -1 --format=%s"),
            EjecutarGit("rev-parse --abbrev-ref HEAD"));
    }

    private static string? EjecutarGit(string argumentos)
    {
        try
        {
            using var proceso = Process.Start(new ProcessStartInfo("git", argumentos)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proceso is null) return null;
            var salida = proceso.StandardOutput.ReadToEnd().Trim();
            var salio = proceso.WaitForExit(3000);
            return salio && proceso.ExitCode == 0 && !string.IsNullOrWhiteSpace(salida) ? salida : null;
        }
        catch
        {
            // git no instalado en el server, o cualquier otro fallo del proceso — no es un error
            // real, simplemente no hay info de commit para mostrar.
            return null;
        }
    }
}
