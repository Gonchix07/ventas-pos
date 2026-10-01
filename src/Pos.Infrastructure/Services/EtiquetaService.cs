using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Etiquetas;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Búsqueda de artículos para armar la lista de etiquetas y cálculo de los datos de cada
/// etiqueta (precio base + precios por tipo de tarjeta + precio por unidad de medida + sin
/// impuestos + compra mínima). Ver docs de Fase 6 y plantillas reales de referencia.
/// </summary>
public class EtiquetaService : IEtiquetaService
{
    private readonly PosDbContext _db;
    private readonly ICurrentUser _currentUser;
    public EtiquetaService(PosDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ArticuloParaEtiquetaDto>> BuscarAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return Array.Empty<ArticuloParaEtiquetaDto>();

        // Solo presentaciones individuales (UnidadXBulto = 1, no bultos/cajas): la etiqueta de
        // góndola es para la unidad suelta, no para el bulto — pedido explícito del usuario.
        var porBarra = await (
            from b in _db.Barras.AsNoTracking().Where(x => x.CodigoBarra == query)
            join pr in _db.Presentaciones.AsNoTracking().Where(p => p.UnidadXBulto == 1m) on b.IdPresentacion equals pr.IdPresentacion
            join a in _db.Articulos.AsNoTracking().Where(x => x.Activo) on pr.IdArticulo equals a.IdArticulo
            select new ArticuloParaEtiquetaDto(a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket)
        ).ToListAsync(ct);
        if (porBarra.Count > 0) return porBarra;

        return await (
            from a in _db.Articulos.AsNoTracking().Where(x => x.Activo && (x.CodigoInterno.Contains(query) || x.Descripcion.Contains(query)))
            join pr in _db.Presentaciones.AsNoTracking().Where(p => p.UnidadXBulto == 1m) on a.IdArticulo equals pr.IdArticulo
            orderby a.Descripcion
            select new ArticuloParaEtiquetaDto(a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket)
        ).Take(30).ToListAsync(ct);
    }

    public async Task<ArticuloParaEtiquetaDto?> BuscarExactoAsync(string codigo, CancellationToken ct = default)
    {
        codigo = codigo.Trim();
        if (codigo.Length == 0) return null;

        // Código de barras exacto primero, mismo criterio que BuscarAsync.
        var porBarra = await (
            from b in _db.Barras.AsNoTracking().Where(x => x.CodigoBarra == codigo)
            join pr in _db.Presentaciones.AsNoTracking().Where(p => p.UnidadXBulto == 1m) on b.IdPresentacion equals pr.IdPresentacion
            join a in _db.Articulos.AsNoTracking().Where(x => x.Activo) on pr.IdArticulo equals a.IdArticulo
            select new ArticuloParaEtiquetaDto(a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket)
        ).FirstOrDefaultAsync(ct);
        if (porBarra is not null) return porBarra;

        // Código interno EXACTO — a diferencia de BuscarAsync, acá NO se admite Contains sobre
        // descripción ni código: es solo para el escaneo numérico, donde un match parcial sería un
        // error (podría traer un artículo distinto al escaneado).
        return await (
            from a in _db.Articulos.AsNoTracking().Where(x => x.Activo && x.CodigoInterno.Trim() == codigo)
            join pr in _db.Presentaciones.AsNoTracking().Where(p => p.UnidadXBulto == 1m) on a.IdArticulo equals pr.IdArticulo
            select new ArticuloParaEtiquetaDto(a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket)
        ).FirstOrDefaultAsync(ct);
    }

