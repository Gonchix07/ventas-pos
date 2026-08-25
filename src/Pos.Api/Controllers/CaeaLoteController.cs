using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Facturacion;

namespace Pos.Api.Controllers;

/// <summary>Módulo "Facturación CAEA": comprobantes emitidos en contingencia (CAEA) todavía sin
/// informar a ARCA, y la acción de subir el lote. Mismo nivel de acceso que Tesorería — es una
/// obligación fiscal de back-office, no una operación de mostrador.</summary>
[ApiController]
[Route("api/v1/caea-lote")]
[Authorize(Roles = "Tesorero,Administrador")]
public class CaeaLoteController : ControllerBase
{
    private readonly ICaeaLoteService _service;

    public CaeaLoteController(ICaeaLoteService service) => _service = service;

    [HttpGet("pendientes")]
    public async Task<IActionResult> ListarPendientes(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<LoteCaeaPendienteDto>>.Success(await _service.ListarPendientesAsync(ct)));

    [HttpGet("pendientes/comprobantes")]
    public async Task<IActionResult> ListarComprobantes([FromQuery] int idSucursal, [FromQuery] int idPuntoVenta,
        [FromQuery] int idTipoComprobante, [FromQuery] string caea, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ComprobanteCaeaDto>>.Success(
            await _service.ListarComprobantesAsync(idSucursal, idPuntoVenta, idTipoComprobante, caea, ct)));

    [HttpPost("informar")]
    public async Task<IActionResult> Informar([FromBody] InformarLoteCaeaRequest req, CancellationToken ct) =>
        Ok(ApiResult<InformarLoteCaeaResponse>.Success(await _service.InformarLoteAsync(req, ct)));
}
