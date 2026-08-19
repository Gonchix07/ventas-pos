using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Pricing;

namespace Pos.Api.Controllers;

/// <summary>Servicios de precio y ofertas para el módulo de Caja (SRS: Precio, Ofertas Artículo).</summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class PreciosController : ControllerBase
{
    private readonly IPricingService _service;
    public PreciosController(IPricingService service) => _service = service;

    [HttpGet("precios/resolver")]
    public async Task<IActionResult> Resolver([FromQuery] int idSucursal, [FromQuery] int idPresentacion,
        [FromQuery] int? idCliente, CancellationToken ct)
    {
        var r = await _service.ResolverPrecioAsync(new ResolverPrecioRequest(idSucursal, idPresentacion, idCliente), ct);
        return Ok(ApiResult<ResolverPrecioResponse>.Success(r));
    }

    [HttpPost("ofertas/articulos")]
    public async Task<IActionResult> Ofertas([FromBody] AplicarOfertasRequest req, CancellationToken ct)
    {
        var r = await _service.AplicarOfertasAsync(req, ct);
        return Ok(ApiResult<AplicarOfertasResponse>.Success(r));
    }
}
