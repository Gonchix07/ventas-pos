using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

/// <summary>Definición de permisos por rol: qué módulos del menú principal ve cada rol.</summary>
[ApiController]
[Route("api/v1/admin/permisos")]
[Authorize(Roles = "Administrador")]
public class PermisosController : ControllerBase
{
    private readonly IPermisoAdminService _service;
    public PermisosController(IPermisoAdminService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetMatriz(CancellationToken ct) =>
        Ok(ApiResult<MatrizPermisosDto>.Success(await _service.GetMatrizAsync(ct)));

    [HttpPut]
    public async Task<IActionResult> Actualizar([FromBody] ActualizarPermisoInput input, CancellationToken ct)
    {
        await _service.ActualizarAsync(input, ct);
        return Ok(ApiResult<bool>.Success(true));
    }
}
