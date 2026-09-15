using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Application.Sistema;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/version")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class VersionController : ControllerBase
{
    private readonly ISistemaVersionService _service;
    public VersionController(ISistemaVersionService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ApiResult<VersionInfoDto>.Success(await _service.ObtenerAsync(ct)));
}
