using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Common;
using Pos.Application.Precios;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/listas-precios")]
[Authorize(Roles = "Administrador")]
public class ListasPreciosController : ControllerBase
{
    private readonly IListaPrecioService _service;
    public ListasPreciosController(IListaPrecioService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ListaPrecioDto>>.Success(await _service.GetAllAsync(ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var l = await _service.GetByIdAsync(id, ct);
        return l is null
            ? NotFound(ApiResult<ListaPrecioDto>.Fail("NO_ENCONTRADO", "No existe la lista."))
            : Ok(ApiResult<ListaPrecioDto>.Success(l));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ListaPrecioInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateAsync(input, ct)));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ListaPrecioInput input, CancellationToken ct)
    {
        var ok = await _service.UpdateAsync(id, input, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la lista."));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la lista."));
    }

    // ---- Precios dentro de la lista ----

    [HttpGet("{id:int}/precios")]
    public async Task<IActionResult> GetPrecios(int id, [FromQuery] string? texto,
        [FromQuery] string? idsArticulos, CancellationToken ct)
    {
        // idsArticulos viene como CSV para no complicar la URL del buscador ("12,34,56").
        var ids = idsArticulos?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();
        return Ok(ApiResult<IReadOnlyList<PrecioDto>>.Success(
            await _service.GetPreciosAsync(id, texto, ids, ct)));
    }

    [HttpPut("{id:int}/precios/{idPresentacion:int}")]
    public async Task<IActionResult> UpsertPrecio(int id, int idPresentacion, [FromBody] PrecioInput input, CancellationToken ct)
    {
        var ok = await _service.UpsertPrecioAsync(id, idPresentacion, input, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la lista."));
    }

    /// <summary>Carga el precio de todas las presentaciones del artículo desde un único precio unitario.</summary>
    [HttpPut("{id:int}/articulos/{idArticulo:int}/precio")]
    public async Task<IActionResult> UpsertPrecioArticulo(int id, int idArticulo,
        [FromBody] PrecioArticuloInput input, CancellationToken ct)
    {
        var r = await _service.UpsertPrecioArticuloAsync(id, idArticulo, input, ct);
        return r is null
            ? NotFound(ApiResult<IReadOnlyList<PrecioAplicadoDto>>.Fail("NO_ENCONTRADO", "No existe la lista."))
            : Ok(ApiResult<IReadOnlyList<PrecioAplicadoDto>>.Success(r));
    }

    [HttpDelete("{id:int}/precios/{idPresentacion:int}")]
    public async Task<IActionResult> DeletePrecio(int id, int idPresentacion, CancellationToken ct)
    {
        var ok = await _service.DeletePrecioAsync(id, idPresentacion, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el precio."));
    }
}
