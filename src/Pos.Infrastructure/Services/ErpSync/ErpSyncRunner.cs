using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions.ErpSync;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services.ErpSync;

/// <summary>
/// Orquesta la importación incremental desde el ERP Central (DATA_PREV): lookups (Sector/Línea/
/// Familia/ModoIVA), artículos con sus presentaciones y códigos de barra, y clientes. Pensado para
/// correr desde el proceso de consola Pos.ErpSync, disparado por Windows Task Scheduler — no vive
/// dentro de la API. Ver memoria "pos-mayorista-erp-sync" para el diseño acordado y sus motivos.
/// </summary>
public class ErpSyncRunner
{
    private const string AutorSync = "ERP-Sync";

    private readonly PosDbContext _db;
    private readonly IErpLookupReader _lookups;
    private readonly IErpArticuloReader _articulos;
    private readonly IErpClienteReader _clientes;
    private readonly ErpSyncOptions _options;
    private readonly ILogger<ErpSyncRunner> _log;

    public ErpSyncRunner(
        PosDbContext db, IErpLookupReader lookups, IErpArticuloReader articulos, IErpClienteReader clientes,
        ErpSyncOptions options, ILogger<ErpSyncRunner> log)
    {
        _db = db;
        _lookups = lookups;
        _articulos = articulos;
        _clientes = clientes;
        _options = options;
        _log = log;
    }

    public async Task EjecutarAsync(CancellationToken ct)
    {
        await SincronizarFuenteAsync(ErpSyncFuentes.Lookups, SincronizarLookupsAsync, ct);
        await SincronizarFuenteAsync(ErpSyncFuentes.Articulos, SincronizarArticulosAsync, ct);
        await SincronizarFuenteAsync(ErpSyncFuentes.Presentaciones, SincronizarPresentacionesAsync, ct);
        await SincronizarFuenteAsync(ErpSyncFuentes.CodBarras, SincronizarCodBarrasAsync, ct);
        await SincronizarFuenteAsync(ErpSyncFuentes.Clientes, SincronizarClientesAsync, ct);
    }

    // ----- Orquestación por fuente: gating de frecuencia + contadores + manejo de errores -----

    private async Task SincronizarFuenteAsync(string fuente, Func<SyncCheckpoint, CancellationToken, Task> accion, CancellationToken ct)
    {
        var checkpoint = await _db.SyncCheckpoints.FirstOrDefaultAsync(x => x.Fuente == fuente, ct);
        if (checkpoint is null)
        {
            checkpoint = new SyncCheckpoint { Fuente = fuente };
            _db.SyncCheckpoints.Add(checkpoint);
            await _db.SaveChangesAsync(ct);
        }

        if (checkpoint.UltimaCorridaUtc is DateTime ultima &&
            DateTime.UtcNow - ultima < TimeSpan.FromMinutes(_options.FrecuenciaMinutos))
        {
            _log.LogInformation("Sync {Fuente}: todavía no toca (última corrida {Ultima:u}, frecuencia {Frecuencia} min).",
                fuente, ultima, _options.FrecuenciaMinutos);
            return;
        }

        checkpoint.Insertados = 0;
        checkpoint.Actualizados = 0;
        checkpoint.Errores = 0;
        try
        {
            await accion(checkpoint, ct);
            checkpoint.UltimoResultado = checkpoint.Errores > 0 ? "Parcial" : "Ok";
        }
        catch (Exception ex)
        {
            checkpoint.UltimoResultado = "Error";
            _log.LogError(ex, "Sync {Fuente} falló.", fuente);
        }
        finally
        {
            checkpoint.UltimaCorridaUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _log.LogInformation(
                "Sync {Fuente} terminó: {Resultado}, insertados={Insertados}, actualizados={Actualizados}, errores={Errores}.",
                fuente, checkpoint.UltimoResultado, checkpoint.Insertados, checkpoint.Actualizados, checkpoint.Errores);
        }
    }

