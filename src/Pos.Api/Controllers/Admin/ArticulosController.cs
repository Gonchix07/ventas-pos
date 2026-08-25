using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Catalogo;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/articulos")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class ArticulosController : ControllerBase
{
    private readonly IArticuloService _service;
    public ArticulosController(IArticuloService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? texto, [FromQuery] int? idSector,
        [FromQuery] int? idLinea, [FromQuery] int? idFamilia, [FromQuery] bool? activo,
        [FromQuery] int? max, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ArticuloListItem>>.Success(
            await _service.GetAllAsync(new ArticuloFiltro(texto, idSector, idLinea, idFamilia, activo, max), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var a = await _service.GetByIdAsync(id, ct);
        return a is null
            ? NotFound(ApiResult<ArticuloDetail>.Fail("NO_ENCONTRADO", "No existe el artículo."))
            : Ok(ApiResult<ArticuloDetail>.Success(a));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ArticuloInput input, CancellationToken ct)
    {
        var id = await _service.CreateAsync(input, ct);
        return Ok(ApiResult<int>.Success(id));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ArticuloInput input, CancellationToken ct)
    {
        var ok = await _service.UpdateAsync(id, input, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el artículo."));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el artículo."));
    }
}
