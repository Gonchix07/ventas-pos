using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

/// <summary>Configuración (singleton) de la conexión a la base MySQL externa — ver ConexionExternaMySql.</summary>
[ApiController]
[Route("api/v1/admin/conexion-externa")]
[Authorize(Roles = "Administrador")]
public class ConexionExternaController : ControllerBase
{
    private readonly IConexionExternaAdminService _service;
    public ConexionExternaController(IConexionExternaAdminService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ApiResult<ConexionExternaMySqlDto>.Success(await _service.GetAsync(ct)));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ConexionExternaMySqlInput input, CancellationToken ct)
    {
        await _service.UpdateAsync(input, ct);
        return Ok(ApiResult<bool>.Success(true));
    }

    /// <summary>Prueba real (TCP + login) contra MySQL con los datos del formulario — nunca expone
    /// la contraseña, solo si funcionó o el motivo del error.</summary>
    [HttpPost("probar")]
    public async Task<IActionResult> Probar([FromBody] ConexionExternaMySqlInput input, CancellationToken ct) =>
        Ok(ApiResult<ProbarConexionResultado>.Success(await _service.ProbarConexionAsync(input, ct)));
}