    // ----- Lookups: Sector/Línea/Familia/ModoIVA. Tablas chicas, se traen enteras cada vez (sin
    // watermark) y se crea lo que falte — CondicionIva es la única excepción: no se auto-crea porque
    // alimenta la letra de facturación ARCA (ver comentario en la entidad). -----

    private async Task SincronizarLookupsAsync(SyncCheckpoint checkpoint, CancellationToken ct)
    {
        await UpsertLookupAsync(await _lookups.GetSectoresAsync(ct),
            async r =>
            {
                var existente = await _db.Sectores.FirstOrDefaultAsync(x => x.CodigoErp == r.Codigo, ct);
                if (existente is null) { _db.Sectores.Add(new Sector { CodigoErp = r.Codigo, Descripcion = r.Descripcion, CreatedBy = AutorSync }); checkpoint.Insertados++; }
                else if (existente.Descripcion != r.Descripcion) { existente.Descripcion = r.Descripcion; existente.UpdatedBy = AutorSync; checkpoint.Actualizados++; }
            }, ct);

        await UpsertLookupAsync(await _lookups.GetLineasAsync(ct),
            async r =>
            {
                var existente = await _db.Lineas.FirstOrDefaultAsync(x => x.CodigoErp == r.Codigo, ct);
                if (existente is null) { _db.Lineas.Add(new Linea { CodigoErp = r.Codigo, Descripcion = r.Descripcion, CreatedBy = AutorSync }); checkpoint.Insertados++; }
                else if (existente.Descripcion != r.Descripcion) { existente.Descripcion = r.Descripcion; existente.UpdatedBy = AutorSync; checkpoint.Actualizados++; }
            }, ct);

        await UpsertLookupAsync(await _lookups.GetModosIvaAsync(ct),
            async r =>
            {
                var existente = await _db.ModosIva.FirstOrDefaultAsync(x => x.CodigoErp == r.Codigo, ct);
                if (existente is null)
                {
                    _db.ModosIva.Add(new ModoIva
                    {
                        CodigoErp = r.Codigo, Descripcion = r.Descripcion,
                        Alicuota = r.Alicuota / 100m, PorcentajePercepcion = r.Percepcion, CreatedBy = AutorSync
                    });
                    checkpoint.Insertados++;
                }
                else if (existente.Descripcion != r.Descripcion || existente.Alicuota != r.Alicuota / 100m)
                {
                    existente.Descripcion = r.Descripcion;
                    existente.Alicuota = r.Alicuota / 100m;
                    existente.PorcentajePercepcion = r.Percepcion;
                    existente.UpdatedBy = AutorSync;
                    checkpoint.Actualizados++;
                }
            }, ct);

        // Se guarda ACÁ (no al final del método): Familia necesita resolver Sector por IdSector real,
        // y hasta que esto no se confirma contra la base los Sectores recién agregados no tienen id
        // asignado ni son visibles para la consulta de abajo — sin este save, sectoresPorCodigo salía
        // vacío y TODAS las familias colapsaban a IdSector=null, chocando entre sí por código
        // duplicado (bug real, encontrado en la prueba controlada del 2026-09-23).
        await _db.SaveChangesAsync(ct);

        // Familia depende de Sector, ya sincronizado y confirmado arriba. El código de familia del
        // ERP NO es único globalmente (solo dentro de su sector — ver comentario en PosDbContext),
        // así que la clave de matcheo es el PAR (sector, código), no el código solo.
        var sectoresPorCodigo = await _db.Sectores.Where(s => s.CodigoErp != null).ToDictionaryAsync(s => s.CodigoErp!, s => s.IdSector, ct);
        await UpsertLookupAsync(await _lookups.GetFamiliasAsync(ct),
            async r =>
            {
                var idSector = r.CodigoSectorErp is not null && sectoresPorCodigo.TryGetValue(r.CodigoSectorErp, out var id) ? id : (int?)null;
                var existente = await _db.Familias.FirstOrDefaultAsync(x => x.CodigoErp == r.Codigo && x.IdSector == idSector, ct);
                if (existente is null)
                {
                    _db.Familias.Add(new Familia { CodigoErp = r.Codigo, Descripcion = r.Descripcion, IdSector = idSector, CreatedBy = AutorSync });
                    checkpoint.Insertados++;
                }
                else if (existente.Descripcion != r.Descripcion)
                {
                    existente.Descripcion = r.Descripcion;
                    existente.UpdatedBy = AutorSync;
                    checkpoint.Actualizados++;
                }
            }, ct);

        await _db.SaveChangesAsync(ct);
    }

