using Microsoft.Data.SqlClient;
using Pos.Application.Abstractions.ErpSync;

namespace Pos.Infrastructure.Adapters.Erp;

public class SqlErpArticuloReader : IErpArticuloReader
{
    private readonly ErpOptions _options;

    public SqlErpArticuloReader(ErpOptions options) => _options = options;

    public async Task<IReadOnlyList<ErpArticuloRow>> GetArticulosModificadosAsync(DateTime watermark, int loteSize, CancellationToken ct)
    {
        // Se resuelven los códigos de Sector/Línea/Familia/ModoIVA acá (no los ids numéricos internos
        // del ERP) para poder matchear directo contra los CodigoErp ya sincronizados del lado de
        // pos-mayorista. idFamilia es nullable en el ERP ("SIN FAMILIA" no cuelga de ninguna), por
        // eso ese LEFT JOIN puede devolver NULL sin problema.
        const string sql = @"
            SELECT TOP (@lote)
                a.idArticulo, a.codigo, a.descripcionFull, a.estado, a.fecha_modificacion,
                s.codigo AS codigoSector, l.codigo AS codigoLinea, f.codigo AS codigoFamilia,
                CAST(a.idModoIVA AS nvarchar(20)) AS codigoModoIva, a.unidadBulto
            FROM dbo.T_Articulos a
            JOIN dbo.T_Sectores s ON s.idSector = a.idSector
            JOIN dbo.T_Lineas l ON l.idLinea = a.idLinea
            LEFT JOIN dbo.T_Familias f ON f.idFamilia = a.idFamilia
            WHERE a.fecha_modificacion > @watermark
            ORDER BY a.fecha_modificacion";
        return await LeerAsync(sql, watermark, loteSize, r => new ErpArticuloRow(
            IdErp: r.GetInt64(0),
            Codigo: r.GetString(1),
            DescripcionFull: r.GetString(2),
            Estado: r.GetInt32(3),
            FechaModificacion: r.GetDateTime(4),
            CodigoSectorErp: r.GetString(5),
            CodigoLineaErp: r.GetString(6),
            CodigoFamiliaErp: r.IsDBNull(7) ? null : r.GetString(7),
            CodigoModoIvaErp: r.GetString(8),
            UnidadBulto: (decimal)r.GetDouble(9)), ct);
    }

    public async Task<IReadOnlyList<ErpPresentacionRow>> GetPresentacionesModificadasAsync(DateTime watermark, int loteSize, CancellationToken ct)
    {
        const string sql = @"
            SELECT TOP (@lote) idPresentacion, idArticulo, codigo, fraccion, baja, fecha_modificacion
            FROM dbo.T_Presentaciones
            WHERE fecha_modificacion > @watermark AND idArticulo IS NOT NULL
            ORDER BY fecha_modificacion";
        return await LeerAsync(sql, watermark, loteSize, r => new ErpPresentacionRow(
            IdErp: r.GetInt64(0),
            IdArticuloErp: r.GetInt64(1),
            Codigo: r.IsDBNull(2) ? null : r.GetString(2),
            Fraccion: r.IsDBNull(3) ? 1m : r.GetInt32(3),
            Baja: !r.IsDBNull(4) && r.GetBoolean(4),
            FechaModificacion: r.GetDateTime(5)), ct);
    }

    public async Task<IReadOnlyList<ErpCodBarraRow>> GetCodBarrasModificadosAsync(DateTime watermark, int loteSize, CancellationToken ct)
    {
        // T_CodBarras solo trae id_presentacion (uniqueidentifier), no el idPresentacion legacy — se
        // resuelve con un join a T_Presentaciones por su columna id (el mismo uniqueidentifier).
        const string sql = @"
            SELECT TOP (@lote) p.idPresentacion, b.codigo, b.tipo_codigo, b.fecha_modificacion
            FROM dbo.T_CodBarras b
            JOIN dbo.T_Presentaciones p ON p.id = b.id_presentacion
            WHERE b.fecha_modificacion > @watermark AND (b.baja = 0 OR b.baja IS NULL)
            ORDER BY b.fecha_modificacion";
        return await LeerAsync(sql, watermark, loteSize, r => new ErpCodBarraRow(
            IdPresentacionErp: r.GetInt64(0),
            Codigo: r.GetString(1),
            TipoCodigo: r.IsDBNull(2) ? "EAN" : r.GetString(2),
            FechaModificacion: r.GetDateTime(3)), ct);
    }

    private async Task<IReadOnlyList<T>> LeerAsync<T>(string sql, DateTime watermark, int loteSize, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        var resultado = new List<T>();
        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@watermark", watermark);
        cmd.Parameters.AddWithValue("@lote", loteSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) resultado.Add(map(reader));
        return resultado;
    }
}
