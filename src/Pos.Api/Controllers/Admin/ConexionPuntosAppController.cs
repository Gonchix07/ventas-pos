using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

/// <summary>Configuración (singleton) de la conexión al API de puntos-app — ver ConexionPuntosApp.</summary>
[ApiController]
[Route("api/v1/admin/conexion-puntos-app")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class ConexionPuntosAppController : ControllerBase
{
    private readonly IConexionPuntosAppAdminService _service;
    public ConexionPuntosAppController(IConexionPuntosAppAdminService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ApiResult<ConexionPuntosAppDto>.Success(await _service.GetAsync(ct)));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ConexionPuntosAppInput input, CancellationToken ct)
    {
        await _service.UpdateAsync(input, ct);
        return Ok(ApiResult<bool>.Success(true));
    }

    /// <summary>Prueba real contra puntos-app con los datos del formulario — nunca expone el token,
    /// solo si funcionó o el motivo del error.</summary>
    [HttpPost("probar")]
    public async Task<IActionResult> Probar([FromBody] ConexionPuntosAppInput input, CancellationToken ct) =>
        Ok(ApiResult<ProbarConexionResultado>.Success(await _service.ProbarConexionAsync(input, ct)));
}