    private static async Task UpsertLookupAsync<T>(IReadOnlyList<T> filas, Func<T, Task> upsert, CancellationToken ct)
    {
        foreach (var fila in filas)
        {
            ct.ThrowIfCancellationRequested();
            await upsert(fila);
        }
    }

    // ----- Artículos -----

    private async Task SincronizarArticulosAsync(SyncCheckpoint checkpoint, CancellationToken ct)
    {
        var sectores = await _db.Sectores.Where(s => s.CodigoErp != null).ToDictionaryAsync(s => s.CodigoErp!, s => s.IdSector, ct);
        var lineas = await _db.Lineas.Where(l => l.CodigoErp != null).ToDictionaryAsync(l => l.CodigoErp!, l => l.IdLinea, ct);
        var modosIva = await _db.ModosIva.Where(m => m.CodigoErp != null).ToDictionaryAsync(m => m.CodigoErp!, m => m.IdModoIva, ct);
        // Familia se resuelve por el PAR (sector, código): el código de familia del ERP no es único
        // globalmente, solo dentro de su sector (ver comentario en PosDbContext).
        var familiasPorSectorYCodigo = await _db.Familias.Where(f => f.CodigoErp != null)
            .ToDictionaryAsync(f => (f.IdSector, f.CodigoErp!), f => f.IdFamilia, ct);
        // Articulo.IdFamilia no es nullable: los artículos que en el ERP no cuelgan de ninguna
        // familia (idFamilia NULL, el "SIN FAMILIA" legado) necesitan igual una fila local válida.
        var idFamiliaSinFamilia = await ObtenerOCrearSinFamiliaAsync(ct);

        var watermark = checkpoint.UltimoWatermarkUtc ?? DateTime.MinValue;
        var ultimoId = checkpoint.UltimoIdErp;
        var lotesProcesados = 0;
        while (true)
        {
            var lote = await _articulos.GetArticulosModificadosAsync(watermark, ultimoId, _options.LoteSize, ct);
            if (lote.Count == 0) break;

            foreach (var fila in lote)
            {
                if (!sectores.TryGetValue(fila.CodigoSectorErp, out var idSector) ||
                    !lineas.TryGetValue(fila.CodigoLineaErp, out var idLinea) ||
                    !modosIva.TryGetValue(fila.CodigoModoIvaErp, out var idModoIva))
                {
                    _log.LogWarning("Artículo ERP {IdErp} ({Codigo}): no se pudo resolver Sector/Línea/ModoIva, se salta.",
                        fila.IdErp, fila.Codigo);
                    checkpoint.Errores++;
                    continue;
                }
                var idFamilia = (fila.CodigoFamiliaErp is not null && familiasPorSectorYCodigo.TryGetValue(((int?)idSector, fila.CodigoFamiliaErp), out var idFam))
                    ? idFam : idFamiliaSinFamilia;

                var codigoInterno = NormalizarCodigoArticulo(fila.Codigo);
                var existente = await _db.Articulos.FirstOrDefaultAsync(a => a.IdErp == fila.IdErp, ct)
                    ?? await _db.Articulos.FirstOrDefaultAsync(a => a.IdErp == null && a.CodigoInterno == codigoInterno, ct);

                var activo = fila.Estado != 3; // 0/1/2 activo (2 = suspendido, igual se puede vender), 3 = inactivo
                if (existente is null)
                {
                    _db.Articulos.Add(new Articulo
                    {
                        IdErp = fila.IdErp, CodigoInterno = codigoInterno, Descripcion = fila.DescripcionFull,
                        IdSector = idSector, IdLinea = idLinea, IdFamilia = idFamilia, IdModoIva = idModoIva,
                        Activo = activo, EstadoErp = fila.Estado, UnidadXBulto = fila.UnidadBulto <= 0 ? 1m : fila.UnidadBulto,
                        CreatedBy = AutorSync
                    });
                    checkpoint.Insertados++;
                }
                else
                {
                    existente.IdErp = fila.IdErp;
                    existente.CodigoInterno = codigoInterno;
                    existente.Descripcion = fila.DescripcionFull;
                    existente.IdSector = idSector;
                    existente.IdLinea = idLinea;
                    existente.IdFamilia = idFamilia;
                    existente.IdModoIva = idModoIva;
                    existente.Activo = activo;
                    existente.EstadoErp = fila.Estado;
                    existente.UnidadXBulto = fila.UnidadBulto <= 0 ? 1m : fila.UnidadBulto;
                    existente.UpdatedBy = AutorSync;
                    checkpoint.Actualizados++;
                }
            }

            // El lote viene ordenado por (fecha, id) — se avanza a la ÚLTIMA fila procesada, no al
            // máximo de fecha del lote: con touches masivos (miles de filas al mismo timestamp) el
            // máximo de fecha por sí solo no alcanza para saber hasta qué id se llegó dentro de ese
            // timestamp, y perdía las filas restantes del empate (bug real, 2026-09-23).
            var ultima = lote[^1];
            watermark = ultima.FechaModificacion;
            ultimoId = ultima.IdErp;
            checkpoint.UltimoWatermarkUtc = watermark;
            checkpoint.UltimoIdErp = ultimoId;
            await _db.SaveChangesAsync(ct);

            lotesProcesados++;
            if (lote.Count < _options.LoteSize) break;
            if (_options.MaxLotesPorFuente > 0 && lotesProcesados >= _options.MaxLotesPorFuente) break;
        }
    }

