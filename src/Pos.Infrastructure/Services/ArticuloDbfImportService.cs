using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Articulos;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Adapters.Dbf;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Compara articulo.dbf (app legacy VFP "Mayorista", misma carpeta que pedidos.dbf — ver
/// PreventaMayorista:CarpetaDbf en Configuraciones) contra Articulos por CodigoInterno e importa SOLO
/// los que todavía no existen (nunca actualiza ni borra un artículo ya cargado), con sus
/// presentaciones y los códigos de barra de cbarras.dbf.
///
/// Equivalencias (verificadas contra SQL el 2026-10-03): LINEA = Linea.CodigoErp, SECTOR =
/// Sector.CodigoErp (coincide en el 99% de los artículos ya cargados), MODIVA = ModoIva.CodigoErp,
/// FAMILIA = Familia.CodigoErp dentro del sector. Sin sector o familia propios cae en "SIN SECTOR" /
/// "SIN FAMILIA" (igual que el sync del ERP); sin línea o IVA equivalentes no se puede importar.
/// En cbarras.dbf TIPO 2 = EAN13 (unidad) y TIPO 1 = DUN14 (bulto) — OJO: el enum TipoBarra de SQL
/// tiene los valores al revés (Ean13=1, Dun14=2), por eso se mapean explícitamente.
/// </summary>
public class ArticuloDbfImportService : IArticuloDbfImportService
{
    private const string ClaveCarpetaDbf = "PreventaMayorista:CarpetaDbf";
    private const string CarpetaDbfPorDefecto = @"S:\appvfp\Mayorista\Mayorista_Release\Datos";
    private const int MaxFilasVistaPrevia = 500;
    private const int MaxLargoCodigoBarra = 20; // Barras.CodigoBarra HasMaxLength(20)
    private const int EstadoInactivo = 3; // igual que el sync ERP: 0/1/2 activo, 3 inactivo

    private static readonly IReadOnlySet<string> CamposArticulo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "CODIGO", "DESCRIP", "LINEA", "SECTOR", "FAMILIA", "MODIVA", "ESTADO", "UNIDADES", "UNIDXBULT", "DESCRIP_TI" };
    private static readonly IReadOnlySet<string> CamposBarras = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "ARTICULO", "CODIGO", "TIPO" };

    private readonly PosDbContext _db;
    private readonly ILogger<ArticuloDbfImportService> _log;

    public ArticuloDbfImportService(PosDbContext db, ILogger<ArticuloDbfImportService> log)
    {
        _db = db;
        _log = log;
    }

    private sealed record FilaArticulo(string Codigo, string Descripcion, string DescripcionTicket, string Linea,
        string Sector, string Familia, string ModoIva, int Estado, decimal UnidadXBulto);

    private sealed record Candidato(FilaArticulo Fila, int? IdSector, int? IdLinea, int? IdFamilia, int? IdModoIva,
        string? Sector, string? Linea, string? Familia, string? ModoIva, string? Motivo);

    public async Task<ComparadorArticulosDto> ObtenerNuevosAsync(CancellationToken ct = default)
    {
        var (candidatos, barrasPorArticulo) = await LeerAsync(ct);
        var items = candidatos
            .OrderBy(c => c.Motivo is null ? 1 : 0).ThenBy(c => c.Fila.Descripcion)
            .Take(MaxFilasVistaPrevia)
            .Select(c => new ArticuloNuevoDto(c.Fila.Codigo, c.Fila.Descripcion, c.Sector, c.Linea, c.Familia,
                c.ModoIva, c.Fila.UnidadXBulto, c.Fila.Estado != EstadoInactivo,
                barrasPorArticulo.TryGetValue(c.Fila.Codigo, out var b) ? b.Count : 0, c.Motivo))
            .ToList();
        return new ComparadorArticulosDto(candidatos.Count, candidatos.Count(c => c.Motivo is null), items);
    }

    public async Task<ImportacionArticulosResultado> ImportarNuevosAsync(CancellationToken ct = default)
    {
        var (candidatos, barrasPorArticulo) = await LeerAsync(ct);
        var importables = candidatos.Where(c => c.Motivo is null).ToList();
        var noImportables = candidatos.Count - importables.Count;
        if (importables.Count == 0) return new ImportacionArticulosResultado(0, 0, 0, noImportables);

        var codigosBarraEnSql = (await _db.Barras.AsNoTracking().Select(b => b.CodigoBarra).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ahora = DateTime.UtcNow;
        int presentaciones = 0, barras = 0, pendientes = 0;
        foreach (var c in importables)
        {
            var f = c.Fila;
            var articulo = new Articulo
            {
                CodigoInterno = f.Codigo, Descripcion = f.Descripcion,
                IdSector = c.IdSector!.Value, IdLinea = c.IdLinea!.Value, IdFamilia = c.IdFamilia!.Value,
                IdModoIva = c.IdModoIva!.Value, Activo = f.Estado != EstadoInactivo, EstadoErp = f.Estado,
                UnidadXBulto = f.UnidadXBulto, UltimaSincronizacionErpUtc = ahora
            };

            // Siempre una presentación unitaria; si el artículo viene en bulto, una segunda por bulto.
            var ticket = f.DescripcionTicket.Length > 0 ? f.DescripcionTicket : f.Descripcion;
            var unidad = new Presentacion { UnidadXBulto = 1m, DescripcionTicket = ticket };
            articulo.Presentaciones.Add(unidad);
            var bulto = f.UnidadXBulto > 1m
                ? new Presentacion { UnidadXBulto = f.UnidadXBulto, DescripcionTicket = $"{ticket} x{f.UnidadXBulto:0.##}" }
                : null;
            if (bulto is not null) articulo.Presentaciones.Add(bulto);
            presentaciones += articulo.Presentaciones.Count;

            if (barrasPorArticulo.TryGetValue(f.Codigo, out var codigos))
                foreach (var (codigo, tipoDbf) in codigos)
                {
                    // TIPO 2 (EAN) cuelga de la unidad; TIPO 1 (DUN) del bulto (o de la unidad si el artículo no viene en bulto).
                    var esEan = tipoDbf == 2;
                    var destino = esEan ? unidad : bulto ?? unidad;
                    if (!codigosBarraEnSql.Add(codigo)) continue; // ya existe en SQL o repetido en el DBF: el índice es único.
                    destino.Barras.Add(new Barra { CodigoBarra = codigo, Tipo = esEan ? TipoBarra.Ean13 : TipoBarra.Dun14 });
                    barras++;
                }

            _db.Articulos.Add(articulo);
            // Por tandas: 16k artículos con sus presentaciones y barras en un único SaveChanges
            // sería una sola transacción enorme. Re-correr es seguro (solo toma los que faltan).
            if (++pendientes % 1000 == 0) await _db.SaveChangesAsync(ct);
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Artículos: importados {Articulos} nuevos desde articulo.dbf ({Presentaciones} presentaciones, " +
            "{Barras} códigos de barra); {NoImportables} sin línea/IVA equivalente quedaron afuera.",
            importables.Count, presentaciones, barras, noImportables);
        return new ImportacionArticulosResultado(importables.Count, presentaciones, barras, noImportables);
    }

    private async Task<(List<Candidato> Candidatos, Dictionary<string, List<(string Codigo, int Tipo)>> Barras)> LeerAsync(
        CancellationToken ct)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == ClaveCarpetaDbf).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        var carpeta = string.IsNullOrWhiteSpace(valor) ? CarpetaDbfPorDefecto : valor;

        var existentes = (await _db.Articulos.AsNoTracking().Select(a => a.CodigoInterno).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Archivos pesados (54k y 70k filas): en un hilo aparte para no bloquear el request.
        var (filas, barrasDbf) = await Task.Run(() =>
            (LeerArticulos(Path.Combine(carpeta, "articulo.dbf"), existentes),
             LeerBarras(Path.Combine(carpeta, "cbarras.dbf"))), ct);

        var lineas = await _db.Lineas.AsNoTracking().Where(l => l.CodigoErp != null)
            .ToDictionaryAsync(l => l.CodigoErp!, l => (l.IdLinea, l.Descripcion), ct);
        var sectores = await _db.Sectores.AsNoTracking().Where(s => s.CodigoErp != null)
            .ToDictionaryAsync(s => s.CodigoErp!, s => (s.IdSector, s.Descripcion), ct);
        var sinSector = await _db.Sectores.AsNoTracking()
            .Where(s => s.CodigoErp == null && s.Descripcion == "SIN SECTOR").Select(s => (int?)s.IdSector).FirstOrDefaultAsync(ct);
        var modosIva = await _db.ModosIva.AsNoTracking().Where(m => m.CodigoErp != null)
            .ToDictionaryAsync(m => m.CodigoErp!, m => (m.IdModoIva, m.Descripcion), ct);
        var familias = (await _db.Familias.AsNoTracking().Where(f => f.CodigoErp != null).ToListAsync(ct))
            .ToDictionary(f => (f.IdSector, f.CodigoErp!), f => (f.IdFamilia, f.Descripcion));
        var sinFamilia = await _db.Familias.AsNoTracking()
            .Where(f => f.CodigoErp == null && f.Descripcion == "SIN FAMILIA").Select(f => (int?)f.IdFamilia).FirstOrDefaultAsync(ct);

        var candidatos = new List<Candidato>();
        foreach (var f in filas)
        {
            int? idLinea = null, idSector = null, idFamilia = null, idModoIva = null;
            string? descLinea = null, descSector = null, descFamilia = null, descIva = null;
            if (lineas.TryGetValue(f.Linea, out var l)) { idLinea = l.IdLinea; descLinea = l.Descripcion; }
            if (sectores.TryGetValue(f.Sector, out var s)) { idSector = s.IdSector; descSector = s.Descripcion; }
            else if (sinSector is not null) { idSector = sinSector; descSector = "SIN SECTOR"; }
            if (modosIva.TryGetValue(f.ModoIva, out var m)) { idModoIva = m.IdModoIva; descIva = m.Descripcion; }
            if (idSector is not null && familias.TryGetValue((idSector, f.Familia), out var fam))
            { idFamilia = fam.IdFamilia; descFamilia = fam.Descripcion; }
            else if (sinFamilia is not null) { idFamilia = sinFamilia; descFamilia = "SIN FAMILIA"; }

            string? motivo = null;
            if (idLinea is null) motivo = $"Línea '{f.Linea}' sin equivalente";
            else if (idModoIva is null) motivo = $"IVA '{f.ModoIva}' sin equivalente";
            else if (idSector is null || idFamilia is null) motivo = "Falta SIN SECTOR / SIN FAMILIA en el sistema";

            candidatos.Add(new Candidato(f, idSector, idLinea, idFamilia, idModoIva,
                descSector, descLinea, descFamilia, descIva, motivo));
        }

        var codigosNuevos = candidatos.Select(c => c.Fila.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var barras = barrasDbf.Where(b => codigosNuevos.Contains(b.Key)).ToDictionary(b => b.Key, b => b.Value);
        return (candidatos, barras);
    }

    private List<FilaArticulo> LeerArticulos(string ruta, HashSet<string> existentes)
    {
        try
        {
            using var reader = new DbfReader(ruta);
            var filas = new List<FilaArticulo>();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in reader.ReadRecords(CamposArticulo))
            {
                var codigo = row["CODIGO"].TrimStart('0');
                if (codigo.Length == 0 || existentes.Contains(codigo) || !vistos.Add(codigo)) continue;
                var descripcion = row["DESCRIP"];
                if (descripcion.Length == 0) continue;

                // UNIDADES es lo que trae el bulto en la mayoría de las filas; UNIDXBULT es el campo
                // alternativo. Ninguno (o 0) = el artículo no viene en bulto.
                var unidades = ParseDecimal(row["UNIDADES"]);
                if (unidades <= 0) unidades = ParseDecimal(row["UNIDXBULT"]);
                filas.Add(new FilaArticulo(codigo, descripcion, row["DESCRIP_TI"], row["LINEA"], row["SECTOR"],
                    row["FAMILIA"], row["MODIVA"], (int)ParseDecimal(row["ESTADO"]), unidades <= 0 ? 1m : unidades));
            }
            return filas;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "No se pudo leer articulo.dbf en {Ruta} (¿red S:\\ no disponible?).", ruta);
            throw new DomainException("ARTICULOS_DBF_INACCESIBLE",
                "No se pudo leer el archivo de artículos (verificar acceso a S:\\).");
        }
    }

    private Dictionary<string, List<(string Codigo, int Tipo)>> LeerBarras(string ruta)
    {
        try
        {
            using var reader = new DbfReader(ruta);
            var resultado = new Dictionary<string, List<(string, int)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in reader.ReadRecords(CamposBarras))
            {
                var articulo = row["ARTICULO"].TrimStart('0');
                var codigo = row["CODIGO"];
                if (articulo.Length == 0 || codigo.Length == 0 || codigo.Length > MaxLargoCodigoBarra) continue;
                var tipo = (int)ParseDecimal(row["TIPO"]);
                if (tipo != 1 && tipo != 2) continue;
                if (!resultado.TryGetValue(articulo, out var lista)) resultado[articulo] = lista = new();
                lista.Add((codigo, tipo));
            }
            return resultado;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "No se pudo leer cbarras.dbf en {Ruta} (¿red S:\\ no disponible?).", ruta);
            throw new DomainException("CBARRAS_DBF_INACCESIBLE",
                "No se pudo leer el archivo de códigos de barra (verificar acceso a S:\\).");
        }
    }

    private static decimal ParseDecimal(string valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
