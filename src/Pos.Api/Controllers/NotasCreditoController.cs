using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Facturacion;

namespace Pos.Api.Controllers;

[ApiController]
[Route("api/v1/notas-credito")]
[Authorize(Roles = "Cajero,Supervisor,Administrador")]
public class NotasCreditoController : ControllerBase
{
    private readonly INotaCreditoService _service;

    public NotasCreditoController(INotaCreditoService service) => _service = service;

    /// <summary>Busca facturas anulables por número, cliente o CUIT, en toda la sucursal.</summary>
    [HttpGet("comprobantes")]
    public async Task<IActionResult> Buscar([FromQuery] int idSucursal, [FromQuery] string? texto,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ComprobanteAnulableDto>>.Success(
            await _service.BuscarAsync(idSucursal, texto ?? "", desde, hasta, ct)));

    /// <summary>Trae la factura con sus líneas y cuáles ya fueron acreditadas.</summary>
    [HttpGet("comprobantes/{idComprobante:int}")]
    public async Task<IActionResult> Obtener(int idComprobante, [FromQuery] int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<ComprobanteAnulableDetalleDto>.Success(
            await _service.ObtenerAsync(idSucursal, idComprobante, ct)));

    [HttpPost("emitir")]
    public async Task<IActionResult> Emitir([FromBody] EmitirNotaCreditoRequest req, CancellationToken ct) =>
        Ok(ApiResult<NotaCreditoResponse>.Success(await _service.EmitirAsync(req, ct)));
}
