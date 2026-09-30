using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Infrastructure.Adapters.Dbf;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Importa descxtipocli_art.dbf (misma carpeta que pedidos.dbf/operator.dbf de la app legacy VFP
/// "Mayorista" — ver PreventaMayorista:CarpetaDbf en Configuraciones) a los Diferenciales de una
/// lista de precios Tipo=Enlazada. Solo se traen los registros TIPO_TARJE='03' vigentes por
/// DESDE/HASTA (criterio dado por el usuario): esa tarjeta representa el recargo % de la lista
/// Enlazada que se está importando.
/// </summary>
public class DiferencialListaPrecioImportService
{
    private const string ClaveCarpetaDbf = "PreventaMayorista:CarpetaDbf";
    private const string CarpetaDbfPorDefecto = @"S:\appvfp\Mayorista\Mayorista_Release\Datos";
    private const string TipoTarjetaVigente = "03";

    private static readonly IReadOnlySet<string> CamposDbf = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "TIPO_TARJE", "ARTICULO", "LINEA", "RECARGO", "DESDE", "HASTA" };

    private readonly PosDbContext _db;
    private readonly ILogger<DiferencialListaPrecioImportService> _log;

    public DiferencialListaPrecioImportService(PosDbContext db, ILogger<DiferencialListaPrecioImportService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<List<DiferencialListaPrecio>> LeerDbfAsync(int idListaPrecio, CancellationToken ct)
    {
        var carpeta = await ObtenerCarpetaDbfAsync(ct);
        var rutaDbf = Path.Combine(carpeta, "descxtipocli_art.dbf");
        var filas = LeerFilasVigentes(rutaDbf);

        // Códigos del DBF vienen con ceros a la izquierda (igual patrón que PRODUCTO en pedidos.dbf
        // — ver PreventaMayoristaService), hay que normalizarlos antes de buscar el artículo.
        var codigosArticulo = filas.Where(f => f.Articulo.Length > 0)
            .Select(f => f.Articulo.TrimStart('0')).Distinct().ToList();
        var articulos = await _db.Articulos.AsNoTracking()
            .Where(a => codigosArticulo.Contains(a.CodigoInterno))
            .ToDictionaryAsync(a => a.CodigoInterno, a => a.IdArticulo, ct);

        // LINEA es el código de 3 dígitos propio de la app legacy VFP. Confirmado (2026-09-30,
        // comparando contra S:\...\Datos\lineas.dbf) que coincide 1:1 con Linea.CodigoErp — el ERP
        // Central heredó esos códigos al migrarse — así que no hace falta ningún campo/mapeo aparte.
        var codigosLinea = filas.Where(f => f.Linea.Length > 0).Select(f => f.Linea).Distinct().ToList();
        var lineas = await _db.Lineas.AsNoTracking()
            .Where(l => l.CodigoErp != null && codigosLinea.Contains(l.CodigoErp))
            .ToDictionaryAsync(l => l.CodigoErp!, l => l.IdLinea, ct);

        // El DBF legacy tiene muchísimas filas repetidas para el mismo artículo/línea (carga
        // acumulada de años, sin limpieza) — algunas idénticas, otras con % distinto para la misma
        // clave sin ningún otro campo (de los que pide el usuario: tipo_tarjeta/articulo/linea/
        // recargo) que permita diferenciarlas. Se resuelve quedándose con la ÚLTIMA fila del
        // archivo para cada artículo/línea (si hay valores en conflicto, gana el último — se avisa
        // por log para poder revisarlo a mano si hace falta).
        var porArticulo = new Dictionary<int, decimal>();
        var porLinea = new Dictionary<int, decimal>();
        int sinMatch = 0, conflictos = 0;
        foreach (var fila in filas)
        {
            if (fila.Articulo.Length > 0)
            {
                if (!articulos.TryGetValue(fila.Articulo.TrimStart('0'), out var idArticulo)) { sinMatch++; continue; }
                if (porArticulo.TryGetValue(idArticulo, out var previo) && previo != fila.Recargo) conflictos++;
                porArticulo[idArticulo] = fila.Recargo;
            }
            else if (fila.Linea.Length > 0)
            {
                if (!lineas.TryGetValue(fila.Linea, out var idLinea)) { sinMatch++; continue; }
                if (porLinea.TryGetValue(idLinea, out var previo) && previo != fila.Recargo) conflictos++;
                porLinea[idLinea] = fila.Recargo;
            }
        }

        var resultado = new List<DiferencialListaPrecio>();
        foreach (var (idArticulo, porcentaje) in porArticulo)
            resultado.Add(new DiferencialListaPrecio { IdListaPrecio = idListaPrecio, IdArticulo = idArticulo, Porcentaje = porcentaje });
        foreach (var (idLinea, porcentaje) in porLinea)
            resultado.Add(new DiferencialListaPrecio { IdListaPrecio = idListaPrecio, IdLinea = idLinea, Porcentaje = porcentaje });

        if (sinMatch > 0)
            _log.LogWarning("Diferenciales lista {IdLista}: {Cantidad} filas de descxtipocli_art.dbf " +
                "sin artículo/línea propia que matchee, se ignoraron.", idListaPrecio, sinMatch);
        if (conflictos > 0)
            _log.LogWarning("Diferenciales lista {IdLista}: {Cantidad} artículos/líneas con más de un % " +
                "distinto en descxtipocli_art.dbf — se tomó el de la última fila del archivo.", idListaPrecio, conflictos);

        return resultado;
    }

    private sealed record FilaDbf(string Articulo, string Linea, decimal Recargo);

    private List<FilaDbf> LeerFilasVigentes(string rutaDbf)
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            using var reader = new DbfReader(rutaDbf);
            var filas = new List<FilaDbf>();
            foreach (var row in reader.ReadRecords(CamposDbf))
            {
                if (row["TIPO_TARJE"] != TipoTarjetaVigente) continue;

                var desde = ParseFechaYmd(row["DESDE"]);
                var hasta = ParseFechaYmd(row["HASTA"]);
                if (desde is not null && hoy < desde) continue;
                if (hasta is not null && hoy > hasta) continue;

                var articulo = row["ARTICULO"];
                var linea = row["LINEA"];
                if (articulo.Length == 0 && linea.Length == 0) continue; // sin alcance: no aplica a nada

                filas.Add(new FilaDbf(articulo, linea, ParseDecimal(row["RECARGO"])));
            }
            return filas;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "No se pudo leer descxtipocli_art.dbf en {Ruta} (¿red S:\\ no disponible?).", rutaDbf);
            throw new DomainException("DIFERENCIALES_DBF_INACCESIBLE",
                "No se pudo leer el archivo de diferenciales (verificar acceso a S:\\).");
        }
    }

    private async Task<string> ObtenerCarpetaDbfAsync(CancellationToken ct)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == ClaveCarpetaDbf).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(valor) ? CarpetaDbfPorDefecto : valor;
    }

    private static decimal ParseDecimal(string valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    // DESDE/HASTA vienen en formato "YYYYMMDD" (tipo D de dBase); vacío = sin límite de ese lado.
    private static DateOnly? ParseFechaYmd(string valor) =>
        valor.Length == 8 && DateOnly.TryParseExact(valor, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var fecha) ? fecha : null;
}
