using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Application.Common;

namespace Pos.Infrastructure.Persistence;

/// <summary>
/// Bloqueo de aplicación (sp_getapplock de SQL Server) para serializar entre transacciones
/// concurrentes la generación de IDs correlativos por Max(columna)+1 (patrón usado en varias
/// entidades: LoteCaja, Operacion, CierreLoteCaja, CabeceraComprobante, MovimientoCaja) allí
/// donde no hay una columna IDENTITY ni una tabla contadora dedicada (a diferencia de
/// <c>Numeros</c>, que ya usa UPDATE...OUTPUT). Sin este lock, dos transacciones concurrentes
/// pueden leer el mismo Max() y generar el mismo próximo ID.
///
/// DEBE llamarse dentro de una transacción de EF Core activa: el lock queda atado a esa
/// transacción (@LockOwner = 'Transaction') y se libera solo al hacer commit o rollback — no
/// hace falta (ni se debe) liberarlo a mano.
/// </summary>
public static class RecursoLockHelper
{
    public static async Task AdquirirAsync(DbContext db, string recurso, CancellationToken ct, int timeoutMs = 10000)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        // @resultado se declara como variable T-SQL local (no como parámetro de retorno de ADO.NET:
        // eso solo funciona con CommandType.StoredProcedure, y acá el comando es texto).
        cmd.CommandText =
            "DECLARE @resultado int; " +
            "EXEC @resultado = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', " +
            "@LockOwner = 'Transaction', @LockTimeout = @timeout; " +
            "SELECT @resultado;";
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

        var pRecurso = cmd.CreateParameter();
        pRecurso.ParameterName = "@recurso";
        pRecurso.Value = recurso;
        cmd.Parameters.Add(pRecurso);

        var pTimeout = cmd.CreateParameter();
        pTimeout.ParameterName = "@timeout";
        pTimeout.Value = timeoutMs;
        cmd.Parameters.Add(pTimeout);

        var resultadoObj = await cmd.ExecuteScalarAsync(ct);

        // sp_getapplock: 0/1/2 = adquirido (ok/tras esperar/tras conversión); negativo = falló
        // (timeout, cancelado, deadlock, error). Ver docs de SQL Server para el detalle de códigos.
        var resultado = Convert.ToInt32(resultadoObj);
        if (resultado < 0)
            throw new DomainException("RECURSO_OCUPADO",
                "El recurso está siendo usado por otra operación en este momento. Reintente en unos segundos.");
    }
}
