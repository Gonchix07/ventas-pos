using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Domain.Common;

namespace Pos.Api.Controllers.Admin;

public record LookupDto(int Id, string Descripcion);
public record LookupInput(string Descripcion);

/// <summary>
/// CRUD genérico para tablas de catálogo simples {Id, Descripcion}.
/// Protegido para rol Administrador (ABM = Administrador, según SRS).
/// </summary>
[ApiController]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public abstract class LookupController<TEntity> : ControllerBase
    where TEntity : class, IEntidadLookup, new()
{
    private readonly ICrudService<TEntity> _crud;
    protected LookupController(ICrudService<TEntity> crud) => _crud = crud;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = (await _crud.GetAllAsync(ct))
            .Select(e => new LookupDto(e.Id, e.Descripcion))
            .OrderBy(x => x.Descripcion)
            .ToList();
        return Ok(ApiResult<IReadOnlyList<LookupDto>>.Success(items));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var e = await _crud.GetByIdAsync(id, ct);
        return e is null
            ? NotFound(ApiResult<LookupDto>.Fail("NO_ENCONTRADO", "No existe el registro."))
            : Ok(ApiResult<LookupDto>.Success(new LookupDto(e.Id, e.Descripcion)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LookupInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Descripcion))
            return BadRequest(ApiResult<LookupDto>.Fail("VALIDACION", "La descripción es obligatoria."));

        var entity = new TEntity { Descripcion = input.Descripcion.Trim() };
        await _crud.AddAsync(entity, ct);
        return Ok(ApiResult<LookupDto>.Success(new LookupDto(entity.Id, entity.Descripcion)));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LookupInput input, CancellationToken ct)
    {
        var e = await _crud.GetByIdAsync(id, ct);
        if (e is null)
            return NotFound(ApiResult<LookupDto>.Fail("NO_ENCONTRADO", "No existe el registro."));
        if (string.IsNullOrWhiteSpace(input.Descripcion))
            return BadRequest(ApiResult<LookupDto>.Fail("VALIDACION", "La descripción es obligatoria."));

        e.Descripcion = input.Descripcion.Trim();
        await _crud.UpdateAsync(e, ct);
        return Ok(ApiResult<LookupDto>.Success(new LookupDto(e.Id, e.Descripcion)));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await _crud.DeleteAsync(id, ct);
        return ok
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el registro."));
    }
}
