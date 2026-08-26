using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Application.Tesoreria;

namespace Pos.Api.Controllers;

/// <summary>Módulo de reportes de tesorería (SRS): dashboard, cierres y validación.</summary>
[ApiController]
[Route("api/v1/tesoreria")]
[Authorize]
[ModuloAutorizado("Tesoreria", "Tesorero,Administrador")]
public class TesoreriaController : ControllerBase
{
    private readonly ITesoreriaService _service;
    public TesoreriaController(ITesoreriaService service) => _service = service;

    /// <summary>
    /// Vista principal: lotes (abiertos y cerrados) cuya apertura cae dentro de [desde, hasta]. Sin
    /// fechas, default a "ayer" en las dos puntas (mismo criterio que la pantalla de Tesorería).
    /// </summary>
    [HttpGet("lotes")]
    public async Task<IActionResult> Lotes([FromQuery] int? idSucursal, [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var ayer = DateTime.UtcNow.Date.AddDays(-1);
        return Ok(ApiResult<IReadOnlyList<LoteResumenDto>>.Success(
            await _service.GetLotesAsync(idSucursal, desde?.Date ?? ayer, hasta?.Date ?? ayer, ct)));
    }

    /// <summary>Detalle de rendición de un lote puntual (subfila al expandir una fila de /lotes).</summary>
    [HttpGet("lotes/{idLote:int}/detalle")]
    public async Task<IActionResult> DetalleLote(int idLote, [FromQuery] int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<LoteDetalleDto>.Success(await _service.GetDetalleLoteAsync(idSucursal, idLote, ct)));

    /// <summary>Comprobantes del lote (popup al hacer click en un valor por medio de pago). Sin
    /// idMedioPago trae todos los comprobantes del lote.</summary>
    [HttpGet("lotes/{idLote:int}/comprobantes")]
    public async Task<IActionResult> ComprobantesLote(int idLote, [FromQuery] int idSucursal,
        [FromQuery] int? idMedioPago, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<Pos.Application.Cierres.ComprobanteLoteDto>>.Success(
            await _service.GetComprobantesLoteAsync(idSucursal, idLote, idMedioPago, ct)));

    [HttpGet("motivos-cierre")]
    public async Task<IActionResult> MotivosCierre(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<MotivoCierreDto>>.Success(await _service.GetMotivosCierreAsync(ct)));

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int? idSucursal, CancellationToken ct) =>
        Ok(ApiResult<DashboardResponse>.Success(await _service.GetDashboardAsync(idSucursal, ct)));

    [HttpGet("cierres")]
    public async Task<IActionResult> Cierres([FromQuery] int? idSucursal, [FromQuery] string? cajero, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<CierreListItemDto>>.Success(await _service.GetCierresAsync(idSucursal, cajero, ct)));

    [HttpGet("motivos-diferencia")]
    public async Task<IActionResult> MotivosDiferencia(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<Pos.Application.Cierres.MotivoDto>>.Success(
            await _service.GetMotivosDiferenciaAsync(ct)));

    /// <summary>
    /// Lotes que quedaron abiertos en días anteriores. Su cajero ya no puede cerrarlos (arqueo X y
    /// cierre Z solo operan sobre el lote de hoy), así que los regulariza Tesorería/Administración.
    /// </summary>
    [HttpGet("lotes-pendientes")]
    public async Task<IActionResult> LotesPendientes([FromQuery] int? idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<LotePendienteDto>>.Success(await _service.GetLotesPendientesAsync(idSucursal, ct)));

    [HttpPost("lotes-pendientes/{idLote:int}/cerrar")]
    public async Task<IActionResult> CerrarLotePendiente(int idLote, [FromQuery] int idSucursal,
        [FromBody] CerrarLotePendienteRequest req, CancellationToken ct) =>
        Ok(ApiResult<Pos.Application.Cierres.CerrarTurnoResponse>.Success(
            await _service.CerrarLotePendienteAsync(idSucursal, idLote, req, ct)));

    /// <summary>Lookup de medios de pago para el popup de "Entrega de valores".</summary>
    [HttpGet("medios-pago")]
    public async Task<IActionResult> MediosPago(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<MedioPagoLookupDto>>.Success(await _service.GetMediosPagoAsync(ct)));

    /// <summary>Corrección manual +/- sobre un lote (cualquier medio, cualquier estado del lote).</summary>
    [HttpPost("lotes/{idLote:int}/correccion")]
    public async Task<IActionResult> Corregir(int idLote, [FromQuery] int idSucursal,
        [FromBody] CorreccionManualInput req, CancellationToken ct) =>
        Ok(ApiResult<Pos.Application.Cierres.CorreccionDto>.Success(
            await _service.CorregirAsync(idSucursal, idLote, req, ct)));

    [HttpPost("cierres/{idLote:int}/validar")]
    public async Task<IActionResult> Validar(int idLote, [FromQuery] int idSucursal,
        [FromBody] ValidarCierreRequest req, CancellationToken ct) =>
        await _service.ValidarCierreAsync(idSucursal, idLote, req, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el cierre para ese lote."));

    /// <summary>Deshace la validación de Tesorería (vuelve el lote a "Pendiente") — ver ReabrirCierreAsync.</summary>
    [HttpPost("cierres/{idLote:int}/reabrir")]
    public async Task<IActionResult> Reabrir(int idLote, [FromQuery] int idSucursal, CancellationToken ct) =>
        await _service.ReabrirCierreAsync(idSucursal, idLote, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "El lote no existe o no está validado."));
}
