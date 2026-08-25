using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Application.Etiquetas;

namespace Pos.Api.Controllers;

/// <summary>Módulo de etiquetas (SRS): búsqueda/escaneo, selección por clasificación, impresión.</summary>
[ApiController]
[Route("api/v1/etiquetas")]
[Authorize]
[ModuloAutorizado("Etiquetas", "Repositor,Tesorero,Cajero,Administrador")]
public class EtiquetasController : ControllerBase
{
    private readonly IEtiquetaService _service;
    public EtiquetasController(IEtiquetaService service) => _service = service;

    [HttpGet("clasificaciones")]
    public async Task<IActionResult> Clasificaciones(CancellationToken ct) =>
        Ok(ApiResult<ClasificacionesDto>.Success(await _service.GetClasificacionesAsync(ct)));

    [HttpGet("sucursales")]
    public async Task<IActionResult> Sucursales(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<LookupSimpleDto>>.Success(await _service.GetSucursalesAsync(ct)));

    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string q, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ArticuloParaEtiquetaDto>>.Success(await _service.BuscarAsync(q ?? "", ct)));

    [HttpGet("por-clasificacion")]
    public async Task<IActionResult> PorClasificacion(
        [FromQuery] int? idSector, [FromQuery] int? idLinea, [FromQuery] int? idFamilia, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ArticuloParaEtiquetaDto>>.Success(
            await _service.PorClasificacionAsync(idSector, idLinea, idFamilia, ct)));

    public record GenerarRequest(int IdSucursal, List<int> IdsPresentacion);

    [HttpPost("generar")]
    public async Task<IActionResult> Generar([FromBody] GenerarRequest req, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<EtiquetaDto>>.Success(await _service.GenerarAsync(req.IdSucursal, req.IdsPresentacion, ct)));
}
