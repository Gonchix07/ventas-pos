using Microsoft.Data.SqlClient;
using Pos.Application.Abstractions.ErpSync;

namespace Pos.Infrastructure.Adapters.Erp;

public class SqlErpClienteReader : IErpClienteReader
{
    private readonly ErpOptions _options;

    public SqlErpClienteReader(ErpOptions options) => _options = options;

    public async Task<IReadOnlyList<ErpClienteRow>> GetClientesModificadosAsync(DateTime watermark, int loteSize, CancellationToken ct)
    {
        // T_Clientes (el maestro legacy con los datos completos) no tiene fecha de modificación
        // propia, así que el "qué cambió" sale de cli.cliente (normalizada, sí la tiene) cruzando por
        // mig.map_cliente para volver al código de 5 caracteres que identifica al cliente en
        // T_Clientes. El GROUP BY colapsa el caso (raro, visto en la exploración) de más de una fila
        // de cli.cliente mapeando al mismo código.
        const string sql = @"
            SELECT TOP (@lote)
                t.idCliente, t.codigo, t.razonSocial, t.nombreFantasia, t.domicilio, t.localidad,
                t.cuit, t.estado, t.email, t.telefono, iva.codigo AS codigoCondIva, x.fecha_actualizacion
            FROM (
                SELECT m.codigo, MAX(c.fecha_actualizacion) AS fecha_actualizacion
                FROM cli.cliente c
                JOIN mig.map_cliente m ON m.id_cliente = c.id
                WHERE c.fecha_actualizacion > @watermark
                GROUP BY m.codigo
            ) x
            JOIN dbo.T_Clientes t ON t.codigo = x.codigo
            JOIN dbo.T_CondIVA iva ON iva.idCondIVA = t.idCondIVA
            ORDER BY x.fecha_actualizacion";

        var resultado = new List<ErpClienteRow>();
        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@watermark", watermark);
        cmd.Parameters.AddWithValue("@lote", loteSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(new ErpClienteRow(
                IdErp: reader.GetInt64(0),
                Codigo: reader.GetString(1),
                RazonSocial: reader.GetString(2),
                NombreFantasia: NuloSiVacio(reader, 3),
                Domicilio: NuloSiVacio(reader, 4),
                Localidad: NuloSiVacio(reader, 5),
                Cuit: NuloSiVacio(reader, 6),
                Estado: reader.GetInt32(7),
                Email: NuloSiVacio(reader, 8),
                Telefono: NuloSiVacio(reader, 9),
                CodigoCondIvaErp: reader.GetString(10),
                FechaActualizacion: reader.GetDateTime(11)));
        }
        return resultado;
    }

    /// <summary>El maestro legacy usa "" en vez de NULL para los campos opcionales sin cargar; se
    /// normaliza acá para no propagar strings vacíos a los campos nullable del dominio.</summary>
    private static string? NuloSiVacio(SqlDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        var v = r.GetString(i);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
