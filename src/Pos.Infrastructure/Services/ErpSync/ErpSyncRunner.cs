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
        await SincronizarFuenteAsync(ErpSyncFuentes.Lookups, (cp, c) => SincronizarLookupsAsync(c), ct);
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

    private async Task SincronizarLookupsAsync(CancellationToken ct)
    {
        await UpsertLookupAsync(await _lookups.GetSectoresAsync(ct),
            r => r.Codigo, async r =>
            {
                var existente = await _db.Sectores.FirstOrDefaultAsync(x => x.CodigoErp == r.Codigo, ct);
                if (existente is null) _db.Sectores.Add(new Sector { CodigoErp = r.Codigo, Descripcion = r.Descripcion, CreatedBy = AutorSync });
                else if (existente.Descripcion != r.Descripcion) { existente.Descripcion = r.Descripcion; existente.UpdatedBy = AutorSync; }
            }, ct);

        await UpsertLookupAsync(await _lookups.GetLineasAsync(ct),
            r => r.Codigo, async r =>
            {
                var existente = await _db.Lineas.FirstOrDefaultAsync(x => x.CodigoErp == r.Codigo, ct);
                if (existente is null) _db.Lineas.Add(new Linea { CodigoErp = r.Codigo, Descripcion = r.Descripcion, CreatedBy = AutorSync });
                else if (existente.Descripcion != r.Descripcion) { existente.Descripcion = r.Descripcion; existente.UpdatedBy = AutorSync; }
            }, ct);

        await UpsertLookupAsync(await _lookups.GetModosIvaAsync(ct),
            r => r.Codigo, async r =>
            {
                var existente = await _db.ModosIva.FirstOrDefaultAsync(x => x.CodigoErp == r.Codigo, ct);
                if (existente is null)
                    _db.ModosIva.Add(new ModoIva
                    {
                        CodigoErp = r.Codigo, Descripcion = r.Descripcion,
                        Alicuota = r.Alicuota / 100m, PorcentajePercepcion = r.Percepcion, CreatedBy = AutorSync
                    });
                else if (existente.Descripcion != r.Descripcion || existente.Alicuota != r.Alicuota / 100m)
                {
                    existente.Descripcion = r.Descripcion;
                    existente.Alicuota = r.Alicuota / 100m;
                    existente.PorcentajePercepcion = r.Percepcion;
                    existente.UpdatedBy = AutorSync;
                }
            }, ct);

        // Familia depende de Sector, ya sincronizado arriba en esta misma corrida.
        var sectoresPorCodigo = await _db.Sectores.Where(s => s.CodigoErp != null).ToDictionaryAsync(s => s.CodigoErp!, s => s.IdSector, ct);
        await UpsertLookupAsync(await _lookups.GetFamiliasAsync(ct),
            r => r.Codigo, async r =>
            {
                var idSector = r.CodigoSectorErp is not null && sectoresPorCodigo.TryGetValue(r.CodigoSectorErp, out var id) ? id : (int?)null;
                var existente = await _db.Familias.FirstOrDefaultAsync(x => x.CodigoErp == r.Codigo, ct);
                if (existente is null)
                    _db.Familias.Add(new Familia { CodigoErp = r.Codigo, Descripcion = r.Descripcion, IdSector = idSector, CreatedBy = AutorSync });
                else if (existente.Descripcion != r.Descripcion || existente.IdSector != idSector)
                {
                    existente.Descripcion = r.Descripcion;
                    existente.IdSector = idSector;
                    existente.UpdatedBy = AutorSync;
                }
            }, ct);

        await _db.SaveChangesAsync(ct);
    }

    private static async Task UpsertLookupAsync<T>(IReadOnlyList<T> filas, Func<T, string> codigo, Func<T, Task> upsert, CancellationToken ct)
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
        var familias = await _db.Familias.Where(f => f.CodigoErp != null).ToDictionaryAsync(f => f.CodigoErp!, f => f.IdFamilia, ct);
        var modosIva = await _db.ModosIva.Where(m => m.CodigoErp != null).ToDictionaryAsync(m => m.CodigoErp!, m => m.IdModoIva, ct);
        // Articulo.IdFamilia no es nullable: los artículos que en el ERP no cuelgan de ninguna
        // familia (idFamilia NULL, el "SIN FAMILIA" legado) necesitan igual una fila local válida.
        var idFamiliaSinFamilia = await ObtenerOCrearSinFamiliaAsync(ct);

        var watermark = checkpoint.UltimoWatermarkUtc ?? DateTime.MinValue;
        while (true)
        {
            var lote = await _articulos.GetArticulosModificadosAsync(watermark, _options.LoteSize, ct);
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
                var idFamilia = (fila.CodigoFamiliaErp is not null && familias.TryGetValue(fila.CodigoFamiliaErp, out var idFam))
                    ? idFam : idFamiliaSinFamilia;

                var existente = await _db.Articulos.FirstOrDefaultAsync(a => a.IdErp == fila.IdErp, ct)
                    ?? await _db.Articulos.FirstOrDefaultAsync(a => a.IdErp == null && a.CodigoInterno == fila.Codigo, ct);

                var activo = fila.Estado != 3; // 0/1/2 activo (2 = suspendido, igual se puede vender), 3 = inactivo
                if (existente is null)
                {
                    _db.Articulos.Add(new Articulo
                    {
                        IdErp = fila.IdErp, CodigoInterno = fila.Codigo, Descripcion = fila.DescripcionFull,
                        IdSector = idSector, IdLinea = idLinea, IdFamilia = idFamilia, IdModoIva = idModoIva,
                        Activo = activo, EstadoErp = fila.Estado, UnidadXBulto = fila.UnidadBulto <= 0 ? 1m : fila.UnidadBulto,
                        CreatedBy = AutorSync
                    });
                    checkpoint.Insertados++;
                }
                else
                {
                    existente.IdErp = fila.IdErp;
                    existente.CodigoInterno = fila.Codigo;
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

            watermark = lote.Max(f => f.FechaModificacion);
            checkpoint.UltimoWatermarkUtc = watermark;
            await _db.SaveChangesAsync(ct);

            if (lote.Count < _options.LoteSize) break;
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
        while (true)
        {
            var lote = await _articulos.GetPresentacionesModificadasAsync(watermark, _options.LoteSize, ct);
            if (lote.Count == 0) break;

            foreach (var fila in lote)
            {
                var idArticulo = await _db.Articulos.Where(a => a.IdErp == fila.IdArticuloErp).Select(a => (int?)a.IdArticulo).FirstOrDefaultAsync(ct);
                if (idArticulo is null)
                {
                    _log.LogWarning("Presentación ERP {IdErp}: el artículo ERP {IdArticuloErp} todavía no está sincronizado, se salta (se resuelve en la próxima corrida).",
                        fila.IdErp, fila.IdArticuloErp);
                    checkpoint.Errores++;
                    continue;
                }
                // baja=1 en el ERP: por ahora no se borra ni se oculta (Presentacion no tiene un
                // Activo/Baja local todavía — queda pendiente, ver memoria pos-mayorista-erp-sync
                // punto 3), pero tampoco se crea una presentación nueva si es la primera vez que se ve.
                if (fila.Baja)
                {
                    var yaExiste = await _db.Presentaciones.AnyAsync(p => p.IdErp == fila.IdErp, ct);
                    if (!yaExiste)
                    {
                        _log.LogInformation("Presentación ERP {IdErp} está dada de baja en el ERP y no existe localmente, se ignora.", fila.IdErp);
                        continue;
                    }
                }

                var existente = await _db.Presentaciones.FirstOrDefaultAsync(p => p.IdErp == fila.IdErp, ct);
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

            watermark = lote.Max(f => f.FechaModificacion);
            checkpoint.UltimoWatermarkUtc = watermark;
            await _db.SaveChangesAsync(ct);

            if (lote.Count < _options.LoteSize) break;
        }
    }

    // ----- Códigos de barra (requieren la Presentacion ya sincronizada por IdErp) -----

    private async Task SincronizarCodBarrasAsync(SyncCheckpoint checkpoint, CancellationToken ct)
    {
        var watermark = checkpoint.UltimoWatermarkUtc ?? DateTime.MinValue;
        while (true)
        {
            var lote = await _articulos.GetCodBarrasModificadosAsync(watermark, _options.LoteSize, ct);
            if (lote.Count == 0) break;

            foreach (var fila in lote)
            {
                var idPresentacion = await _db.Presentaciones.Where(p => p.IdErp == fila.IdPresentacionErp).Select(p => (int?)p.IdPresentacion).FirstOrDefaultAsync(ct);
                if (idPresentacion is null)
                {
                    _log.LogWarning("Código de barra {Codigo}: la presentación ERP {IdPresentacionErp} todavía no está sincronizada, se salta.",
                        fila.Codigo, fila.IdPresentacionErp);
                    checkpoint.Errores++;
                    continue;
                }
                var tipo = string.Equals(fila.TipoCodigo, "DUN", StringComparison.OrdinalIgnoreCase) ? TipoBarra.Dun14 : TipoBarra.Ean13;

                // Clave natural: (presentación local, código de barra) — el propio código ya es único.
                var existente = await _db.Barras.FirstOrDefaultAsync(b => b.IdPresentacion == idPresentacion && b.CodigoBarra == fila.Codigo, ct);
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

            watermark = lote.Max(f => f.FechaModificacion);
            checkpoint.UltimoWatermarkUtc = watermark;
            await _db.SaveChangesAsync(ct);

            if (lote.Count < _options.LoteSize) break;
        }
    }

    // ----- Clientes -----

    private async Task SincronizarClientesAsync(SyncCheckpoint checkpoint, CancellationToken ct)
    {
        var condicionesIva = await _db.CondicionesIva.Where(c => c.CodigoErp != null).ToDictionaryAsync(c => c.CodigoErp!, c => c.IdCondIva, ct);

        var watermark = checkpoint.UltimoWatermarkUtc ?? DateTime.MinValue;
        while (true)
        {
            var lote = await _clientes.GetClientesModificadosAsync(watermark, _options.LoteSize, ct);
            if (lote.Count == 0) break;

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

                var existente = await _db.Clientes.FirstOrDefaultAsync(c => c.IdErp == fila.IdErp, ct)
                    ?? await _db.Clientes.FirstOrDefaultAsync(c => c.IdErp == null && c.CodigoInt == fila.Codigo, ct);

                var activo = fila.Estado != 3; // mismo criterio que Articulo.EstadoErp
                if (existente is null)
                {
                    _db.Clientes.Add(new Cliente
                    {
                        IdErp = fila.IdErp, CodigoInt = fila.Codigo, Descripcion = fila.RazonSocial,
                        NombreFantasia = fila.NombreFantasia, Domicilio = fila.Domicilio, Localidad = fila.Localidad,
                        Cuit = fila.Cuit, IdCondIva = idCondIva, Email = fila.Email, Activo = activo,
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
                    existente.Cuit = fila.Cuit;
                    existente.IdCondIva = idCondIva;
                    existente.Email = fila.Email;
                    existente.Activo = activo;
                    existente.EstadoErp = fila.Estado;
                    existente.UpdatedBy = AutorSync;
                    checkpoint.Actualizados++;
                }
            }

            watermark = lote.Max(f => f.FechaActualizacion);
            checkpoint.UltimoWatermarkUtc = watermark;
            await _db.SaveChangesAsync(ct);

            if (lote.Count < _options.LoteSize) break;
        }
    }
}
