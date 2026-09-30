using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Clientes;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/clientes")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class ClientesAdminController : ControllerBase
{
    private readonly IClienteService _service;
    private readonly IClienteDbfImportService _importDbf;

    public ClientesAdminController(IClienteService service, IClienteDbfImportService importDbf)
    {
        _service = service;
        _importDbf = importDbf;
    }

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

    // ---- Comparador de diferencias / import desde clientes.dbf + codtarje.dbf (app legacy VFP) ----

    /// <summary>Clientes que están en clientes.dbf pero todavía no en SQL (por CodigoInt). Solo
    /// lectura, no crea nada — es la vista previa antes de "Importar".</summary>
    [HttpGet("nuevos-dbf")]
    public async Task<IActionResult> GetNuevosDbf(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ClienteNuevoDto>>.Success(await _importDbf.ObtenerNuevosAsync(ct)));

    /// <summary>Crea los clientes nuevos de clientes.dbf (nunca toca los que ya existen).</summary>
    [HttpPost("nuevos-dbf/importar")]
    public async Task<IActionResult> ImportarNuevosDbf(CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _importDbf.ImportarNuevosAsync(ct)));
}
