namespace Pos.Infrastructure.Storage;

/// <summary>Rutas en disco del servidor donde se guardan archivos que no van en la base (hoy,
/// solo los certificados CAE de cada empresa). Configurable vía Storage:CertificadosPath.</summary>
public class StorageOptions
{
    public required string CertificadosPath { get; init; }
}
