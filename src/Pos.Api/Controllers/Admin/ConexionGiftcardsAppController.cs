using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

/// <summary>Configuración (singleton) de la conexión al API de giftcards-app — ver ConexionGiftcardsApp.</summary>
[ApiController]
[Route("api/v1/admin/conexion-giftcards-app")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class ConexionGiftcardsAppController : ControllerBase
{
    private readonly IConexionGiftcardsAppAdminService _service;
    public ConexionGiftcardsAppController(IConexionGiftcardsAppAdminService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ApiResult<ConexionGiftcardsAppDto>.Success(await _service.GetAsync(ct)));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ConexionGiftcardsAppInput input, CancellationToken ct)
    {
        await _service.UpdateAsync(input, ct);
        return Ok(ApiResult<bool>.Success(true));
    }

    [HttpPost("probar")]
    public async Task<IActionResult> Probar([FromBody] ConexionGiftcardsAppInput input, CancellationToken ct) =>
        Ok(ApiResult<ProbarConexionResultado>.Success(await _service.ProbarConexionAsync(input, ct)));
}
