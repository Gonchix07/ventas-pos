using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/sucursales/{idSucursal:int}/ofertas-medio-pago")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class OfertasMedioPagoController : ControllerBase
{
    private readonly IOfertaMedioPagoAdminService _service;
    public OfertasMedioPagoController(IOfertaMedioPagoAdminService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<OfertaMedioPagoDto>>.Success(await _service.GetAllAsync(idSucursal, ct)));

    [HttpPost]
    public async Task<IActionResult> Create(int idSucursal, [FromBody] OfertaMedioPagoInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateAsync(idSucursal, input, ct)));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int idSucursal, int id, [FromBody] OfertaMedioPagoInput input, CancellationToken ct) =>
        await _service.UpdateAsync(idSucursal, id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la oferta."));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int idSucursal, int id, CancellationToken ct) =>
        await _service.DeleteAsync(idSucursal, id, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la oferta."));
}
