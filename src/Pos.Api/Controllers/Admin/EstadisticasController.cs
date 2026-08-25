using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Application.Estadisticas;

namespace Pos.Api.Controllers.Admin;

/// <summary>Módulo Ventas del Admin: dashboard de estadísticas en vivo (el front repolla este
/// endpoint cada tanto, no hay push por WebSocket todavía).</summary>
[ApiController]
[Route("api/v1/admin/estadisticas")]
[Authorize]
[ModuloAutorizado("Ventas", "Administrador")]
public class EstadisticasController : ControllerBase
{
    private readonly IEstadisticasService _service;
    public EstadisticasController(IEstadisticasService service) => _service = service;

    [HttpGet("ventas")]
    public async Task<IActionResult> Ventas([FromQuery] PeriodoEstadisticas periodo, [FromQuery] int? idSucursal,
        CancellationToken ct) =>
        Ok(ApiResult<EstadisticasVentasResponse>.Success(await _service.GetVentasAsync(periodo, idSucursal, ct)));
}