    /// <summary>
    /// Familia "SIN FAMILIA" local para los artículos que en el ERP no cuelgan de ninguna
    /// (idFamilia NULL). No tiene CodigoErp propio (no viene de T_Familias) — se identifica por
    /// descripción y se crea una sola vez.
    /// </summary>
    private async Task<int> ObtenerOCrearSinFamiliaAsync(CancellationToken ct)
    {
        const string descripcion = "SIN FAMILIA";
        var existente = await _db.Familias.FirstOrDefaultAsync(f => f.CodigoErp == null && f.Descripcion == descripcion, ct);
        if (existente is not null) return existente.IdFamilia;

        var nueva = new Familia { Descripcion = descripcion, IdSector = null, CreatedBy = AutorSync };
        _db.Familias.Add(nueva);
        await _db.SaveChangesAsync(ct);
        return nueva.IdFamilia;
    }

    // ----- Presentaciones (requieren el Articulo ya sincronizado por IdErp) -----

    private async Task SincronizarPresentacionesAsync(SyncCheckpoint checkpoint, CancellationToken ct)
    {
        var watermark = checkpoint.UltimoWatermarkUtc ?? DateTime.MinValue;
        var ultimoId = checkpoint.UltimoIdErp;
        var lotesProcesados = 0;
        while (true)
        {
            var lote = await _articulos.GetPresentacionesModificadasAsync(watermark, ultimoId, _options.LoteSize, ct);
            if (lote.Count == 0) break;

            // Precarga por lote (2 consultas IN en vez de hasta 3 por fila): con lotes de cientos o
            // miles de filas, una query por fila hacía que un lote tardara minutos por puro ida-y-vuelta
            // a la base (bug de performance real, encontrado en la primera corrida completa).
            var idsArticuloErp = lote.Select(f => f.IdArticuloErp).Distinct().ToList();
            var articulosPorIdErp = await _db.Articulos.Where(a => idsArticuloErp.Contains(a.IdErp!.Value))
                .ToDictionaryAsync(a => a.IdErp!.Value, a => a.IdArticulo, ct);
            var idsErpDelLote = lote.Select(f => f.IdErp).ToList();
            var presentacionesExistentes = await _db.Presentaciones.Where(p => idsErpDelLote.Contains(p.IdErp!.Value))
                .ToDictionaryAsync(p => p.IdErp!.Value, p => p, ct);

            foreach (var fila in lote)
            {
                if (!articulosPorIdErp.TryGetValue(fila.IdArticuloErp, out var idArticuloValue))
                {
                    _log.LogWarning("Presentación ERP {IdErp}: el artículo ERP {IdArticuloErp} todavía no está sincronizado, se salta (se resuelve en la próxima corrida).",
                        fila.IdErp, fila.IdArticuloErp);
                    checkpoint.Errores++;
                    continue;
                }
                var idArticulo = (int?)idArticuloValue;
                // baja=1 en el ERP: por ahora no se borra ni se oculta (Presentacion no tiene un
                // Activo/Baja local todavía — queda pendiente, ver memoria pos-mayorista-erp-sync
                // punto 3), pero tampoco se crea una presentación nueva si es la primera vez que se ve.
                // LogDebug (no Information): en catálogos con muchas bajas históricas esto puede ser la
                // mayoría de las filas de un lote, y a nivel INFO saturaba la consola/archivo de log.
                if (fila.Baja && !presentacionesExistentes.ContainsKey(fila.IdErp))
                {
                    _log.LogDebug("Presentación ERP {IdErp} está dada de baja en el ERP y no existe localmente, se ignora.", fila.IdErp);
                    continue;
                }

                var existente = presentacionesExistentes.GetValueOrDefault(fila.IdErp);
                if (existente is null)
                {
                    _db.Presentaciones.Add(new Presentacion
                    {
                        IdErp = fila.IdErp, IdArticulo = idArticulo.Value,
                        UnidadXBulto = fila.Fraccion <= 0 ? 1m : fila.Fraccion,
                        DescripcionTicket = fila.Codigo, CreatedBy = AutorSync
                    });
                    checkpoint.Insertados++;
                }
                else
                {
                    existente.IdArticulo = idArticulo.Value;
                    existente.UnidadXBulto = fila.Fraccion <= 0 ? 1m : fila.Fraccion;
                    existente.DescripcionTicket = fila.Codigo;
                    existente.UpdatedBy = AutorSync;
                    checkpoint.Actualizados++;
                }
            }

            // El lote viene ordenado por (fecha, id) — se avanza a la ÚLTIMA fila procesada, no al
            // máximo de fecha del lote: con touches masivos (miles de filas al mismo timestamp) el
            // máximo de fecha por sí solo no alcanza para saber hasta qué id se llegó dentro de ese
            // timestamp, y perdía las filas restantes del empate (bug real, 2026-09-23).
            var ultima = lote[^1];
            watermark = ultima.FechaModificacion;
            ultimoId = ultima.IdErp;
            checkpoint.UltimoWatermarkUtc = watermark;
            checkpoint.UltimoIdErp = ultimoId;
            await _db.SaveChangesAsync(ct);

            lotesProcesados++;
            if (lote.Count < _options.LoteSize) break;
            if (_options.MaxLotesPorFuente > 0 && lotesProcesados >= _options.MaxLotesPorFuente) break;
        }
    }

