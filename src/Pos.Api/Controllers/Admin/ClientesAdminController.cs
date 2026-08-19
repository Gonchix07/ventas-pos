using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Clientes;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/clientes")]
[Authorize(Roles = "Administrador")]
public class ClientesAdminController : ControllerBase
{
    private readonly IClienteService _service;
    public ClientesAdminController(IClienteService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? q,
        [FromQuery] bool? admiteCuentaCorriente, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ClienteDto>>.Success(
            await _service.GetAllAsync(q, admiteCuentaCorriente, ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var c = await _service.GetByIdAsync(id, ct);
        return c is null
            ? NotFound(ApiResult<ClienteDto>.Fail("NO_ENCONTRADO", "No existe el cliente."))
            : Ok(ApiResult<ClienteDto>.Success(c));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ClienteInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateAsync(input, ct)));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ClienteInput input, CancellationToken ct)
    {
        var ok = await _service.UpdateAsync(id, input, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el cliente."));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el cliente."));
    }
}
