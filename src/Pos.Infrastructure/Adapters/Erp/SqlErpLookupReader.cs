using Microsoft.Data.SqlClient;
using Pos.Application.Abstractions.ErpSync;

namespace Pos.Infrastructure.Adapters.Erp;

public class SqlErpLookupReader : IErpLookupReader
{
    private readonly ErpOptions _options;

    public SqlErpLookupReader(ErpOptions options) => _options = options;

    public async Task<IReadOnlyList<ErpSectorRow>> GetSectoresAsync(CancellationToken ct)
    {
        const string sql = "SELECT codigo, descripcion FROM dbo.T_Sectores WHERE activo = 1";
        return await LeerAsync(sql, r => new ErpSectorRow(r.GetString(0), r.GetString(1)), ct);
    }

    public async Task<IReadOnlyList<ErpLineaRow>> GetLineasAsync(CancellationToken ct)
    {
        const string sql = "SELECT codigo, descripcion FROM dbo.T_Lineas WHERE activo = 1";
        return await LeerAsync(sql, r => new ErpLineaRow(r.GetString(0), r.GetString(1)), ct);
    }

    public async Task<IReadOnlyList<ErpFamiliaRow>> GetFamiliasAsync(CancellationToken ct)
    {
        // El sector de la familia se resuelve acá vía el propio código (no el id numérico interno del
        // ERP), para poder matchear contra Sector.CodigoErp del lado de pos-mayorista sin un segundo
        // round-trip.
        const string sql = @"
            SELECT f.codigo, f.descripcion, s.codigo AS codigoSector
            FROM dbo.T_Familias f
            LEFT JOIN dbo.T_Sectores s ON s.idSector = f.idSector
            WHERE f.activo = 1";
        return await LeerAsync(sql, r => new ErpFamiliaRow(
            r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)), ct);
    }

    public async Task<IReadOnlyList<ErpModoIvaRow>> GetModosIvaAsync(CancellationToken ct)
    {
        // valor/percepcion son "float" del lado SQL (double en .NET), no "decimal" — GetDecimal
        // tira InvalidCastException contra una columna float, por eso se pasa por GetDouble.
        const string sql = @"
            SELECT CAST(idModoIVA AS nvarchar(20)), descripcion, valor, ISNULL(percepcion, 0)
            FROM dbo.T_ModoIVA
            WHERE activo = 1";
        return await LeerAsync(sql, r => new ErpModoIvaRow(
            r.GetString(0), r.GetString(1), (decimal)r.GetDouble(2), (decimal)r.GetDouble(3)), ct);
    }

    public async Task<IReadOnlyList<ErpCondicionIvaRow>> GetCondicionesIvaAsync(CancellationToken ct)
    {
        const string sql = "SELECT CAST(codigo AS nvarchar(20)), descripcion FROM dbo.T_CondIVA";
        return await LeerAsync(sql, r => new ErpCondicionIvaRow(r.GetString(0), r.GetString(1)), ct);
    }

    private async Task<IReadOnlyList<T>> LeerAsync<T>(string sql, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        var resultado = new List<T>();
        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) resultado.Add(map(reader));
        return resultado;
    }
}
