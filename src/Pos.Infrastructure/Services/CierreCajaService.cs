using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Abstractions.Fiscal;
using Pos.Application.Cierres;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Arqueo X (vista, no persiste) y cierre de TURNO (irreversible) del lote de caja del cajero. Ver
/// docs/06-flujos-caja.md §4 y §3. El Cierre Z del controlador fiscal es una operación aparte, ver
/// <see cref="Pos.Application.Cierres.ICierreZFiscalService"/>.
/// </summary>
public class CierreCajaService : ICierreCajaService
{
    private static readonly TimeSpan TimeoutFiscal = TimeSpan.FromSeconds(8);

    private readonly PosDbContext _db;
    private readonly IFiscalPrinter _impresora;
    private readonly ICurrentUser _currentUser;
    private readonly CierreLoteEjecutor _ejecutor;

    public CierreCajaService(PosDbContext db, IFiscalPrinter impresora, ICurrentUser currentUser,
        CierreLoteEjecutor ejecutor)
    {
        _db = db;
        _impresora = impresora;
        _currentUser = currentUser;
        _ejecutor = ejecutor;
    }

    public async Task<ArqueoXResponse> ArqueoXAsync(int idSucursal, int idCaja, bool imprimir = true, CancellationToken ct = default)
    {
        // paraApertura: false — el arqueo exige un lote propio en esa caja (que puede ser el turno
        // retomado desde otra PC, ver CajaAccesoHelper).
        await CajaAccesoHelper.AsegurarCajaOperableAsync(_db, _currentUser, idSucursal, idCaja, false, ct);

        var lote = await ObtenerLoteAbiertoAsync(idSucursal, idCaja, _currentUser.IdUsuario ?? 0, ct)
            ?? throw new DomainException("SIN_LOTE_ABIERTO", "No hay un lote abierto para esta caja.");

        var acumulados = await _ejecutor.AcumularAsync(idSucursal, lote.IdLote, ct);
        var anulaciones = await _ejecutor.AnulacionesAsync(idSucursal, lote.IdLote, ct);
        var retiros = await _ejecutor.RetirosAsync(idSucursal, lote.IdLote, ct);
        var vueltos = await _ejecutor.VueltosAsync(idSucursal, lote.IdLote, ct);
        var ingresoInicial = await _ejecutor.IngresoInicialAsync(idSucursal, lote.IdLote, ct);
        // Best-effort: el arqueo X es una vista, no persiste nada — un fallo/timeout de la
        // impresora fiscal no debe impedir mostrar los acumulados. Si imprimir=false (ej. el
        // preview que arma la pantalla de "Cerrar turno") no se dispara la impresión física: ese
        // flujo ya emite su propia rendición al confirmar, un reporte X del controlador ahí es un
        // papel de más que nadie pide.
        var impresion = imprimir
            ? await ImprimirBestEffortAsync(ct2 => _impresora.ArqueoXAsync(idSucursal, idCaja, ct2), ct)
            : new ResultadoImpresion(false, null, null);

        var descripcionCaja = await _db.Cajas.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdCaja == idCaja)
            .Select(c => c.Descripcion).FirstOrDefaultAsync(ct) ?? "";

        // Para avisarle al cajero que conviene hacer un retiro: cuánto Efectivo hay acumulado
        // (ya está adentro de acumulados/TotalGeneral, esto es solo para identificarlo aparte) vs.
        // el tope configurado. LimiteEfectivoCaja=0 (sin cargar) significa "sin límite".
        var idsMediosEfectivo = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
            .Where(m => m.TipoPago!.Fuente == FuentePago.Efectivo)
            .Select(m => m.IdMedioPago).ToListAsync(ct);
        var efectivoAcumulado = acumulados.Where(a => idsMediosEfectivo.Contains(a.IdMedioPago)).Sum(a => a.Total);
        var limiteEfectivoCaja = await ObtenerConfigDecimalAsync("LimiteEfectivoCaja", 0m, ct);

