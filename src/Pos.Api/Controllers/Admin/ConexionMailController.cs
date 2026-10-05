using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

/// <summary>Cuenta de Brevo (singleton) para enviar mails — ver ConexionMail.</summary>
[ApiController]
[Route("api/v1/admin/conexion-mail")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class ConexionMailController : ControllerBase
{
    private readonly IConexionMailAdminService _service;
    public ConexionMailController(IConexionMailAdminService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ApiResult<ConexionMailDto>.Success(await _service.GetAsync(ct)));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ConexionMailInput input, CancellationToken ct)
    {
        await _service.UpdateAsync(input, ct);
        return Ok(ApiResult<bool>.Success(true));
    }

    [HttpPost("probar")]
    public async Task<IActionResult> Probar([FromBody] ConexionMailInput input, CancellationToken ct) =>
        Ok(ApiResult<ProbarConexionResultado>.Success(await _service.ProbarAsync(input, ct)));
}
