using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pos.Application.Common;
using Pos.Application.PreventaMayorista;
using Pos.Infrastructure.Adapters.Dbf;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Lee pedidos.dbf de la app legacy VFP "Mayorista_Release" (ver [[preventa-dbf-app]] / análisis del
/// 2026-09-25) y lo cruza contra Cliente/Articulo de pos-mayorista para el módulo "Preventa
/// Mayorista" del menú principal (solo consulta, no escribe nada). Solo importan los pedidos con
/// REPARTO en (PEDIPEND, PEDIAPP): son los dos únicos estados "vigentes" de la tabla — el resto ya
/// está descartado o asignado a un reparto real y no hace falta mostrarlo.
/// </summary>
public class PreventaMayoristaService : IPreventaMayoristaService
{
    private const string CacheKey = "PreventaMayorista:Pedidos";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> RepartosVigentes = new(StringComparer.OrdinalIgnoreCase)
        { "PEDIPEND", "PEDIAPP" };
    private static readonly IReadOnlySet<string> CamposDbf = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "REPARTO", "CLIENTE", "PRODUCTO", "CANTIDAD", "CANT_ORIG", "PRECIO", "DESCUENTO", "PVENTISTA", "PRECARGA",
          "FPEDIDO", "CERRADO", "CONVENIO", "CODCONV", "NUMERO_PED", "AUTOCONV" };

    // Clave de Configuraciones (ABM "Sistema > Configuraciones", editable sin reiniciar el backend
    // — se relee en cada refresco del caché de 5 min, ver LeerYCruzarAsync) y valor por defecto si
    // todavía no está cargada.
    private const string ClaveCarpetaDbf = "PreventaMayorista:CarpetaDbf";
    private const string CarpetaDbfPorDefecto = @"S:\appvfp\Mayorista\Mayorista_Release\Datos";

    private readonly PosDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IRecargoLogisticaService _recargoLogistica;
    private readonly ILogger<PreventaMayoristaService> _log;

    public PreventaMayoristaService(PosDbContext db, IMemoryCache cache, IRecargoLogisticaService recargoLogistica,
        ILogger<PreventaMayoristaService> log)
    {
        _db = db;
        _cache = cache;
        _recargoLogistica = recargoLogistica;
        _log = log;
    }

    public async Task<IReadOnlyList<PreventaClienteDto>> ObtenerPedidosPendientesAsync(
        bool forzarRefresco = false, CancellationToken ct = default)
    {
        if (forzarRefresco) _cache.Remove(CacheKey);

        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<PreventaClienteDto>? cacheado) && cacheado is not null)
            return cacheado;

        var resultado = await LeerYCruzarAsync(ct);
        _cache.Set(CacheKey, resultado, CacheTtl);
        return resultado;
    }

    private async Task<IReadOnlyList<PreventaClienteDto>> LeerYCruzarAsync(CancellationToken ct)
    {
        var carpetaDbf = await ObtenerConfigStringAsync(ClaveCarpetaDbf, CarpetaDbfPorDefecto, ct);
        var lineasCrudas = LeerLineasDelDbf(Path.Combine(carpetaDbf, "pedidos.dbf"));
        if (lineasCrudas.Count == 0) return Array.Empty<PreventaClienteDto>();

        var codigosCliente = lineasCrudas.Select(l => l.CodigoCliente).Distinct().ToList();
        var codigosArticulo = lineasCrudas.Select(l => l.CodigoArticulo).Distinct().ToList();

        var clientes = await _db.Clientes.AsNoTracking()
            .Where(c => codigosCliente.Contains(c.CodigoInt))
            .Select(c => new { c.CodigoInt, c.Descripcion, CondicionIva = c.CondicionIva!.Descripcion, c.PermitePresupuesto })
            .ToDictionaryAsync(c => c.CodigoInt, ct);

        // Articulo.UnidadXBulto (dato propio de la ficha del artículo, no de una presentación
        // puntual — a pedido del usuario) — siempre tiene un valor (default 1m), a diferencia de
        // Presentacion.UnidadXBulto que puede no estar cargada.
        var articulos = await _db.Articulos.AsNoTracking()
            .Where(a => codigosArticulo.Contains(a.CodigoInterno))
            .Select(a => new { a.CodigoInterno, a.Descripcion, a.UnidadXBulto, a.VentaPorPeso })
            .ToDictionaryAsync(a => a.CodigoInterno, ct);

        var tramosRecargo = await _recargoLogistica.ObtenerAsync(ct);

        return lineasCrudas
            .GroupBy(l => l.CodigoCliente)
            .Select(g =>
            {
                // PRECIO del DBF es el precio POR BULTO. La columna "Unitario" que se MUESTRA por
                // línea es ese precio dividido por UnidadXBulto (precio por unidad suelta).
                // CANTIDAD viene codificada igual que la exportación a MySQL (movstock.salida, ver
                // InterfaseContableReglas.CodificarCantidadMovStock): parte entera = bultos, parte
                // decimal = unidades sueltas (2 decimales si UnidadXBulto ≤ 99, 3 si lo supera) —
                // para artículos sin venta por peso. Para el total de la CABECERA hay que decodificar
                // esa cantidad a unidades reales antes de multiplicar por el Unitario.
                var filas = g.Select(l =>
                    {
                        var articulo = articulos.GetValueOrDefault(l.CodigoArticulo);
                        var unidadXBulto = articulo is { UnidadXBulto: > 0 } ? articulo.UnidadXBulto : 1m;
                        var unitario = l.Precio / unidadXBulto;
                        var unidadesReales = DecodificarCantidad(l.Cantidad, unidadXBulto, articulo?.VentaPorPeso ?? false);
                        var importeTotalLinea = unitario * unidadesReales;
                        var dto = new PreventaLineaDto(
                            l.Reparto, l.PVentista, l.CodigoArticulo, articulo?.Descripcion, unidadXBulto,
                            // El importe de la LÍNEA va sin descuento (bruto) — el descuento ya se ve
                            // aparte en su propia columna. El de la CABECERA sí lo aplica (más abajo).
                            l.Cantidad, l.CantOrig, unitario, l.Descuento, l.Precarga,
                            l.FechaPedido, EstadoPrecarga(l.Reparto, l.Cerrado),
                            DescuentoAutorizado(l.Descuento, l.Convenio, l.CodConv, l.Autoconv), Logis(l.NumeroPed));
                        return (Dto: dto, ImporteTotalLinea: importeTotalLinea);
                    })
                    .ToList();
                var lineas = filas.Select(f => f.Dto).ToList();
                var cliente = clientes.GetValueOrDefault(g.Key);
                // Recargo logístico: se calcula sobre la suma de las líneas con Logis="Entrega" (con
                // descuento aplicado, mismo criterio que ImporteFinal), buscando en qué tramo de
                // RecargosLogistica cae ese total.
                var sumaEntrega = filas.Where(f => f.Dto.Logis == "Entrega")
                    .Sum(f => f.ImporteTotalLinea * (1 - f.Dto.Descuento / 100m));
                return new PreventaClienteDto(
                    g.Key,
                    cliente?.Descripcion,
                    cliente?.CondicionIva,
                    cliente?.PermitePresupuesto,
                    lineas.Sum(l => l.Cantidad),
                    g.Sum(l => l.CantOrig),
                    // DESCUENTO es un PORCENTAJE (ej. 11.54 = 11,54%), no un monto — mismo criterio
                    // que InterfaseContableReglas.PorcentajeDescuento del lado opuesto (esa calcula
                    // el % a partir del monto; acá ya viene el % directo del DBF). Se aplica sobre el
                    // importe TOTAL de línea (sin dividir por UnidadXBulto), no sobre el que se
                    // muestra (dividido) — ver comentario arriba.
                    filas.Sum(f => f.ImporteTotalLinea * (1 - f.Dto.Descuento / 100m)),
                    CalcularRecargoLogistica(sumaEntrega, tramosRecargo),
                    g.Select(l => l.Precarga).Distinct().Count(),
                    lineas);
            })
            .OrderBy(c => c.CodigoCliente)
            .ToList();
    }

    private sealed record LineaCruda(string Reparto, string CodigoCliente, string CodigoArticulo,
        decimal Cantidad, decimal CantOrig, decimal Precio, decimal Descuento,
        string PVentista, string Precarga, DateOnly? FechaPedido, int? Cerrado, int? Convenio, string CodConv,
        string NumeroPed, int? Autoconv);

    private List<LineaCruda> LeerLineasDelDbf(string rutaDbf)
    {
        try
        {
            using var reader = new DbfReader(rutaDbf);
            var lineas = new List<LineaCruda>();
            foreach (var row in reader.ReadRecords(CamposDbf))
            {
                var reparto = row["REPARTO"];
                if (!RepartosVigentes.Contains(reparto)) continue;

                var codigoCliente = row["CLIENTE"];
                if (string.IsNullOrEmpty(codigoCliente)) continue;

                // PRODUCTO viene con ceros a la izquierda (char 13) — mismo patrón que
                // Articulo.CodigoInterno normalizado en el sync ERP (ver InterfaseContableReglas.Articulo).
                var codigoArticulo = row["PRODUCTO"].TrimStart('0');
                if (codigoArticulo.Length == 0) continue;

                // Pedido del usuario: solo traer líneas con CANTIDAD > 0 (deja afuera, por ejemplo,
                // las reservadas sin armar todavía, donde CANTIDAD queda en 0/vacío).
                var cantidad = ParseDecimal(row["CANTIDAD"]);
                if (cantidad <= 0) continue;

                lineas.Add(new LineaCruda(
                    reparto, codigoCliente, codigoArticulo,
                    cantidad, ParseDecimal(row["CANT_ORIG"]),
                    ParseDecimal(row["PRECIO"]), ParseDecimal(row["DESCUENTO"]),
                    row["PVENTISTA"], row["PRECARGA"], ParseFechaYmd(row["FPEDIDO"]), ParseInt(row["CERRADO"]),
                    ParseInt(row["CONVENIO"]), row["CODCONV"], row["NUMERO_PED"], ParseInt(row["AUTOCONV"])));
            }
            return lineas;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "No se pudo leer pedidos.dbf en {Ruta} (¿red S:\\ no disponible?).", rutaDbf);
            throw new DomainException("PREVENTA_DBF_INACCESIBLE",
                "No se pudo leer el archivo de pedidos de Preventa Mayorista (verificar acceso a S:\\).");
        }
    }

    /// <summary>
    /// Lee una configuración de texto de la tabla Configuraciones (ABM "Sistema > Configuraciones"),
    /// mismo criterio que FacturacionService/CierreCajaService.ObtenerConfigDecimalAsync pero para
    /// string. Sin caché propio: la llama LeerYCruzarAsync, que ya vive detrás del IMemoryCache de
    /// 5 min del método público — un cambio de configuración se refleja como máximo en ese lapso.
    /// </summary>
    private async Task<string> ObtenerConfigStringAsync(string clave, string valorPorDefecto, CancellationToken ct)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == clave).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(valor) ? valorPorDefecto : valor;
    }

    private static decimal ParseDecimal(string valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static int? ParseInt(string valor) =>
        int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    // FPEDIDO viene en formato "YYYYMMDD" (tipo D de dBase); vacío o corrupto en registros viejos.
    private static DateOnly? ParseFechaYmd(string valor) =>
        valor.Length == 8 && DateOnly.TryParseExact(valor, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var fecha) ? fecha : null;

    /// <summary>
    /// Estado de la precarga (pedido con un carrito armado en la app legacy), según lo pedido por el
    /// usuario: si está en REPARTO=PEDIAPP, siempre "Armando" sin importar CERRADO (todavía se está
    /// cargando en la app). Si no, CERRADO=0 o vacío (null, campo sin cargar) → "Reservado"
    /// (apartado, sin empezar a armar) — confirmado con el usuario que vacío es lo mismo que 0.
    /// CERRADO=1 → "Armado" (ya completo). Cualquier otro valor es desconocido — no se vio en los
    /// datos reales al implementar esto, se muestra tal cual para poder detectarlo.
    /// </summary>
    private static string EstadoPrecarga(string reparto, int? cerrado)
    {
        if (string.Equals(reparto, "PEDIAPP", StringComparison.OrdinalIgnoreCase)) return "Armando";
        return cerrado switch
        {
            null or 0 => "Reservado",
            1 => "Armado",
            _ => $"Desconocido ({cerrado})",
        };
    }

    /// <summary>
    /// Busca el tramo de RecargosLogistica (Inicio/Fin, Fin=-1 = sin límite superior) donde cae
    /// <paramref name="sumaEntrega"/> y devuelve esa suma multiplicada por el Porcentaje del tramo.
    /// 0 si no hay líneas de Entrega o ningún tramo matchea.
    /// </summary>
    private static decimal CalcularRecargoLogistica(decimal sumaEntrega, IReadOnlyList<RecargoLogisticaDto> tramos)
    {
        if (sumaEntrega <= 0) return 0m;
        var tramo = tramos.FirstOrDefault(t =>
            sumaEntrega >= t.Inicio && (t.Fin == -1m || sumaEntrega < t.Fin));
        return tramo is null ? 0m : sumaEntrega * tramo.Porcentaje / 100m;
    }

    /// <summary>
    /// Inverso de <see cref="InterfaseContableReglas.CodificarCantidadMovStock"/>: dado el valor
    /// codificado (bultos + unidades sueltas en la parte decimal), devuelve la cantidad real de
    /// unidades. Confirmado por el usuario: misma regla que ya usa la propia exportación a MySQL.
    /// </summary>
    private static decimal DecodificarCantidad(decimal cantidadCodificada, decimal unidadXBultoArticulo, bool ventaPorPeso)
    {
        if (ventaPorPeso) return cantidadCodificada;

        var bulto = unidadXBultoArticulo <= 0 ? 1m : unidadXBultoArticulo;
        var factor = bulto > 99m ? 1000m : 100m;

        var bultos = Math.Floor(cantidadCodificada);
        var sueltas = Math.Round((cantidadCodificada - bultos) * factor, 0, MidpointRounding.AwayFromZero);
        return bultos * bulto + sueltas;
    }

    /// <summary>
    /// "Autorizado" según lo pedido por el usuario: solo aplica a líneas con descuento (null si
    /// Descuento=0, la columna no tiene sentido ahí). Si CONVENIO=1, CODCONV viene cargado en el DBF
    /// y además AUTOCONV=1 → true (el descuento está respaldado por un convenio Y autorizado); en
    /// cualquier otro caso → false.
    /// </summary>
    private static bool? DescuentoAutorizado(decimal descuento, int? convenio, string codConv, int? autoconv) =>
        descuento <= 0 ? null : convenio == 1 && !string.IsNullOrEmpty(codConv) && autoconv == 1;

    /// <summary>
    /// LOGIS: según lo pedido por el usuario, se deriva del código en NUMERO_PED (generado por la
    /// app de toma de pedidos por PDA/celular) — termina en "E" → Entrega, en "R" → Retiro, vacío →
    /// Fox (pedido cargado directo en la app legacy, no por PDA). El literal "Consolidado" se deja
    /// tal cual (a pedido del usuario). Cualquier otro valor que no termine en E/R (ej. GUIDs sin
    /// ese sufijo — pedidos sugeridos por Mercado Central) → "Sugerido".
    /// </summary>
    private static string Logis(string numeroPed)
    {
        if (string.IsNullOrEmpty(numeroPed)) return "Fox";
        if (numeroPed == "Consolidado") return numeroPed;
        return numeroPed[^1] switch
        {
            'E' => "Entrega",
            'R' => "Retiro",
            _ => "Sugerido",
        };
    }
}