        return new ArqueoXResponse(idSucursal, lote.IdLote, idCaja, descripcionCaja, lote.FechaApertura,
            acumulados, acumulados.Sum(a => a.Total), impresion.Ok ? impresion.Referencia : null,
            anulaciones, anulaciones.Sum(a => a.Total), retiros, retiros.Sum(r => r.Monto),
            vueltos, vueltos.Sum(v => v.Monto), ingresoInicial, efectivoAcumulado, limiteEfectivoCaja);
    }

    /// <summary>Lee un valor numérico de la tabla Configuracion (clave/valor); si no está cargada o
    /// no es un número válido, devuelve <paramref name="porDefecto"/> en vez de fallar.</summary>
    private async Task<decimal> ObtenerConfigDecimalAsync(string clave, decimal porDefecto, CancellationToken ct)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == clave).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return valor is not null
            && decimal.TryParse(valor, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n : porDefecto;
    }

    // Cierre del TURNO del cajero: rendición de lo vendido/cobrado en su lote, irreversible en el
    // negocio. Deliberadamente NO toca el controlador fiscal — eso es un reporte de la caja física
    // (puede haber otros cajeros con turno abierto ahí mismo) y es una operación aparte, ver
    // ICierreZFiscalService/CierreZFiscalService.
    public async Task<CerrarTurnoResponse> CerrarTurnoAsync(int idSucursal, int idCaja, CerrarTurnoRequest req, CancellationToken ct = default)
    {
        await CajaAccesoHelper.AsegurarCajaOperableAsync(_db, _currentUser, idSucursal, idCaja, false, ct);

        if (req.Declaraciones.Any(d => d.MontoDeclarado < 0))
            throw new DomainException("MONTO_INVALIDO", "El monto declarado no puede ser negativo.");

        var lote = await ObtenerLoteAbiertoAsync(idSucursal, idCaja, _currentUser.IdUsuario ?? 0, ct)
            ?? throw new DomainException("SIN_LOTE_ABIERTO", "No hay un lote abierto para esta caja.");

        if (!CierreLoteReglas.PuedeCerrarse(lote.Estado))
            throw new DomainException("LOTE_YA_CERRADO", "El lote ya fue cerrado (el cierre de turno es irreversible).");

        var acumulados = await _ejecutor.AcumularAsync(idSucursal, lote.IdLote, ct);
        var anulaciones = await _ejecutor.AnulacionesAsync(idSucursal, lote.IdLote, ct);
        var detalle = await _ejecutor.ArmarDetalleAsync(acumulados, req.Declaraciones, ct);

        if (detalle.Any(d => d.RequiereMotivo) && req.IdMotivoDiferencia is null)
            throw new DomainException("MOTIVO_REQUERIDO",
                "Hay diferencias entre lo declarado y lo esperado: debe indicar un motivo.");

        var cierre = await _ejecutor.CerrarAsync(idSucursal, lote.IdLote, detalle, acumulados,
            new CierreLoteJustificacion(req.IdMotivoDiferencia, req.ObservacionesCajero,
                IdMotivoCierre: null, ObservacionTesoreria: null), ct);

        return new CerrarTurnoResponse(idSucursal, lote.IdLote, cierre.NumeroCierre, cierre.FechaCierre,
            detalle, detalle.Sum(d => d.Diferencia), anulaciones, anulaciones.Sum(a => a.Total));
    }

    private static async Task<ResultadoImpresion> ImprimirBestEffortAsync(
        Func<CancellationToken, Task<ResultadoImpresion>> impresora, CancellationToken ct)
    {
        try
        {
            return await ResilientCall.ConTimeoutAsync(impresora, TimeoutFiscal, ct);
        }
        catch (Exception ex)
        {
            return new ResultadoImpresion(false, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<MotivoDto>> GetMotivosDiferenciaAsync(CancellationToken ct = default) =>
        await _db.MotivosDiferencia.AsNoTracking().OrderBy(m => m.Descripcion)
            .Select(m => new MotivoDto(m.IdMotivoDiferencia, m.Descripcion)).ToListAsync(ct);

    private async Task<LoteCaja?> ObtenerLoteAbiertoAsync(int idSucursal, int idCaja, int idUsuario, CancellationToken ct)
    {
        // El lote a operar es el de HOY (SRS: un lote por día) Y del cajero logueado — varios
        // cajeros pueden compartir la misma caja física con lotes propios simultáneos, así que
        // "la caja" sola no identifica un único lote. Un lote abierto de un día anterior que haya
        // quedado sin cerrar no debe confundirse con el actual — evita cerrar por error un lote
        // viejo cuando se abre uno nuevo el día siguiente.
        return await _db.LotesCaja
            .Where(l => l.IdSucursal == idSucursal && l.IdCaja == idCaja && l.IdUsuarioApertura == idUsuario
                     && l.Estado == EstadoLote.Abierto && l.FechaApertura.Date == DateTime.UtcNow.Date)
            .FirstOrDefaultAsync(ct);
    }

}
