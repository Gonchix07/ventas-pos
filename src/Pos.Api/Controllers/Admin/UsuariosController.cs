using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Administrador")]
public class UsuariosController : ControllerBase
{
    private readonly IUsuarioAdminService _service;
    public UsuariosController(IUsuarioAdminService service) => _service = service;

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<RolDto>>.Success(await _service.GetRolesAsync(ct)));

    [HttpGet("usuarios")]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<UsuarioDto>>.Success(await _service.GetAllAsync(ct)));

    [HttpPost("usuarios")]
    public async Task<IActionResult> Create([FromBody] UsuarioCreateInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateAsync(input, ct)));

    [HttpPut("usuarios/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UsuarioUpdateInput input, CancellationToken ct) =>
        await _service.UpdateAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el usuario."));

    [HttpPost("usuarios/{id:int}/reset-clave")]
    public async Task<IActionResult> ResetClave(int id, [FromBody] ResetClaveInput input, CancellationToken ct) =>
        await _service.ResetClaveAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el usuario."));

    [HttpDelete("usuarios/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await _service.DeleteAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el usuario."));
}
