namespace Pos.Domain.Common;

/// <summary>
/// Base para entidades con auditoría técnica y concurrencia optimista.
/// No confundir con la auditoría de negocio (MovimientoAuditoria), que es independiente.
/// </summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Token de concurrencia optimista (rowversion en SQL Server).</summary>
    public byte[]? RowVersion { get; set; }
}