    private const string ClaveCarpetaDbf = "PreventaMayorista:CarpetaDbf";
    private const string CarpetaDbfPorDefecto = @"S:\appvfp\Mayorista\Mayorista_Release\Datos";
    private const string ListaPrecioAzul = "2068";
    private static readonly IReadOnlySet<string> CamposListasProg = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "FECHA", "LISTA", "ARTICULO", "HECHO", "PFINAL" };
    private static readonly IReadOnlySet<string> CamposPrecProg = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "LISTA", "ARTICULO", "F_DESDE", "PRECIO", "IMP_INT" };

    public async Task<CambioPreciosDto> CambioDePreciosAsync(CancellationToken ct = default)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == ClaveCarpetaDbf).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        var carpeta = string.IsNullOrWhiteSpace(valor) ? CarpetaDbfPorDefecto : valor;
        // Argentina (UTC-3): "mañana" se toma en hora local, no UTC (a la noche UTC ya es otro día).
        var manana = DateTime.UtcNow.AddHours(-3).Date.AddDays(1).ToString("yyyyMMdd");

        // LISTAS_PROG pesa ~400 MB y 1,5 millones de registros: se lee en un hilo aparte para no bloquear el request.
        var (azules, unicos) = await Task.Run(() =>
        {
            var azules = new Dictionary<string, decimal>();
            var unicos = new Dictionary<string, (decimal Precio, decimal ImpuestoInterno)>();
            try
            {
                using (var reader = new Pos.Infrastructure.Adapters.Dbf.DbfReader(Path.Combine(carpeta, "LISTAS_PROG.DBF")))
                    foreach (var row in reader.ReadRecords(CamposListasProg))
                    {
                        if (row["LISTA"] != ListaPrecioAzul || row["HECHO"].Length != 0 || row["FECHA"] != manana) continue;
                        var codigo = row["ARTICULO"].TrimStart('0');
                        if (codigo.Length == 0) continue;
                        // Si el DBF repite el artículo para la misma fecha, vale la última fila.
                        azules[codigo] = ParseDecimalDbf(row["PFINAL"]);
                    }

                // Precios únicos programados: un solo precio para Azul y Rojo, vigente desde F_DESDE.
                using (var reader = new Pos.Infrastructure.Adapters.Dbf.DbfReader(Path.Combine(carpeta, "PREC_PROG.DBF")))
                    foreach (var row in reader.ReadRecords(CamposPrecProg))
                    {
                        if (row["LISTA"] != ListaPrecioAzul || row["F_DESDE"] != manana) continue;
                        var codigo = row["ARTICULO"].TrimStart('0');
                        if (codigo.Length == 0) continue;
                        unicos[codigo] = (ParseDecimalDbf(row["PRECIO"]), ParseDecimalDbf(row["IMP_INT"]));
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new Pos.Application.Common.DomainException("LISTAS_PROG_DBF_INACCESIBLE",
                    "No se pudo leer LISTAS_PROG.DBF / PREC_PROG.DBF (verificar acceso a S:\\).");
            }
            return (azules, unicos);
        }, ct);

        // Si un artículo está en las dos tablas gana el precio único: es un precio específico para esa fecha.
        var codigos = azules.Keys.Union(unicos.Keys).ToList();
        var articulos = await (
            from a in _db.Articulos.AsNoTracking().Where(x => x.Activo && codigos.Contains(x.CodigoInterno))
            join pr in _db.Presentaciones.AsNoTracking().Where(p => p.UnidadXBulto == 1m) on a.IdArticulo equals pr.IdArticulo
            orderby a.Descripcion
            select new { a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket }
        ).ToListAsync(ct);

        // Un artículo puede tener más de una presentación unitaria: una sola etiqueta por artículo.
        var items = articulos.GroupBy(x => x.IdArticulo).Select(g => g.First())
            .Select(x => unicos.TryGetValue(x.CodigoInterno, out var u)
                ? new ArticuloCambioPrecioDto(x.IdArticulo, x.IdPresentacion, x.CodigoInterno, x.Descripcion,
                    x.DescripcionTicket, u.Precio, EsPrecioUnico: true, ImpuestoInterno: u.ImpuestoInterno)
                : new ArticuloCambioPrecioDto(x.IdArticulo, x.IdPresentacion, x.CodigoInterno, x.Descripcion,
                    x.DescripcionTicket, azules[x.CodigoInterno]))
            .ToList();
        var encontradosCodigos = items.Select(i => i.CodigoInterno).ToHashSet();
        var sinMatch = codigos.Where(c => !encontradosCodigos.Contains(c)).OrderBy(c => c).ToList();
        return new CambioPreciosDto(items, codigos.Count, sinMatch);
    }

    private static decimal ParseDecimalDbf(string valor) =>
        decimal.TryParse(valor, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;

    public async Task<IReadOnlyList<ArticuloParaEtiquetaDto>> PorClasificacionAsync(
        int? idSector, int? idLinea, int? idFamilia, CancellationToken ct = default)
    {
        var q = _db.Articulos.AsNoTracking().Where(a => a.Activo);
        if (idSector.HasValue) q = q.Where(a => a.IdSector == idSector.Value);
        if (idLinea.HasValue) q = q.Where(a => a.IdLinea == idLinea.Value);
        if (idFamilia.HasValue) q = q.Where(a => a.IdFamilia == idFamilia.Value);

        return await (
            from a in q
            join pr in _db.Presentaciones.AsNoTracking() on a.IdArticulo equals pr.IdArticulo
            orderby a.Descripcion
            select new ArticuloParaEtiquetaDto(a.IdArticulo, pr.IdPresentacion, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket)
        ).Take(500).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EtiquetaDto>> GenerarAsync(int idSucursal, List<int> idsPresentacion,
        IReadOnlyDictionary<int, decimal>? preciosAzulNuevos = null,
        IReadOnlyDictionary<int, PrecioUnicoNuevoDto>? preciosUnicosNuevos = null, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        var resultado = new List<EtiquetaDto>();
        // UtcNow, no Now: ver PricingService — DateTime.Now depende de la zona horaria del servidor.
        var fecha = DateTime.UtcNow;

        var tiposTarjeta = await _db.TiposTarjeta.AsNoTracking().Where(t => t.IdListaPrecio != null).ToListAsync(ct);
        // Cambio de Precios: la tarjeta Azul es la lista base de la Roja (Enlazada). Si no se puede
        // identificar, no se simula nada y se sigue con los precios vigentes.
        var idListaAzul = tiposTarjeta.FirstOrDefault(t => t.Descripcion.Contains("AZUL", StringComparison.OrdinalIgnoreCase))?.IdListaPrecio;
        var ofertasVigentes = await _db.CabecerasOfertas.AsNoTracking()
            .Where(o => o.IdSucursal == idSucursal && o.FechaInicio <= fecha && o.FechaFin >= fecha)
            .Include(o => o.Alcances).Include(o => o.Acciones)
            .ToListAsync(ct);

        foreach (var idPresentacion in idsPresentacion.Distinct())
        {
            var info = await (
                from pr in _db.Presentaciones.AsNoTracking().Where(p => p.IdPresentacion == idPresentacion)
                join a in _db.Articulos.AsNoTracking() on pr.IdArticulo equals a.IdArticulo
                join m in _db.ModosIva.AsNoTracking() on a.IdModoIva equals m.IdModoIva
                select new
                {
                    pr.IdPresentacion, a.IdArticulo, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket,
                    a.IdSector, a.IdLinea, a.IdFamilia, a.UnidadMedida, a.ContenidoNetoUnitario, m.Alicuota
                }
            ).FirstOrDefaultAsync(ct);
            if (info is null) continue;

            var codigoBarra = await _db.Barras.AsNoTracking().Where(b => b.IdPresentacion == idPresentacion)
                .Select(b => b.CodigoBarra).FirstOrDefaultAsync(ct);

            // Cambio de Precios — precio único programado (PREC_PROG.DBF): una sola línea para Azul y
            // Rojo, igual que un folder. No depende de las listas, así que sale aunque hoy no tenga precio.
            if (preciosUnicosNuevos is not null && preciosUnicosNuevos.TryGetValue(idPresentacion, out var unico))
            {
                var pxuUnico = EtiquetaCalculos.PrecioPorUnidadMedida(unico.Precio, info.ContenidoNetoUnitario);
                var siUnico = EtiquetaCalculos.PrecioSinImpuestosNacionales(unico.Precio, unico.ImpuestoInterno, info.Alicuota);
                resultado.Add(new EtiquetaDto(idPresentacion, info.CodigoInterno, info.Descripcion, info.DescripcionTicket,
                    codigoBarra, unico.Precio, pxuUnico, siUnico, new List<TipoTarjetaPrecioDto>(),
                    ResolverCompraMinima(ofertasVigentes, info.IdArticulo, info.IdSector, info.IdLinea, info.IdFamilia),
                    TextoUnidadMedida(info.UnidadMedida), "Precio Único"));
                continue;
            }

            var candidatos = await (
                from p in _db.Precios.AsNoTracking().Where(x => x.IdPresentacion == idPresentacion)
                join l in _db.ListasPrecios.AsNoTracking().Where(x => x.IdSucursal == idSucursal) on p.IdListaPrecio equals l.IdListaPrecio
                // El IdListaPrecio va explícito: dentro de un árbol de expresión no se pueden omitir
                // los argumentos opcionales del record.
                select new CandidatoPrecio(l.Tipo, l.Prioridad, l.FechaInicio, l.FechaFin, p.PrecioFinal,
                    p.ImpuestoInterno, l.IdListaPrecio)
            ).ToListAsync(ct);
            candidatos.AddRange(await DiferencialListaPrecioResolver.ResolverCandidatosAsync(_db, idSucursal, idPresentacion, ct));
            var resuelto = CalculadoraPrecios.Resolver(candidatos, fecha);
            var azulNuevo = idListaAzul is not null && preciosAzulNuevos is not null &&
                preciosAzulNuevos.TryGetValue(idPresentacion, out var pn) ? pn : (decimal?)null;
            // Sin precio vigente no se genera etiqueta... salvo en Cambio de Precios: ahí el artículo puede
            // no tener todavía ningún precio cargado y el Azul nuevo del DBF es justamente su primer precio.
            if (!resuelto.Encontrado && azulNuevo is null) continue;
            var precioVigente = resuelto.Encontrado ? resuelto.PrecioVigente : azulNuevo!.Value;
            var impuestoVigente = resuelto.Encontrado ? resuelto.ImpuestoInterno : 0m;

            var precioPorUnidad = EtiquetaCalculos.PrecioPorUnidadMedida(precioVigente, info.ContenidoNetoUnitario);
            var sinImpuestos = EtiquetaCalculos.PrecioSinImpuestosNacionales(precioVigente, impuestoVigente, info.Alicuota);

            var preciosTarjeta = new List<TipoTarjetaPrecioDto>();
            foreach (var t in tiposTarjeta)
            {
                var precioLista = azulNuevo is decimal nuevo
                    ? await ResolverPrecioSimuladoAsync(t.IdListaPrecio!.Value, idListaAzul!.Value, nuevo,
                        info.IdArticulo, info.IdLinea, idPresentacion, ct)
                    : await DiferencialListaPrecioResolver.ResolverPrecioUnicoAsync(
                        _db, t.IdListaPrecio!.Value, idPresentacion, ct);
                if (precioLista is null) continue;
                var pxu = EtiquetaCalculos.PrecioPorUnidadMedida(precioLista.Value.PrecioFinal, info.ContenidoNetoUnitario);
                var si = EtiquetaCalculos.PrecioSinImpuestosNacionales(precioLista.Value.PrecioFinal, precioLista.Value.ImpuestoInterno, info.Alicuota);
                preciosTarjeta.Add(new TipoTarjetaPrecioDto(t.Descripcion.ToUpperInvariant(), precioLista.Value.PrecioFinal, pxu, si));
            }

            var compraMinima = ResolverCompraMinima(ofertasVigentes, info.IdArticulo, info.IdSector, info.IdLinea, info.IdFamilia);

            // Precio Único: se busca primero el folder (gana por prioridad — ver CalculadoraPrecios —
            // así que si hay uno vigente ES el precio ya resuelto) y, si no hay, se ve si las tarjetas
            // configuradas (Rojo/Azul) terminaron coincidiendo en el mismo precio. En cualquiera de los
            // dos casos se muestra una sola línea con la aclaración; si no, la etiqueta sigue mostrando
            // un precio por tarjeta como hasta ahora.
            var precioFinal = precioVigente;
            var pxuFinal = precioPorUnidad;
            var siFinal = sinImpuestos;
            string? aclaracion = null;
            var esFolder = candidatos.Any(c => c.Tipo == TipoListaPrecio.Folder);

            if (esFolder)
            {
                aclaracion = "Precio Único";
                preciosTarjeta = new List<TipoTarjetaPrecioDto>();
            }
            else if (preciosTarjeta.Count >= 2 && preciosTarjeta.Select(t => t.Precio).Distinct().Count() == 1)
            {
                aclaracion = "Precio Único";
                var compartido = preciosTarjeta[0];
                precioFinal = compartido.Precio;
                pxuFinal = compartido.PrecioPorUnidadMedida;
                siFinal = compartido.PrecioSinImpuestos;
                preciosTarjeta = new List<TipoTarjetaPrecioDto>();
            }

            resultado.Add(new EtiquetaDto(idPresentacion, info.CodigoInterno, info.Descripcion, info.DescripcionTicket,
                codigoBarra, precioFinal, pxuFinal, siFinal, preciosTarjeta, compraMinima,
                TextoUnidadMedida(info.UnidadMedida), aclaracion));
        }

        return resultado;
    }

    /// <summary>
    /// Precio de una tarjeta SIMULANDO que el Azul ya vale <paramref name="azulNuevo"/>: la lista Azul
    /// devuelve ese precio; una lista Enlazada cuya base es el Azul aplica su diferencial (artículo
    /// puntual &gt; línea, como en producción) sobre el Azul nuevo; cualquier otra lista no cambia.
    /// El impuesto interno se toma del precio vigente del Azul (el DBF solo trae PFINAL).
    /// </summary>
    private async Task<(decimal PrecioFinal, decimal ImpuestoInterno)?> ResolverPrecioSimuladoAsync(
        int idListaTarjeta, int idListaAzul, decimal azulNuevo, int idArticulo, int idLinea, int idPresentacion,
        CancellationToken ct)
    {
        var lista = await _db.ListasPrecios.AsNoTracking().Where(l => l.IdListaPrecio == idListaTarjeta)
            .Select(l => new { l.Tipo, l.IdListaBase }).FirstOrDefaultAsync(ct);
        if (lista is null) return null;

        var esAzul = idListaTarjeta == idListaAzul;
        var esEnlazadaDelAzul = lista.Tipo == TipoListaPrecio.Enlazada && lista.IdListaBase == idListaAzul;
        if (!esAzul && !esEnlazadaDelAzul)
            return await DiferencialListaPrecioResolver.ResolverPrecioUnicoAsync(_db, idListaTarjeta, idPresentacion, ct);

        var impuestoInterno = await _db.Precios.AsNoTracking()
            .Where(p => p.IdListaPrecio == idListaAzul && p.IdPresentacion == idPresentacion)
            .Select(p => (decimal?)p.ImpuestoInterno).FirstOrDefaultAsync(ct) ?? 0m;
        if (esAzul) return (azulNuevo, impuestoInterno);

        var porcentaje = await DiferencialListaPrecioResolver.ObtenerPorcentajeAsync(_db, idListaTarjeta, idArticulo, idLinea, ct);
        return (Math.Round(azulNuevo * (1 + porcentaje / 100m), 4, MidpointRounding.AwayFromZero), impuestoInterno);
    }

    /// <summary>
    /// Cantidad mínima de compra para el precio mostrado, según ofertas de bonificación
    /// vigentes con alcance sobre el artículo (mismo criterio de alcance que el motor de
    /// ofertas de Fase 2, sin la dimensión de cluster porque en etiquetas no hay cliente).
    /// </summary>
    private static decimal ResolverCompraMinima(
        List<Pos.Domain.Entities.CabeceraOferta> ofertas, int idArticulo, int idSector, int idLinea, int idFamilia)
    {
        foreach (var cab in ofertas)
        {
            var accionBonif = cab.Acciones.FirstOrDefault(a => a.CantidadMin is > 0);
            if (accionBonif is null) continue;

            bool Coincide(Pos.Domain.Entities.AlcanceOferta al) =>
                (al.IdArticulo is null || al.IdArticulo == idArticulo) &&
                (al.IdSector is null || al.IdSector == idSector) &&
                (al.IdLinea is null || al.IdLinea == idLinea) &&
                (al.IdFamilia is null || al.IdFamilia == idFamilia);

            var inclusiones = cab.Alcances.Where(a => !a.EsExcepcion).ToList();
            var exclusiones = cab.Alcances.Where(a => a.EsExcepcion).ToList();
            var incluida = inclusiones.Count == 0 || inclusiones.Any(Coincide);
            var excluida = exclusiones.Any(Coincide);
            if (incluida && !excluida) return accionBonif.CantidadMin!.Value;
        }
        return 1m;
    }

    public async Task<ClasificacionesDto> GetClasificacionesAsync(CancellationToken ct = default)
    {
        var sectores = await _db.Sectores.AsNoTracking().OrderBy(s => s.Descripcion)
            .Select(s => new LookupSimpleDto(s.IdSector, s.Descripcion)).ToListAsync(ct);
        var lineas = await _db.Lineas.AsNoTracking().OrderBy(l => l.Descripcion)
            .Select(l => new LookupSimpleDto(l.IdLinea, l.Descripcion)).ToListAsync(ct);
        var familias = await _db.Familias.AsNoTracking().OrderBy(f => f.Descripcion)
            .Select(f => new FamiliaLookupDto(f.IdFamilia, f.Descripcion, f.IdSector)).ToListAsync(ct);
        return new ClasificacionesDto(sectores, lineas, familias);
    }

    public async Task<IReadOnlyList<LookupSimpleDto>> GetSucursalesAsync(CancellationToken ct = default) =>
        await _db.Sucursales.AsNoTracking().OrderBy(s => s.Descripcion)
            .Select(s => new LookupSimpleDto(s.IdSucursal, s.Descripcion)).ToListAsync(ct);

    private static string TextoUnidadMedida(UnidadMedida u) => u switch
    {
        UnidadMedida.Kilogramo => "Kg",
        UnidadMedida.Litro => "Lt",
        _ => ""
    };
}