    // ----- Códigos de barra (requieren la Presentacion ya sincronizada por IdErp) -----

    private async Task SincronizarCodBarrasAsync(SyncCheckpoint checkpoint, CancellationToken ct)
    {
        var watermark = checkpoint.UltimoWatermarkUtc ?? DateTime.MinValue;
        var ultimoId = checkpoint.UltimoIdErp;
        var lotesProcesados = 0;
        while (true)
        {
            var lote = await _articulos.GetCodBarrasModificadosAsync(watermark, ultimoId, _options.LoteSize, ct);
            if (lote.Count == 0) break;

            // Códigos ya reservados EN ESTE LOTE (además de lo que ya hay en la base): Barra.CodigoBarra
            // es único globalmente en pos-mayorista (un escaneo en Caja tiene que resolver a un único
            // producto) pero el ERP no exige lo mismo — un mismo código puede aparecer repetido bajo
            // presentaciones distintas (visto en producción). Sin este control, la segunda fila del
            // MISMO lote todavía no está en la base (recién se guarda al final) y el chequeo contra la
            // base no la detecta, así que igual rompía el INSERT.
            var codigosReservadosEnLote = new Dictionary<string, int>();

            // Precarga por lote (2 consultas IN en vez de hasta 2 por fila) — mismo motivo que en
            // SincronizarPresentacionesAsync.
            var idsPresentacionErp = lote.Select(f => f.IdPresentacionErp).Distinct().ToList();
            var presentacionesPorIdErp = await _db.Presentaciones.Where(p => idsPresentacionErp.Contains(p.IdErp!.Value))
                .ToDictionaryAsync(p => p.IdErp!.Value, p => p.IdPresentacion, ct);
            var codigosDelLote = lote.Select(f => f.Codigo).Distinct().ToList();
            var barrasPorCodigo = await _db.Barras.Where(b => codigosDelLote.Contains(b.CodigoBarra))
                .ToDictionaryAsync(b => b.CodigoBarra, b => b, ct);

            foreach (var fila in lote)
            {
                if (!presentacionesPorIdErp.TryGetValue(fila.IdPresentacionErp, out var idPresentacionValue))
                {
                    _log.LogWarning("Código de barra {Codigo}: la presentación ERP {IdPresentacionErp} todavía no está sincronizada, se salta.",
                        fila.Codigo, fila.IdPresentacionErp);
                    checkpoint.Errores++;
                    continue;
                }
                var idPresentacion = (int?)idPresentacionValue;
                var tipo = string.Equals(fila.TipoCodigo, "DUN", StringComparison.OrdinalIgnoreCase) ? TipoBarra.Dun14 : TipoBarra.Ean13;

                if (codigosReservadosEnLote.TryGetValue(fila.Codigo, out var idPresentacionReservada))
                {
                    // Ya se procesó este código en este mismo lote (para esta u otra presentación):
                    // no hay nada más que hacer con esta fila, sea porque ya se agregó (misma
                    // presentación, fila repetida) o porque ya se logueó el conflicto (otra presentación).
                    if (idPresentacionReservada != idPresentacion)
                    {
                        _log.LogWarning("Código de barra {Codigo}: repetido en el mismo lote bajo dos presentaciones distintas del ERP, se salta.", fila.Codigo);
                        checkpoint.Errores++;
                    }
                    continue;
                }

                // Clave natural: (presentación local, código de barra). Pero CodigoBarra también es
                // único GLOBAL en pos-mayorista — si el ERP lo repite bajo otra presentación, no se
                // puede insertar sin violar esa invariante (que además es real: evita que un mismo
                // código escaneado en Caja resuelva a dos productos). Se salta y se loguea para que
                // alguien lo resuelva a mano en el ERP, no se rompe todo el lote por una fila sucia.
                var existente = barrasPorCodigo.GetValueOrDefault(fila.Codigo);
                if (existente is not null && existente.IdPresentacion != idPresentacion)
                {
                    _log.LogWarning("Código de barra {Codigo}: ya existe localmente en otra presentación (Id {IdPresentacionLocal}), el ERP lo trae bajo la presentación ERP {IdPresentacionErp} — se salta.",
                        fila.Codigo, existente.IdPresentacion, fila.IdPresentacionErp);
                    checkpoint.Errores++;
                    continue;
                }

                codigosReservadosEnLote[fila.Codigo] = idPresentacion.Value;
                if (existente is null)
                {
                    _db.Barras.Add(new Barra { IdPresentacion = idPresentacion.Value, CodigoBarra = fila.Codigo, Tipo = tipo, CreatedBy = AutorSync });
                    checkpoint.Insertados++;
                }
                else if (existente.Tipo != tipo)
                {
                    existente.Tipo = tipo;
                    existente.UpdatedBy = AutorSync;
                    checkpoint.Actualizados++;
                }
            }

            // El lote viene ordenado por (fecha, id) — se avanza a la ÚLTIMA fila procesada, no al
            // máximo de fecha del lote: con touches masivos (miles de filas al mismo timestamp) el
            // máximo de fecha por sí solo no alcanza para saber hasta qué id se llegó dentro de ese
            // timestamp, y perdía las filas restantes del empate (bug real, 2026-09-23).
            var ultima = lote[^1];
            watermark = ultima.FechaModificacion;
            ultimoId = ultima.IdErp;
            checkpoint.UltimoWatermarkUtc = watermark;
            checkpoint.UltimoIdErp = ultimoId;
            await _db.SaveChangesAsync(ct);

            lotesProcesados++;
            if (lote.Count < _options.LoteSize) break;
            if (_options.MaxLotesPorFuente > 0 && lotesProcesados >= _options.MaxLotesPorFuente) break;
        }
    }

