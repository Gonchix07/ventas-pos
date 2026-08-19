using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Caja;
using Pos.Application.Common;

namespace Pos.Api.Controllers;

/// <summary>Retiro de efectivo del turno del cajero (ver RetiroCajaService).</summary>
[ApiController]
[Route("api/v1/caja")]
[Authorize(Roles = "Cajero,Supervisor,Administrador")]
public class RetiroCajaController : ControllerBase
{
    private readonly IRetiroCajaService _service;
    public RetiroCajaController(IRetiroCajaService service) => _service = service;

    [HttpPost("retiro-efectivo")]
    public async Task<IActionResult> RetiroEfectivo([FromQuery] int idSucursal, [FromQuery] int idCaja,
        [FromBody] RetiroEfectivoRequest req, CancellationToken ct) =>
        Ok(ApiResult<RetiroEfectivoResponse>.Success(await _service.RegistrarAsync(idSucursal, idCaja, req, ct)));
}
