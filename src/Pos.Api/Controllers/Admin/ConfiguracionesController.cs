using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/configuraciones")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class ConfiguracionesController : ControllerBase
{
    private readonly IConfiguracionAdminService _service;
    public ConfiguracionesController(IConfiguracionAdminService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ConfiguracionDto>>.Success(await _service.GetAllAsync(ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ConfiguracionInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateAsync(input, ct)));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ConfiguracionInput input, CancellationToken ct) =>
        await _service.UpdateAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la configuración."));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await _service.DeleteAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la configuración."));
}
