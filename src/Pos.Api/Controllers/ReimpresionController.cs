using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Application.Facturacion;

namespace Pos.Api.Controllers;

/// <summary>
/// Reimpresión de comprobantes ya emitidos: buscar por número, cliente o CUIT (misma UX que Nota de
/// Crédito) y volver a mostrarlo en pantalla para imprimir. No reemite ni reabre nada fiscal — reusa
/// el mismo armado que ya se usa para la vista posterior a emitir
/// (IFacturacionService.ObtenerParaImprimirAsync). Reimprimir el papel fiscal original en la propia
/// controladora (comando CopiarComprobante del protocolo Hasar) queda pendiente aparte.
/// </summary>
[ApiController]
[Route("api/v1/reimpresion")]
[Authorize]
[ModuloAutorizado("Reimpresion", "Supervisor,Tesorero,Administrador")]
public class ReimpresionController : ControllerBase
{
    private readonly IReimpresionService _service;
    private readonly IFacturacionService _facturacion;

    public ReimpresionController(IReimpresionService service, IFacturacionService facturacion)
    {
        _service = service;
        _facturacion = facturacion;
    }

    /// <summary>Busca comprobantes (facturas y notas de crédito) por número, cliente o CUIT.</summary>
    [HttpGet("comprobantes")]
    public async Task<IActionResult> Buscar([FromQuery] int idSucursal, [FromQuery] string? texto,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ComprobanteReimpresionDto>>.Success(
            await _service.BuscarAsync(idSucursal, texto ?? "", desde, hasta, ct)));

    /// <summary>Comprobante armado para imprimir (formato A o B según la letra emitida) — mismo DTO
    /// que la vista inmediata post-emisión.</summary>
    [HttpGet("{idComprobante:int}/impresion")]
    public async Task<IActionResult> Impresion(int idComprobante, [FromQuery] int idSucursal, CancellationToken ct)
    {
        var c = await _facturacion.ObtenerParaImprimirAsync(idSucursal, idComprobante, ct);
        return c is null
            ? NotFound(ApiResult<ComprobanteImpresionDto>.Fail("NO_ENCONTRADO", "El comprobante no existe."))
            : Ok(ApiResult<ComprobanteImpresionDto>.Success(c));
    }
}