    /// <summary>El ERP guarda el código de artículo con 13 dígitos, completado con ceros a la
    /// izquierda ("0000000025004"); el padrón legacy de pos-mayorista lo tiene cargado sin esos
    /// ceros ("25004"). Sin esta normalización, el matcheo por CodigoInterno contra artículos
    /// legacy (los que todavía no tienen IdErp) fallaba siempre y el sync terminaba creando un
    /// artículo DUPLICADO por cada uno en vez de actualizar el existente — bug real que generó
    /// 36.914 duplicados en la primera corrida completa (2026-09-23), limpiados a mano.</summary>
    private static string NormalizarCodigoArticulo(string codigo)
    {
        var sinCeros = codigo.TrimStart('0');
        return sinCeros.Length == 0 ? codigo : sinCeros;
    }

    /// <summary>El ERP guarda el CUIT formateado con guiones ("30-71012233-4", 13 caracteres);
    /// Cliente.Cuit espera los 11 dígitos sin separadores (mismo criterio que el padrón ya cargado
    /// a mano). Sin esto, el INSERT truncaba contra el nvarchar(11) local (bug real, encontrado en
    /// la prueba controlada del 2026-09-23).</summary>
    private static string? NormalizarCuit(string? cuit)
    {
        if (string.IsNullOrWhiteSpace(cuit)) return null;
        var soloDigitos = new string(cuit.Where(char.IsDigit).ToArray());
        if (soloDigitos.Length == 0) return null;
        // Un CUIT válido son 11 dígitos; si el dato del ERP viene más largo (entrada sucia), se
        // recorta en vez de romper el lote entero contra el nvarchar(11) local.
        return soloDigitos.Length > 11 ? soloDigitos[..11] : soloDigitos;
    }

