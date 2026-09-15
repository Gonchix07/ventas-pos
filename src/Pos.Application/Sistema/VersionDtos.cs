namespace Pos.Application.Sistema;

/// <summary>
/// Info de versión/build para Admin > Sistema > Versión. Los campos <c>Git*</c> son null cuando el
/// proceso no corre desde un checkout con `.git` (ej. un deploy que solo copió los binarios) o no
/// hay `git` disponible en el server — ver SistemaVersionService.
/// </summary>
public record VersionInfoDto(
    string VersionApp,
    string Entorno,
    string RuntimeDotnet,
    string EfCoreVersion,
    string Servidor,
    string? GitCommit,
    string? GitCommitCorto,
    string? GitFecha,
    string? GitMensaje,
    string? GitRama);

public interface ISistemaVersionService
{
    Task<VersionInfoDto> ObtenerAsync(CancellationToken ct = default);
}
