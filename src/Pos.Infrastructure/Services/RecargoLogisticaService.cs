using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pos.Application.Common;
using Pos.Application.PreventaMayorista;
using Pos.Domain.Entities;
using Pos.Infrastructure.Adapters.Dbf;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Importa recargo_logistica.dbf (tabla de tramos de recargo logístico por rango de importe, app
/// legacy VFP "Mayorista", S:\Mayorista\Datos) a la tabla SQL RecargosLogistica, para su uso desde
/// el módulo Preventa Mayorista. A diferencia de pedidos.dbf (que se lee en vivo, cambia varias
/// veces al día) esta es una tabla de tarifas que cambia poco, así que se persiste en SQL y se
/// reimporta solo bajo demanda (ImportarAsync), no en cada consulta.
/// </summary>
public class RecargoLogisticaService : IRecargoLogisticaService
{
    // CODIGO se lee solo para filtrar (a pedido del usuario, únicamente el código 1 — el resto son
    // otras tablas de tarifas del DBF), no se persiste en RecargosLogistica.
    private static readonly IReadOnlySet<string> CamposDbf = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "CODIGO", "INICIO", "FIN", "PORCENTAJE" };

    private readonly PosDbContext _db;
    private readonly ILogger<RecargoLogisticaService> _log;
    private readonly string _rutaDbf;

    public RecargoLogisticaService(PosDbContext db, IConfiguration config, ILogger<RecargoLogisticaService> log)
    {
        _db = db;
        _log = log;
        _rutaDbf = config["PreventaMayorista:RutaDbfRecargoLogistica"]
            ?? @"S:\Mayorista\Datos\recargo_logistica.dbf";
    }

    public async Task<IReadOnlyList<RecargoLogisticaDto>> ObtenerAsync(CancellationToken ct = default) =>
        await _db.RecargosLogistica.AsNoTracking()
            .OrderBy(r => r.Inicio)
            .Select(r => new RecargoLogisticaDto(r.IdRecargoLogistica, r.Inicio, r.Fin, r.Porcentaje))
            .ToListAsync(ct);

    public async Task<int> CreateAsync(RecargoLogisticaInput input, CancellationToken ct = default)
    {
        var tramo = new RecargoLogistica
        {
            Inicio = input.Inicio,
            Fin = input.Fin,
            Porcentaje = input.Porcentaje,
        };
        _db.RecargosLogistica.Add(tramo);
        await _db.SaveChangesAsync(ct);
        return tramo.IdRecargoLogistica;
    }

    public async Task<bool> UpdateAsync(int id, RecargoLogisticaInput input, CancellationToken ct = default)
    {
        var tramo = await _db.RecargosLogistica.FirstOrDefaultAsync(r => r.IdRecargoLogistica == id, ct);
        if (tramo is null) return false;

        tramo.Inicio = input.Inicio;
        tramo.Fin = input.Fin;
        tramo.Porcentaje = input.Porcentaje;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var tramo = await _db.RecargosLogistica.FirstOrDefaultAsync(r => r.IdRecargoLogistica == id, ct);
        if (tramo is null) return false;

        _db.RecargosLogistica.Remove(tramo);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ImportarAsync(CancellationToken ct = default)
    {
        var tramos = LeerDbf();

        // Reemplazo completo: es una tabla de tarifas chica (decenas de filas), más simple y menos
        // propenso a errores que un diff registro por registro contra lo que ya había en SQL.
        var actuales = await _db.RecargosLogistica.ToListAsync(ct);
        _db.RecargosLogistica.RemoveRange(actuales);
        await _db.RecargosLogistica.AddRangeAsync(tramos, ct);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Recargo logística: importados {Cantidad} tramos desde {Ruta}.", tramos.Count, _rutaDbf);
        return tramos.Count;
    }

    private List<RecargoLogistica> LeerDbf()
    {
        try
        {
            using var reader = new DbfReader(_rutaDbf);
            var tramos = new List<RecargoLogistica>();
            // DbfReader.ReadRecords ya descarta los registros marcados como borrados ('*') del DBF.
            foreach (var row in reader.ReadRecords(CamposDbf))
            {
                if (row["CODIGO"] != "1") continue;

                tramos.Add(new RecargoLogistica
                {
                    Inicio = ParseDecimal(row["INICIO"]),
                    Fin = ParseDecimal(row["FIN"]),
                    Porcentaje = ParseDecimal(row["PORCENTAJE"]),
                });
            }
            return tramos;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "No se pudo leer recargo_logistica.dbf en {Ruta} (¿red S:\\ no disponible?).", _rutaDbf);
            throw new DomainException("RECARGO_LOGISTICA_DBF_INACCESIBLE",
                "No se pudo leer el archivo de recargos logísticos (verificar acceso a S:\\).");
        }
    }

    private static decimal ParseDecimal(string valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
