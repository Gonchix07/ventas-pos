using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Application.Abm;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class ConvenioService : IConvenioService
{
    private readonly PosDbContext _db;
    public ConvenioService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<ConvenioDto>> GetAsync(int idSucursal, CancellationToken ct = default)
    {
        var query =
            from cv in _db.Convenios.AsNoTracking().Where(x => x.IdSucursal == idSucursal)
            join c in _db.Clientes.AsNoTracking() on cv.IdCliente equals c.IdCliente into cj
            from c in cj.DefaultIfEmpty()
            join l in _db.ListasPrecios.AsNoTracking() on cv.IdListaPrecio equals l.IdListaPrecio into lj
            from l in lj.DefaultIfEmpty()
            orderby cv.IdConvenio
            select new ConvenioDto(cv.IdSucursal, cv.IdConvenio, cv.IdCliente,
                c != null ? c.Descripcion : null, cv.Descuento, cv.IdListaPrecio,
                l != null ? l.CodigoInterno : null);
        return await query.ToListAsync(ct);
    }

    public async Task<int> CreateAsync(int idSucursal, ConvenioInput input, CancellationToken ct = default)
    {
        var next = (await _db.Convenios.Where(x => x.IdSucursal == idSucursal)
            .Select(x => x.IdConvenio).MaxAsync(x => (int?)x, ct) ?? 0) + 1;
        _db.Convenios.Add(new Convenio
        {
            IdSucursal = idSucursal, IdConvenio = next, IdCliente = input.IdCliente,
            Descuento = input.Descuento, IdListaPrecio = input.IdListaPrecio
        });
        await _db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<bool> UpdateAsync(int idSucursal, int idConvenio, ConvenioInput input, CancellationToken ct = default)
    {
        var cv = await _db.Convenios.FirstOrDefaultAsync(x => x.IdSucursal == idSucursal && x.IdConvenio == idConvenio, ct);
        if (cv is null) return false;
        cv.IdCliente = input.IdCliente;
        cv.Descuento = input.Descuento;
        cv.IdListaPrecio = input.IdListaPrecio;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int idSucursal, int idConvenio, CancellationToken ct = default)
    {
        var cv = await _db.Convenios.FirstOrDefaultAsync(x => x.IdSucursal == idSucursal && x.IdConvenio == idConvenio, ct);
        if (cv is null) return false;
        _db.Convenios.Remove(cv);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public class ClusterService : IClusterService
{
    private readonly PosDbContext _db;
    public ClusterService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<ClusterDto>> GetClustersAsync(CancellationToken ct = default)
    {
        // Se listan desde Clusters (no desde las pertenencias): un cluster sin miembros también existe.
        return await _db.Clusters.AsNoTracking()
            .OrderBy(c => c.Descripcion)
            .Select(c => new ClusterDto(c.IdCluster, c.Descripcion, c.Miembros.Count))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ClusterMiembroDto>> GetMiembrosAsync(int idCluster, CancellationToken ct = default)
    {
        var query =
            from cc in _db.ClusterClientes.AsNoTracking().Where(x => x.IdCluster == idCluster)
            join c in _db.Clientes.AsNoTracking() on cc.IdCliente equals c.IdCliente
            orderby c.Descripcion
            select new ClusterMiembroDto(c.IdCliente, c.Descripcion, c.CodigoInt);
        return await query.ToListAsync(ct);
    }

    public async Task<int> CreateClusterAsync(ClusterInput input, CancellationToken ct = default)
    {
        var desc = (input.Descripcion ?? "").Trim();
        if (desc.Length == 0)
            throw new DomainException("DESCRIPCION_REQUERIDA", "El cluster necesita un nombre.");
        if (await _db.Clusters.AnyAsync(c => c.Descripcion == desc, ct))
            throw new DomainException("CLUSTER_DUPLICADO", $"Ya existe un cluster llamado «{desc}».");

        var cluster = new Cluster { Descripcion = desc };
        _db.Clusters.Add(cluster);
        await _db.SaveChangesAsync(ct);
        return cluster.IdCluster;
    }

    public async Task<bool> RenameClusterAsync(int idCluster, ClusterInput input, CancellationToken ct = default)
    {
        var desc = (input.Descripcion ?? "").Trim();
        if (desc.Length == 0)
            throw new DomainException("DESCRIPCION_REQUERIDA", "El cluster necesita un nombre.");

        var cluster = await _db.Clusters.FirstOrDefaultAsync(c => c.IdCluster == idCluster, ct);
        if (cluster is null) return false;
        if (await _db.Clusters.AnyAsync(c => c.Descripcion == desc && c.IdCluster != idCluster, ct))
            throw new DomainException("CLUSTER_DUPLICADO", $"Ya existe otro cluster llamado «{desc}».");

        cluster.Descripcion = desc;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> AddMiembroAsync(int idCluster, ClusterMiembroInput input, CancellationToken ct = default)
    {
        if (!await _db.Clusters.AnyAsync(c => c.IdCluster == idCluster, ct)) return false;
        if (await _db.ClusterClientes.AnyAsync(x => x.IdCluster == idCluster && x.IdCliente == input.IdCliente, ct))
            throw new DomainException("YA_MIEMBRO", "El cliente ya pertenece al cluster.");

        _db.ClusterClientes.Add(new ClusterCliente { IdCluster = idCluster, IdCliente = input.IdCliente });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveMiembroAsync(int idCluster, int idCliente, CancellationToken ct = default)
    {
        var row = await _db.ClusterClientes.FirstOrDefaultAsync(x => x.IdCluster == idCluster && x.IdCliente == idCliente, ct);
        if (row is null) return false;
        _db.ClusterClientes.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ClusterMiembrosResultado?> SetMiembrosAsync(int idCluster, ClusterMiembrosSetInput input, CancellationToken ct = default)
    {
        if (!await _db.Clusters.AnyAsync(c => c.IdCluster == idCluster, ct)) return null;

        var pedidos = (input.IdsClientes ?? new List<int>()).Distinct().ToList();

        // Se validan los clientes ANTES de tocar nada: si viene un id inexistente conviene fallar
        // entero y no dejar el set a medias.
        if (pedidos.Count > 0)
        {
            var existentes = await _db.Clientes.AsNoTracking()
                .Where(c => pedidos.Contains(c.IdCliente)).Select(c => c.IdCliente).ToListAsync(ct);
            var faltantes = pedidos.Except(existentes).ToList();
            if (faltantes.Count > 0)
                throw new DomainException("CLIENTE_INEXISTENTE",
                    $"No existen los clientes: {string.Join(", ", faltantes)}.");
        }

        var actuales = await _db.ClusterClientes.Where(x => x.IdCluster == idCluster).ToListAsync(ct);
        var idsActuales = actuales.Select(x => x.IdCliente).ToHashSet();

        var aQuitar = actuales.Where(x => !pedidos.Contains(x.IdCliente)).ToList();
        var aAgregar = pedidos.Where(id => !idsActuales.Contains(id)).ToList();

        _db.ClusterClientes.RemoveRange(aQuitar);
        foreach (var id in aAgregar)
            _db.ClusterClientes.Add(new ClusterCliente { IdCluster = idCluster, IdCliente = id });

        await _db.SaveChangesAsync(ct);
        return new ClusterMiembrosResultado(aAgregar.Count, aQuitar.Count, pedidos.Count);
    }

    public async Task<bool> DeleteClusterAsync(int idCluster, CancellationToken ct = default)
    {
        var cluster = await _db.Clusters.FirstOrDefaultAsync(c => c.IdCluster == idCluster, ct);
        if (cluster is null) return false;

        // Integridad: si alguna oferta usa este cluster como alcance, borrarlo dejaría el alcance
        // apuntando a la nada (y el MotorOfertas resolvería mal). Mismo patrón EN_USO del resto del ABM.
        if (await _db.AlcancesOfertas.AnyAsync(a => a.IdCluster == idCluster, ct))
            throw new DomainException("EN_USO", "No se puede eliminar: hay ofertas que usan este cluster como alcance.");

        // La convención del DbContext es "sin cascadas", así que las pertenencias se borran a mano
        // (mismo SaveChanges, así que es atómico).
        _db.ClusterClientes.RemoveRange(_db.ClusterClientes.Where(x => x.IdCluster == idCluster));
        _db.Clusters.Remove(cluster);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public class TarjetaAdminService : ITarjetaAdminService
{
    private readonly PosDbContext _db;
    public TarjetaAdminService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<TipoTarjetaDto>> GetTiposAsync(CancellationToken ct = default)
    {
        var query =
            from t in _db.TiposTarjeta.AsNoTracking()
            join l in _db.ListasPrecios.AsNoTracking() on t.IdListaPrecio equals l.IdListaPrecio into lj
            from l in lj.DefaultIfEmpty()
            orderby t.Descripcion
            select new TipoTarjetaDto(t.IdTipoTarjeta, t.Descripcion, t.IdListaPrecio, l != null ? l.CodigoInterno : null);
        return await query.ToListAsync(ct);
    }

    public async Task<int> CreateTipoAsync(TipoTarjetaInput input, CancellationToken ct = default)
    {
        var t = new TipoTarjeta { Descripcion = input.Descripcion.Trim(), IdListaPrecio = input.IdListaPrecio };
        _db.TiposTarjeta.Add(t);
        await _db.SaveChangesAsync(ct);
        return t.IdTipoTarjeta;
    }

    public async Task<bool> UpdateTipoAsync(int id, TipoTarjetaInput input, CancellationToken ct = default)
    {
        var t = await _db.TiposTarjeta.FirstOrDefaultAsync(x => x.IdTipoTarjeta == id, ct);
        if (t is null) return false;
        t.Descripcion = input.Descripcion.Trim();
        t.IdListaPrecio = input.IdListaPrecio;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteTipoAsync(int id, CancellationToken ct = default)
    {
        if (await _db.TarjetasClientes.AnyAsync(x => x.IdTipoTarjeta == id, ct))
            throw new DomainException("EN_USO", "El tipo de tarjeta está asignado a clientes.");
        var t = await _db.TiposTarjeta.FirstOrDefaultAsync(x => x.IdTipoTarjeta == id, ct);
        if (t is null) return false;
        _db.TiposTarjeta.Remove(t);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<TarjetaClienteDto>> GetTarjetasAsync(int idCliente, CancellationToken ct = default)
    {
        var query =
            from tc in _db.TarjetasClientes.AsNoTracking().Where(x => x.IdCliente == idCliente)
            join t in _db.TiposTarjeta.AsNoTracking() on tc.IdTipoTarjeta equals t.IdTipoTarjeta into tj
            from t in tj.DefaultIfEmpty()
            // La vigente primero, después las anuladas de la más reciente a la más vieja.
            orderby tc.Activa descending, tc.FechaBajaUtc descending
            select new TarjetaClienteDto(tc.IdCliente, tc.IdTipoTarjeta, t != null ? t.Descripcion : null,
                tc.NroTarjeta, tc.Activa, tc.FechaBajaUtc);
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// El cliente tiene UNA tarjeta vigente: dar de alta una nueva anula la que tenía activa (no se
    /// borra, para no perder el rastro de las ventas hechas con ese número). Todo en un solo
    /// SaveChanges, así nunca queda el cliente con dos activas ni con ninguna.
    /// </summary>
    public async Task<AltaTarjetaResultado> AddTarjetaAsync(int idCliente, TarjetaClienteInput input, CancellationToken ct = default)
    {
        if (!await _db.Clientes.AnyAsync(c => c.IdCliente == idCliente, ct))
            return new AltaTarjetaResultado(false, 0, null, null);

        var nro = (input.NroTarjeta ?? "").Trim();
        if (nro.Length == 0)
            throw new DomainException("NRO_REQUERIDO", "Hay que indicar el número de tarjeta.");
        if (!await _db.TiposTarjeta.AnyAsync(t => t.IdTipoTarjeta == input.IdTipoTarjeta, ct))
            throw new DomainException("TIPO_INEXISTENTE", "El tipo de tarjeta no existe.");

        // El número identifica al cliente en Caja: no puede estar vigente en otro.
        var deOtro = await _db.TarjetasClientes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NroTarjeta == nro && x.Activa && x.IdCliente != idCliente, ct);
        if (deOtro is not null)
            throw new DomainException("TARJETA_DE_OTRO_CLIENTE",
                $"El número {nro} ya está vigente en otro cliente (código interno {deOtro.IdCliente}).");

        var delCliente = await _db.TarjetasClientes.Where(x => x.IdCliente == idCliente).ToListAsync(ct);
        var activa = delCliente.FirstOrDefault(x => x.Activa);
        var nueva = delCliente.FirstOrDefault(x => x.IdTipoTarjeta == input.IdTipoTarjeta && x.NroTarjeta == nro);

        if (nueva is not null && nueva.Activa)
            throw new DomainException("DUPLICADA", "Esa ya es la tarjeta vigente del cliente.");

        string? nroAnulada = null, tipoAnulada = null;
        var anuladas = 0;
        // Se anulan TODAS las activas, no solo una: el padrón importado tiene clientes con más de
        // una tarjeta (la regla no existía cuando se cargó), y el alta es el momento de normalizar.
        foreach (var t in delCliente.Where(x => x.Activa && x != nueva))
        {
            t.Activa = false;
            t.FechaBajaUtc = DateTime.UtcNow;
            anuladas++;
        }
        if (activa is not null && activa != nueva)
        {
            nroAnulada = activa.NroTarjeta;
            tipoAnulada = await _db.TiposTarjeta.AsNoTracking()
                .Where(t => t.IdTipoTarjeta == activa.IdTipoTarjeta).Select(t => t.Descripcion)
                .FirstOrDefaultAsync(ct);
        }

        if (nueva is not null)
        {
            // Ya existía anulada (le devuelven una tarjeta vieja): se reactiva en vez de insertar,
            // que chocaría con la clave (IdCliente, IdTipoTarjeta, NroTarjeta).
            nueva.Activa = true;
            nueva.FechaBajaUtc = null;
        }
        else
        {
            _db.TarjetasClientes.Add(new TarjetaCliente
            {
                IdCliente = idCliente, IdTipoTarjeta = input.IdTipoTarjeta, NroTarjeta = nro, Activa = true
            });
        }

        await _db.SaveChangesAsync(ct);
        return new AltaTarjetaResultado(true, anuladas, nroAnulada, tipoAnulada);
    }

    public async Task<bool> RemoveTarjetaAsync(int idCliente, int idTipoTarjeta, string nroTarjeta, CancellationToken ct = default)
    {
        var row = await _db.TarjetasClientes.FirstOrDefaultAsync(
            x => x.IdCliente == idCliente && x.IdTipoTarjeta == idTipoTarjeta && x.NroTarjeta == nroTarjeta, ct);
        if (row is null) return false;
        _db.TarjetasClientes.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public class PadronService : IPadronService
{
    /// <summary>El padrón tiene cientos de miles de filas: la lista es para buscar un CUIT puntual.</summary>
    public const int MaxResultados = 10;

    private readonly PosDbContext _db;
    public PadronService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<PadronIibbDto>> GetIibbAsync(string? filtro, CancellationToken ct = default)
    {
        var q = _db.PadronIngresosBrutos.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filtro)) q = q.Where(p => p.Cuit.Contains(filtro.Trim()));
        return await q.OrderBy(p => p.Cuit).Take(MaxResultados)
            .Select(p => new PadronIibbDto(p.Cuit, p.Percepcion)).ToListAsync(ct);
    }

    /// <summary>
    /// Reemplaza el padrón de IIBB completo. El archivo real trae ~5,5 millones de líneas (300 MB),
    /// así que NO puede pasar por EF fila por fila: se lee del stream a medida que llega (nunca
    /// entra completo en memoria) y se vuelca con SqlBulkCopy.
    /// </summary>
    public async Task<ImportacionPadronDto> ImportarIibbAsync(Stream archivo, bool incluirSinPercepcion = false,
        CancellationToken ct = default)
    {
        var sinPercepcion = 0;

        return await ReemplazarPadronAsync(archivo,
            destino: "PadronIngresosBrutos",
            columnasStage: "Cuit char(11) NOT NULL, Percepcion decimal(18,4) NOT NULL",
            armarTabla: () =>
            {
                var t = new DataTable();
                t.Columns.Add("Cuit", typeof(string));
                t.Columns.Add("Percepcion", typeof(decimal));
                return t;
            },
            parsear: (linea, tabla) =>
            {
                if (!PadronRgsParser.TryParse(linea, out var fila)) return ResultadoLinea.Invalida;
                if (fila.Percepcion == 0 && !incluirSinPercepcion) { sinPercepcion++; return ResultadoLinea.Omitida; }
                tabla.Rows.Add(fila.Cuit, fila.Percepcion);
                return ResultadoLinea.Cargada;
            },
            // GROUP BY y no DISTINCT: el CUIT es la PK del destino, así que un archivo con el mismo
            // CUIT repetido (con distinta alícuota) reventaría el INSERT. Gana la alícuota mayor.
            insertSql: "INSERT INTO PadronIngresosBrutos (Cuit, Percepcion, CreatedAtUtc, CreatedBy) " +
                       "SELECT Cuit, MAX(Percepcion), @ahora, @por FROM #stage GROUP BY Cuit",
            omitidas: () => sinPercepcion,
            ct: ct);
    }

    /// <summary>
    /// Reemplaza el padrón de excepción de percepción de IVA. A diferencia del de IIBB, este viene
    /// en ancho fijo (sin separadores): el CUIT son los primeros 11 caracteres de cada línea. No hay
    /// alícuota — estar en el padrón ES la excepción.
    /// </summary>
    public async Task<ImportacionPadronDto> ImportarExcepcionIvaAsync(Stream archivo, CancellationToken ct = default) =>
        await ReemplazarPadronAsync(archivo,
            destino: "PadronExcepcionPercepcionesIva",
            columnasStage: "Cuit char(11) NOT NULL",
            armarTabla: () =>
            {
                var t = new DataTable();
                t.Columns.Add("Cuit", typeof(string));
                return t;
            },
            parsear: (linea, tabla) =>
            {
                if (!PadronRgsParser.TryParseCuitExcepcionIva(linea, out var cuit)) return ResultadoLinea.Invalida;
                tabla.Rows.Add(cuit);
                return ResultadoLinea.Cargada;
            },
            insertSql: "INSERT INTO PadronExcepcionPercepcionesIva (Cuit, CreatedAtUtc, CreatedBy) " +
                       "SELECT DISTINCT Cuit, @ahora, @por FROM #stage",
            omitidas: () => 0,
            ct: ct);

    private enum ResultadoLinea { Cargada, Omitida, Invalida }

    /// <summary>
    /// Motor común de los dos importadores: stream -> tabla temporal (SqlBulkCopy en tandas) ->
    /// borrar el padrón -> insertar desde la temporal, todo en UNA transacción. Si algo falla, el
    /// padrón anterior queda intacto.
    /// <para>Se pasa por una tabla temporal en vez de insertar directo al destino para poder
    /// deduplicar en SQL: el CUIT es la clave primaria y un archivo con repetidos abortaría el
    /// bulk copy a mitad de camino.</para>
    /// </summary>
    private async Task<ImportacionPadronDto> ReemplazarPadronAsync(
        Stream archivo, string destino, string columnasStage, Func<DataTable> armarTabla,
        Func<string, DataTable, ResultadoLinea> parsear, string insertSql, Func<int> omitidas,
        CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        int leidas = 0, cargadas = 0, invalidas = 0, borradas, importadas;
        var ahora = DateTime.UtcNow;
        const string por = "import:padron";
        const int tamTanda = 20_000;

        var conn = (SqlConnection)_db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var tx = (SqlTransaction)(await conn.BeginTransactionAsync(ct));
        try
        {
            await EjecutarAsync(conn, tx, "CREATE TABLE #stage (" + columnasStage + ")", ct);

            var tabla = armarTabla();
            using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, tx)
            {
                DestinationTableName = "#stage", BulkCopyTimeout = 0, BatchSize = tamTanda
            })
            {
                foreach (DataColumn col in tabla.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

                using var lector = new StreamReader(archivo, System.Text.Encoding.Latin1);
                while (await lector.ReadLineAsync(ct) is { } linea)
                {
                    if (linea.Length == 0) continue;
                    leidas++;

                    switch (parsear(linea, tabla))
                    {
                        case ResultadoLinea.Cargada: cargadas++; break;
                        case ResultadoLinea.Invalida: invalidas++; break;
                    }

                    // La tabla en memoria se vacía en cada tanda: acumularlo todo antes de escribir
                    // volvería a meter el archivo entero en RAM.
                    if (tabla.Rows.Count >= tamTanda)
                    {
                        await bulk.WriteToServerAsync(tabla, ct);
                        tabla.Clear();
                    }
                }
                if (tabla.Rows.Count > 0) await bulk.WriteToServerAsync(tabla, ct);
            }

            if (leidas > 0 && cargadas == 0)
                throw new DomainException("PADRON_SIN_FILAS_VALIDAS",
                    "Se leyeron " + leidas.ToString("N0") + " líneas y ninguna tenía un CUIT válido en la columna esperada.");

            borradas = await EjecutarAsync(conn, tx, "DELETE FROM " + destino, ct);
            importadas = await EjecutarAsync(conn, tx, insertSql, ct, ahora, por);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }

        return new ImportacionPadronDto(leidas, importadas, omitidas(), invalidas, borradas,
            reloj.ElapsedMilliseconds);
    }

    private static async Task<int> EjecutarAsync(SqlConnection conn, SqlTransaction tx, string sql,
        CancellationToken ct, DateTime? ahora = null, string? por = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        if (ahora is not null) cmd.Parameters.AddWithValue("@ahora", ahora.Value);
        if (por is not null) cmd.Parameters.AddWithValue("@por", por);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertIibbAsync(PadronIibbInput input, CancellationToken ct = default)
    {
        var cuit = input.Cuit.Trim();
        var row = await _db.PadronIngresosBrutos.FirstOrDefaultAsync(p => p.Cuit == cuit, ct);
        if (row is null)
            _db.PadronIngresosBrutos.Add(new PadronIngresosBrutos { Cuit = cuit, Percepcion = input.Percepcion });
        else
            row.Percepcion = input.Percepcion;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteIibbAsync(string cuit, CancellationToken ct = default)
    {
        var row = await _db.PadronIngresosBrutos.FirstOrDefaultAsync(p => p.Cuit == cuit, ct);
        if (row is null) return false;
        _db.PadronIngresosBrutos.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<PadronExIvaDto>> GetExIvaAsync(string? filtro, CancellationToken ct = default)
    {
        var q = _db.PadronExcepcionPercepcionesIva.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filtro)) q = q.Where(p => p.Cuit.Contains(filtro.Trim()));
        return await q.OrderBy(p => p.Cuit).Take(MaxResultados)
            .Select(p => new PadronExIvaDto(p.Cuit)).ToListAsync(ct);
    }

    public async Task AddExIvaAsync(string cuit, CancellationToken ct = default)
    {
        cuit = cuit.Trim();
        if (!await _db.PadronExcepcionPercepcionesIva.AnyAsync(p => p.Cuit == cuit, ct))
        {
            _db.PadronExcepcionPercepcionesIva.Add(new PadronExcepcionPercepcionIva { Cuit = cuit });
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> DeleteExIvaAsync(string cuit, CancellationToken ct = default)
    {
        var row = await _db.PadronExcepcionPercepcionesIva.FirstOrDefaultAsync(p => p.Cuit == cuit, ct);
        if (row is null) return false;
        _db.PadronExcepcionPercepcionesIva.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// ABM del límite de crédito de cuenta corriente por cliente/sucursal (ClienteEnCuenta). El
/// control real de crédito al facturar vive en FacturacionService/CuentaCorrienteReglas — esto
/// solo administra el límite y muestra el saldo actual (Debe-Haber acumulado en CuentaCorriente).
/// </summary>
public class ClienteEnCuentaService : IClienteEnCuentaService
{
    private readonly PosDbContext _db;
    public ClienteEnCuentaService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<CuentaCorrienteLimiteDto>> GetAsync(int idSucursal, CancellationToken ct = default)
    {
        var cuentas = await (
            from c in _db.ClientesEnCuenta.AsNoTracking().Where(x => x.IdSucursal == idSucursal)
            join cl in _db.Clientes.AsNoTracking() on c.IdCliente equals cl.IdCliente
            orderby cl.Descripcion
            select new { c.IdCliente, cl.Descripcion, c.LimiteCredito }
        ).ToListAsync(ct);

        var idsCliente = cuentas.Select(x => x.IdCliente).ToList();
        var saldos = await _db.CuentasCorrientes.AsNoTracking()
            .Where(x => x.IdSucursal == idSucursal && idsCliente.Contains(x.IdCliente))
            .GroupBy(x => x.IdCliente)
            .Select(g => new { IdCliente = g.Key, Debe = g.Sum(x => x.Debe), Haber = g.Sum(x => x.Haber) })
            .ToDictionaryAsync(x => x.IdCliente, ct);

        return cuentas.Select(c => new CuentaCorrienteLimiteDto(idSucursal, c.IdCliente, c.Descripcion,
            c.LimiteCredito,
            saldos.TryGetValue(c.IdCliente, out var s)
                ? CuentaCorrienteReglas.CalcularSaldo(s.Debe, s.Haber)
                : 0m)).ToList();
    }

    public async Task UpsertAsync(int idSucursal, int idCliente, CuentaCorrienteLimiteInput input, CancellationToken ct = default)
    {
        if (input.LimiteCredito < 0)
            throw new DomainException("LIMITE_INVALIDO", "El límite de crédito no puede ser negativo.");
        var cliente = await _db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.IdCliente == idCliente, ct)
            ?? throw new DomainException("CLIENTE_INEXISTENTE", "El cliente no existe.");
        // El buscador de la pantalla ya ofrece solo los habilitados; esto cierra la puerta por API
        // para que la regla no viva únicamente en el frontend.
        if (!cliente.AdmiteCuentaCorriente)
            throw new DomainException("CLIENTE_NO_ADMITE_CUENTA_CORRIENTE",
                $"{cliente.Descripcion} no admite cuenta corriente. Habilitalo en el ABM de clientes.");

        var existente = await _db.ClientesEnCuenta
            .FirstOrDefaultAsync(c => c.IdSucursal == idSucursal && c.IdCliente == idCliente, ct);
        if (existente is null)
            _db.ClientesEnCuenta.Add(new ClienteEnCuenta
            {
                IdSucursal = idSucursal, IdCliente = idCliente, LimiteCredito = input.LimiteCredito
            });
        else
            existente.LimiteCredito = input.LimiteCredito;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int idSucursal, int idCliente, CancellationToken ct = default)
    {
        var existente = await _db.ClientesEnCuenta
            .FirstOrDefaultAsync(c => c.IdSucursal == idSucursal && c.IdCliente == idCliente, ct);
        if (existente is null) return false;
        _db.ClientesEnCuenta.Remove(existente);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
