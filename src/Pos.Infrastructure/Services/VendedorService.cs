using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Common;
using Pos.Application.PreventaMayorista;
using Pos.Domain.Entities;
using Pos.Infrastructure.Adapters.Dbf;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Importa operator.dbf (vendedores de la app legacy VFP "Mayorista", misma carpeta que pedidos.dbf
/// — ver PreventaMayorista:CarpetaDbf en Configuraciones) a la tabla SQL Vendedores, para el módulo
/// Preventa Mayorista. Solo se traen los operadores CARGO=6, PROV="00MAY", ESPECIAL=1, INACTIVO=0
/// (criterio dado por el usuario: son los vendedores "reales" de venta mayorista, el resto de
/// operator.dbf son otros roles — repartidores, administrativos, etc.). Igual que RecargoLogistica,
/// es una tabla chica que cambia poco: se persiste en SQL y se reimporta solo bajo demanda.
/// </summary>
public class VendedorService : IVendedorService
{
    private const string ClaveCarpetaDbf = "PreventaMayorista:CarpetaDbf";
    private const string CarpetaDbfPorDefecto = @"S:\appvfp\Mayorista\Mayorista_Release\Datos";

    private static readonly IReadOnlySet<string> CamposDbf = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "CODIGO", "NOMBRE", "CARGO", "PROV", "ESPECIAL", "INACTIVO" };

    private readonly PosDbContext _db;
    private readonly ILogger<VendedorService> _log;

    public VendedorService(PosDbContext db, ILogger<VendedorService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<IReadOnlyList<VendedorDto>> ObtenerAsync(CancellationToken ct = default) =>
        await _db.Vendedores.AsNoTracking()
            .OrderBy(v => v.Nombre)
            .Select(v => new VendedorDto(v.Codigo, v.Nombre))
            .ToListAsync(ct);

    public async Task<int> ImportarAsync(CancellationToken ct = default)
    {
        var rutaDbf = Path.Combine(await ObtenerCarpetaDbfAsync(ct), "operator.dbf");
        var vendedores = LeerDbf(rutaDbf);

        // Reemplazo completo: tabla chica (decenas de filas), mismo criterio que RecargoLogistica.
        var actuales = await _db.Vendedores.ToListAsync(ct);
        _db.Vendedores.RemoveRange(actuales);
        await _db.Vendedores.AddRangeAsync(vendedores, ct);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Vendedores: importados {Cantidad} desde {Ruta}.", vendedores.Count, rutaDbf);
        return vendedores.Count;
    }

    private async Task<string> ObtenerCarpetaDbfAsync(CancellationToken ct)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == ClaveCarpetaDbf).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(valor) ? CarpetaDbfPorDefecto : valor;
    }

    private List<Vendedor> LeerDbf(string rutaDbf)
    {
        try
        {
            using var reader = new DbfReader(rutaDbf);
            var vendedores = new List<Vendedor>();
            // DbfReader.ReadRecords ya descarta los registros marcados como borrados ('*') del DBF.
            foreach (var row in reader.ReadRecords(CamposDbf))
            {
                if (row["CARGO"] != "6") continue;
                if (row["PROV"] != "00MAY") continue;
                if (row["ESPECIAL"] != "1") continue;
                if (row["INACTIVO"] != "0") continue;

                vendedores.Add(new Vendedor { Codigo = row["CODIGO"], Nombre = row["NOMBRE"] });
            }
            return vendedores;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "No se pudo leer operator.dbf en {Ruta} (¿red S:\\ no disponible?).", rutaDbf);
            throw new DomainException("VENDEDORES_DBF_INACCESIBLE",
                "No se pudo leer el archivo de vendedores de Preventa Mayorista (verificar acceso a S:\\).");
        }
    }
}
