using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Tesoreria;

namespace Pos.Api.Controllers;

/// <summary>Módulo de reportes de tesorería (SRS): dashboard, cierres y validación.</summary>
[ApiController]
[Route("api/v1/tesoreria")]
[Authorize(Roles = "Tesorero,Administrador")]
public class TesoreriaController : ControllerBase
{
    private readonly ITesoreriaService _service;
    public TesoreriaController(ITesoreriaService service) => _service = service;

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

    [HttpPost("cierres/{idLote:int}/validar")]
    public async Task<IActionResult> Validar(int idLote, [FromQuery] int idSucursal,
        [FromBody] ValidarCierreRequest req, CancellationToken ct) =>
        await _service.ValidarCierreAsync(idSucursal, idLote, req, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el cierre para ese lote."));
}
