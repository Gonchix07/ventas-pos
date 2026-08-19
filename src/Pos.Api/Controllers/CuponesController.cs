using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Cupones;

namespace Pos.Api.Controllers;

/// <summary>
/// Cupones de tarjeta (ver CuponesService): listado filtrable por fecha/cajero para rendir contra
/// el resumen del operador, y corrección retroactiva de número de cupón/lote/plan con historial.
/// </summary>
[ApiController]
[Route("api/v1/tesoreria/cupones")]
[Authorize(Roles = "Tesorero,Supervisor,Administrador")]
public class CuponesController : ControllerBase
{
    private readonly ICuponesService _service;
    public CuponesController(ICuponesService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? idSucursal, [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta, [FromQuery] string? cajero, CancellationToken ct)
    {
        var ayer = DateTime.UtcNow.Date.AddDays(-1);
        return Ok(ApiResult<IReadOnlyList<CuponDto>>.Success(
            await _service.GetAsync(idSucursal, desde?.Date ?? ayer, hasta?.Date ?? ayer, cajero, ct)));
    }

    [HttpPut("{idMovPagos:long}")]
    public async Task<IActionResult> Corregir(long idMovPagos, [FromQuery] int idSucursal,
        [FromBody] CorregirCuponInput req, CancellationToken ct) =>
        Ok(ApiResult<CuponDto>.Success(await _service.CorregirAsync(idSucursal, idMovPagos, req, ct)));

    [HttpGet("{idMovPagos:long}/historial")]
    public async Task<IActionResult> Historial(long idMovPagos, [FromQuery] int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<CorreccionCuponDto>>.Success(
            await _service.HistorialAsync(idSucursal, idMovPagos, ct)));

    /// <summary>Planes de cuotas del medio de pago de ese cupón, para elegir al corregir.</summary>
    [HttpGet("{idMovPagos:long}/planes")]
    public async Task<IActionResult> Planes(long idMovPagos, [FromQuery] int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<Pos.Application.Caja.PlanCuotaResumen>>.Success(
            await _service.GetPlanesAsync(idSucursal, idMovPagos, ct)));
}
