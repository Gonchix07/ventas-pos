using Pos.Application.Abstractions;
using Pos.Domain.Entities;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Audit;

/// <summary>
/// Escribe en la auditoría de negocio independiente. Persiste por separado para que la
/// traza sobreviva aunque la transacción de negocio se revierta.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _current;

    public AuditLogger(PosDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    public async Task LogAsync(string modulo, string accion, string? entidad = null, string? entidadId = null,
        string? datosAntes = null, string? datosDespues = null, CancellationToken ct = default)
    {
        _db.MovimientosAuditoria.Add(new MovimientoAuditoria
        {
            FechaUtc = DateTime.UtcNow,
            IdUsuario = _current.IdUsuario,
            Usuario = _current.Usuario,
            Modulo = modulo,
            Accion = accion,
            Entidad = entidad,
            EntidadId = entidadId,
            DatosAntes = datosAntes,
            DatosDespues = datosDespues,
            Ip = _current.Ip,
            Puesto = _current.Puesto
        });
        await _db.SaveChangesAsync(ct);
    }
}
