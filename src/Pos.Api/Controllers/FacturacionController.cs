using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Facturacion;

namespace Pos.Api.Controllers;

/// <summary>Emisión y consulta de comprobantes (módulo de Facturación del SRS).</summary>
[ApiController]
[Route("api/v1/facturacion")]
[Authorize(Roles = "Cajero,Supervisor,Administrador")]
public class FacturacionController : ControllerBase
{
    private readonly IFacturacionService _service;
    public FacturacionController(IFacturacionService service) => _service = service;

    [HttpPost("emitir")]
    public async Task<IActionResult> Emitir([FromBody] EmitirComprobanteRequest req, CancellationToken ct) =>
        Ok(ApiResult<EmitirComprobanteResponse>.Success(await _service.EmitirAsync(req, ct)));

    [HttpGet("{idComprobante:int}")]
    public async Task<IActionResult> Obtener(int idComprobante, [FromQuery] int idSucursal, CancellationToken ct)
    {
        var c = await _service.ObtenerAsync(idSucursal, idComprobante, ct);
        return c is null
            ? NotFound(ApiResult<ComprobanteDetailDto>.Fail("NO_ENCONTRADO", "El comprobante no existe."))
            : Ok(ApiResult<ComprobanteDetailDto>.Success(c));
    }

    /// <summary>Comprobante armado para imprimir (formato A o B según la letra emitida).</summary>
    [HttpGet("{idComprobante:int}/impresion")]
    public async Task<IActionResult> Impresion(int idComprobante, [FromQuery] int idSucursal, CancellationToken ct)
    {
        var c = await _service.ObtenerParaImprimirAsync(idSucursal, idComprobante, ct);
        return c is null
            ? NotFound(ApiResult<ComprobanteImpresionDto>.Fail("NO_ENCONTRADO", "El comprobante no existe."))
            : Ok(ApiResult<ComprobanteImpresionDto>.Success(c));
    }

    /// <summary>Letra que le corresponde a la operación, para anticiparla en la pantalla de cobro.</summary>
    [HttpGet("letra")]
    public async Task<IActionResult> Letra([FromQuery] int idSucursal, [FromQuery] int idOperacion, CancellationToken ct) =>
        Ok(ApiResult<string>.Success(await _service.ResolverLetraAsync(idSucursal, idOperacion, ct)));

    [HttpPost("{idComprobante:int}/reimprimir")]
    public async Task<IActionResult> Reimprimir(int idComprobante, [FromQuery] int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<ReimpresionResponse>.Success(await _service.ReimprimirAsync(idSucursal, idComprobante, ct)));
}
