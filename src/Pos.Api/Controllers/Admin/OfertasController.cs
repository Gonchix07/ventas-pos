using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/sucursales/{idSucursal:int}/ofertas")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class OfertasController : ControllerBase
{
    private readonly IOfertaAdminService _service;
    public OfertasController(IOfertaAdminService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<OfertaListItem>>.Success(await _service.GetAllAsync(idSucursal, ct)));

    [HttpGet("{idOferta:int}")]
    public async Task<IActionResult> GetById(int idSucursal, int idOferta, CancellationToken ct)
    {
        var o = await _service.GetByIdAsync(idSucursal, idOferta, ct);
        return o is null
            ? NotFound(ApiResult<OfertaDetail>.Fail("NO_ENCONTRADO", "No existe la oferta."))
            : Ok(ApiResult<OfertaDetail>.Success(o));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int idSucursal, [FromBody] OfertaInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateAsync(idSucursal, input, ct)));

    [HttpPut("{idOferta:int}")]
    public async Task<IActionResult> Update(int idSucursal, int idOferta, [FromBody] OfertaInput input, CancellationToken ct) =>
        await _service.UpdateAsync(idSucursal, idOferta, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la oferta."));

    [HttpDelete("{idOferta:int}")]
    public async Task<IActionResult> Delete(int idSucursal, int idOferta, CancellationToken ct) =>
        await _service.DeleteAsync(idSucursal, idOferta, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la oferta."));
}
