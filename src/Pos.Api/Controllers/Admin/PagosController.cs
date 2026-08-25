using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class PagosController : ControllerBase
{
    private readonly IPagoAdminService _service;
    public PagosController(IPagoAdminService service) => _service = service;

    // ---- Tipos de pago ----
    [HttpGet("tipos-pago")]
    public async Task<IActionResult> GetTipos(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<TipoPagoDto>>.Success(await _service.GetTiposAsync(ct)));

    [HttpPost("tipos-pago")]
    public async Task<IActionResult> CreateTipo([FromBody] TipoPagoInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateTipoAsync(input, ct)));

    [HttpPut("tipos-pago/{id:int}")]
    public async Task<IActionResult> UpdateTipo(int id, [FromBody] TipoPagoInput input, CancellationToken ct) =>
        await _service.UpdateTipoAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el tipo de pago."));

    [HttpDelete("tipos-pago/{id:int}")]
    public async Task<IActionResult> DeleteTipo(int id, CancellationToken ct) =>
        await _service.DeleteTipoAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el tipo de pago."));

    // ---- Medios de pago ----
    [HttpGet("medios-pago")]
    public async Task<IActionResult> GetMedios(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<MedioPagoDto>>.Success(await _service.GetMediosAsync(ct)));

    [HttpPost("medios-pago")]
    public async Task<IActionResult> CreateMedio([FromBody] MedioPagoInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateMedioAsync(input, ct)));

    [HttpPut("medios-pago/{id:int}")]
    public async Task<IActionResult> UpdateMedio(int id, [FromBody] MedioPagoInput input, CancellationToken ct) =>
        await _service.UpdateMedioAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el medio de pago."));

    [HttpDelete("medios-pago/{id:int}")]
    public async Task<IActionResult> DeleteMedio(int id, CancellationToken ct) =>
        await _service.DeleteMedioAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el medio de pago."));

    // ---- Planes de cuotas (solo medios de tipo Tarjeta) ----
    [HttpGet("medios-pago/{idMedioPago:int}/planes")]
    public async Task<IActionResult> GetPlanes(int idMedioPago, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<PlanCuotaDto>>.Success(await _service.GetPlanesAsync(idMedioPago, ct)));

    [HttpPost("medios-pago/{idMedioPago:int}/planes")]
    public async Task<IActionResult> CreatePlan(int idMedioPago, [FromBody] PlanCuotaInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreatePlanAsync(idMedioPago, input, ct)));

    [HttpPut("planes/{id:int}")]
    public async Task<IActionResult> UpdatePlan(int id, [FromBody] PlanCuotaInput input, CancellationToken ct) =>
        await _service.UpdatePlanAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el plan."));

    [HttpDelete("planes/{id:int}")]
    public async Task<IActionResult> DeletePlan(int id, CancellationToken ct) =>
        await _service.DeletePlanAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el plan."));
}