    // ----- Clientes -----

    private async Task SincronizarClientesAsync(SyncCheckpoint checkpoint, CancellationToken ct)
    {
        var condicionesIva = await _db.CondicionesIva.Where(c => c.CodigoErp != null).ToDictionaryAsync(c => c.CodigoErp!, c => c.IdCondIva, ct);

        var watermark = checkpoint.UltimoWatermarkUtc ?? DateTime.MinValue;
        var ultimoId = checkpoint.UltimoIdErp;
        var lotesProcesados = 0;
        while (true)
        {
            var lote = await _clientes.GetClientesModificadosAsync(watermark, ultimoId, _options.LoteSize, ct);
            if (lote.Count == 0) break;

            // Precarga por lote — mismo motivo y mismo patrón que en SincronizarPresentacionesAsync /
            // SincronizarCodBarrasAsync (bug de performance real, 2026-09-23): una consulta por fila
            // hacía que un lote de cientos de clientes tardara varios minutos.
            var idsErpDelLote = lote.Select(f => f.IdErp).ToList();
            var codigosDelLote = lote.Select(f => f.Codigo).ToList();
            var clientesExistentes = await _db.Clientes
                .Where(c => idsErpDelLote.Contains(c.IdErp!.Value) || (c.IdErp == null && codigosDelLote.Contains(c.CodigoInt)))
                .ToListAsync(ct);
            var clientesPorIdErp = clientesExistentes.Where(c => c.IdErp is not null).ToDictionary(c => c.IdErp!.Value, c => c);
            var clientesPorCodigo = clientesExistentes.Where(c => c.IdErp is null).ToDictionary(c => c.CodigoInt, c => c);

            foreach (var fila in lote)
            {
                if (!condicionesIva.TryGetValue(fila.CodigoCondIvaErp, out var idCondIva))
                {
                    // No se auto-crea: una condición de IVA sin Letra/CodigoInterno cargados a mano
                    // rompería la facturación ARCA de ese cliente. Ver CondicionIva.CodigoErp.
                    _log.LogWarning("Cliente ERP {IdErp} ({Codigo}): condición de IVA {CodigoCondIva} sin mapear localmente, se salta.",
                        fila.IdErp, fila.Codigo, fila.CodigoCondIvaErp);
                    checkpoint.Errores++;
                    continue;
                }

                var existente = clientesPorIdErp.GetValueOrDefault(fila.IdErp) ?? clientesPorCodigo.GetValueOrDefault(fila.Codigo);

                var activo = fila.Estado != 3; // mismo criterio que Articulo.EstadoErp
                var cuit = NormalizarCuit(fila.Cuit);
                if (existente is null)
                {
                    _db.Clientes.Add(new Cliente
                    {
                        IdErp = fila.IdErp, CodigoInt = fila.Codigo, Descripcion = fila.RazonSocial,
                        NombreFantasia = fila.NombreFantasia, Domicilio = fila.Domicilio, Localidad = fila.Localidad,
                        Cuit = cuit, IdCondIva = idCondIva, Email = fila.Email, Activo = activo,
                        EstadoErp = fila.Estado, CreatedBy = AutorSync
                    });
                    checkpoint.Insertados++;
                }
                else
                {
                    existente.IdErp = fila.IdErp;
                    existente.CodigoInt = fila.Codigo;
                    existente.Descripcion = fila.RazonSocial;
                    existente.NombreFantasia = fila.NombreFantasia;
                    existente.Domicilio = fila.Domicilio;
                    existente.Localidad = fila.Localidad;
                    existente.Cuit = cuit;
                    existente.IdCondIva = idCondIva;
                    existente.Email = fila.Email;
                    existente.Activo = activo;
                    existente.EstadoErp = fila.Estado;
                    existente.UpdatedBy = AutorSync;
                    checkpoint.Actualizados++;
                }
            }

            // Ver comentario equivalente más arriba (Articulos/Presentaciones/CodBarras): se avanza a
            // la última fila del lote ordenado, no al máximo de fecha solo.
            var ultima = lote[^1];
            watermark = ultima.FechaActualizacion;
            ultimoId = ultima.IdErp;
            checkpoint.UltimoWatermarkUtc = watermark;
            checkpoint.UltimoIdErp = ultimoId;
            await _db.SaveChangesAsync(ct);

            lotesProcesados++;
            if (lote.Count < _options.LoteSize) break;
            if (_options.MaxLotesPorFuente > 0 && lotesProcesados >= _options.MaxLotesPorFuente) break;
        }
    }
}
