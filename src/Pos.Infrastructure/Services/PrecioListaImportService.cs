using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Infrastructure.Adapters.Dbf;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Lee listas.dbf (misma carpeta que pedidos.dbf/descxtipocli_art.dbf de la app legacy VFP — ver
/// PreventaMayorista:CarpetaDbf en Configuraciones) para importar los precios de una lista Base.
/// Solo se toman las filas con NUMERO = 2068 (la lista Azul). PPUBLICO es el precio final de venta
/// por BULTO (UnidadXBulto del artículo) e IMPINT el impuesto interno, también por bulto: ambos
/// vienen sin dividir, el llamador los pasa a unitarios.
/// </summary>
public class PrecioListaImportService
{
    private const string ClaveCarpetaDbf = "PreventaMayorista:CarpetaDbf";
    private const string CarpetaDbfPorDefecto = @"S:\appvfp\Mayorista\Mayorista_Release\Datos";
    public const string NumeroListaAzul = "2068";

    private static readonly IReadOnlySet<string> CamposDbf = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "NUMERO", "ARTICULO", "PPUBLICO", "IMPINT" };

    private readonly PosDbContext _db;

    public PrecioListaImportService(PosDbContext db) => _db = db;

    /// <summary>Precio e impuesto interno de un artículo (código sin ceros a la izquierda). Vienen por
    /// bulto del artículo en listas.dbf (PorBulto=true) y unitarios en prec_prog.dbf.</summary>
    public sealed record FilaPrecioDbf(string CodigoInterno, decimal Precio, decimal ImpuestoInterno, bool PorBulto);

    private static readonly IReadOnlySet<string> CamposPrecProg = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "LISTA", "ARTICULO", "F_DESDE", "F_HASTA", "PRECIO", "IMP_INT" };

    private async Task<string> RutaAsync(string archivo, CancellationToken ct)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == ClaveCarpetaDbf).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return Path.Combine(string.IsNullOrWhiteSpace(valor) ? CarpetaDbfPorDefecto : valor, archivo);
    }

    /// <summary>
    /// prec_prog.dbf (precios programados) para listas Folder: filas de la lista 2068 vigentes hoy
    /// (F_DESDE &lt;= hoy &lt;= F_HASTA; F_HASTA vacío = sin vencimiento). PRECIO/IMP_INT ya son unitarios.
    /// Si un artículo tiene más de una fila vigente gana la de F_DESDE más reciente.
    /// </summary>
    public async Task<IReadOnlyList<FilaPrecioDbf>> LeerPrecProgAsync(CancellationToken ct)
    {
        var ruta = await RutaAsync("prec_prog.dbf", ct);
        // Argentina (UTC-3): "hoy" en hora local, igual que Cambio de Precios en Etiquetas.
        var hoy = DateTime.UtcNow.AddHours(-3).ToString("yyyyMMdd");

        return await Task.Run(() =>
        {
            try
            {
                var porCodigo = new Dictionary<string, (string Desde, FilaPrecioDbf Fila)>();
                using var reader = new DbfReader(ruta);
                foreach (var row in reader.ReadRecords(CamposPrecProg))
                {
                    if (row["LISTA"] != NumeroListaAzul) continue;
                    var desde = row["F_DESDE"];
                    var hasta = row["F_HASTA"];
                    if (desde.Length != 8 || string.CompareOrdinal(desde, hoy) > 0) continue;
                    if (hasta.Length == 8 && string.CompareOrdinal(hasta, hoy) < 0) continue;
                    var codigo = row["ARTICULO"].TrimStart('0');
                    if (codigo.Length == 0) continue;
                    var precio = ParseDecimal(row["PRECIO"]);
                    if (precio <= 0) continue;
                    if (porCodigo.TryGetValue(codigo, out var previa) && string.CompareOrdinal(previa.Desde, desde) > 0) continue;
                    porCodigo[codigo] = (desde, new FilaPrecioDbf(codigo, precio, ParseDecimal(row["IMP_INT"]), PorBulto: false));
                }
                return (IReadOnlyList<FilaPrecioDbf>)porCodigo.Values.Select(v => v.Fila).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DomainException("PRECIOS_DBF_INACCESIBLE",
                    "No se pudo leer prec_prog.dbf (verificar acceso a S:\\).");
            }
        }, ct);
    }

    public async Task<IReadOnlyList<FilaPrecioDbf>> LeerDbfAsync(CancellationToken ct)
    {
        var ruta = await RutaAsync("listas.dbf", ct);

        return await Task.Run(() =>
        {
            try
            {
                // Si el artículo se repite, vale la última fila del archivo.
                var porCodigo = new Dictionary<string, FilaPrecioDbf>();
                using var reader = new DbfReader(ruta);
                foreach (var row in reader.ReadRecords(CamposDbf))
                {
                    if (row["NUMERO"] != NumeroListaAzul) continue;
                    var codigo = row["ARTICULO"].TrimStart('0');
                    if (codigo.Length == 0) continue; // fila de cabecera (artículo 0)
                    var precio = ParseDecimal(row["PPUBLICO"]);
                    if (precio <= 0) continue;
                    porCodigo[codigo] = new FilaPrecioDbf(codigo, precio, ParseDecimal(row["IMPINT"]), PorBulto: true);
                }
                return (IReadOnlyList<FilaPrecioDbf>)porCodigo.Values.ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DomainException("PRECIOS_DBF_INACCESIBLE",
                    "No se pudo leer listas.dbf (verificar acceso a S:\\).");
            }
        }, ct);
    }

    private static decimal ParseDecimal(string valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
