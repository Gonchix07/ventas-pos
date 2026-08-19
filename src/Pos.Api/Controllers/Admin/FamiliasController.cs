using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Catalogo;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

/// <summary>
/// ABM de familias. Mismo contrato que los lookups simples más el sector: el GET acepta
/// <c>?idSector=</c> para traer solo las familias de un sector (que es lo que consumen los combos
/// dependientes de Artículos, Etiquetas y Ofertas).
/// </summary>
[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/v1/admin/familias")]
public class FamiliasController : ControllerBase
{
    private readonly IFamiliaService _familias;

    public FamiliasController(IFamiliaService familias) => _familias = familias;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? idSector, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<FamiliaDto>>.Success(await _familias.GetAllAsync(idSector, ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var f = await _familias.GetByIdAsync(id, ct);
        return f is null
            ? NotFound(ApiResult<FamiliaDto>.Fail("NO_ENCONTRADO", "No existe la familia."))
            : Ok(ApiResult<FamiliaDto>.Success(f));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FamiliaInput input, CancellationToken ct) =>
        Ok(ApiResult<FamiliaDto>.Success(await _familias.CreateAsync(input, ct)));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FamiliaInput input, CancellationToken ct)
    {
        var f = await _familias.UpdateAsync(id, input, ct);
        return f is null
            ? NotFound(ApiResult<FamiliaDto>.Fail("NO_ENCONTRADO", "No existe la familia."))
            : Ok(ApiResult<FamiliaDto>.Success(f));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await _familias.DeleteAsync(id, ct);
        return ok
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la familia."));
    }
}
