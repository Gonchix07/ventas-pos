using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Application.VerificarPrecios;

namespace Pos.Api.Controllers;

/// <summary>Kiosco de autoconsulta de precios (módulo "VerificarPrecios" del menú principal):
/// escanear un producto y ver imagen + precios de lista + oferta/folder, sin identificar cliente.</summary>
[ApiController]
[Route("api/v1/verificar-precios")]
[Authorize]
[ModuloAutorizado("VerificarPrecios", "Cajero,Supervisor,Tesorero,Repositor,Administrador")]
public class VerificarPreciosController : ControllerBase
{
    private readonly IVerificarPreciosService _service;
    public VerificarPreciosController(IVerificarPreciosService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Consultar([FromQuery] int idSucursal, [FromQuery] string codigo, CancellationToken ct)
    {
        var r = await _service.ConsultarAsync(idSucursal, codigo ?? "", ct);
        return r is null
            ? NotFound(ApiResult<ConsultaPrecioResult>.Fail("NO_ENCONTRADO", "Producto no encontrado."))
            : Ok(ApiResult<ConsultaPrecioResult>.Success(r));
    }
}
