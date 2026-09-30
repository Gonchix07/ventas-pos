using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Application.PreventaMayorista;

namespace Pos.Api.Controllers.Admin;

/// <summary>
/// CRUD de administración de los tramos de recargo logístico (ver <see cref="IRecargoLogisticaService"/>),
/// dentro de Precios y Ofertas. El módulo Preventa Mayorista solo los consulta/reimporta desde DBF
/// (ver PreventaMayoristaController); acá se permite además altas/ediciones/bajas individuales.
/// </summary>
[ApiController]
[Route("api/v1/admin/recargo-logistica")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class RecargoLogisticaAdminController : ControllerBase
{
    private readonly IRecargoLogisticaService _service;
    public RecargoLogisticaAdminController(IRecargoLogisticaService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<RecargoLogisticaDto>>.Success(await _service.ObtenerAsync(ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RecargoLogisticaInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateAsync(input, ct)));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RecargoLogisticaInput input, CancellationToken ct)
    {
        var ok = await _service.UpdateAsync(id, input, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el tramo."));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el tramo."));
    }

    /// <summary>Relee recargo_logistica.dbf y reemplaza el contenido de la tabla SQL.</summary>
    [HttpPost("importar")]
    public async Task<IActionResult> Importar(CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.ImportarAsync(ct)));
}
