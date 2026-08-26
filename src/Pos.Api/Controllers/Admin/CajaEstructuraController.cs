using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/sucursales/{idSucursal:int}")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class CajaEstructuraController : ControllerBase
{
    private readonly ICajaEstructuraService _service;
    public CajaEstructuraController(ICajaEstructuraService service) => _service = service;

    // Tipos de punto de venta: catálogo FIJO (ELECTRONICA / FISCAL / PRESUPUESTO). Solo lectura —
    // no hay POST ni DELETE a propósito: cada tipo implica un camino de emisión distinto en el
    // código, así que uno inventado desde el ABM no haría nada.
    [HttpGet("tipos-punto-venta")]
    public async Task<IActionResult> GetTipos(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<TipoPuntoVentaDto>>.Success(await _service.GetTiposPvAsync(idSucursal, ct)));

    // Puntos de venta
    [HttpGet("puntos-venta")]
    public async Task<IActionResult> GetPv(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<PuntoVentaDto>>.Success(await _service.GetPuntosVentaAsync(idSucursal, ct)));

    [HttpPost("puntos-venta")]
    public async Task<IActionResult> CreatePv(int idSucursal, [FromBody] PuntoVentaInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreatePuntoVentaAsync(idSucursal, input, ct)));

    [HttpPut("puntos-venta/{id:int}")]
    public async Task<IActionResult> UpdatePv(int idSucursal, int id, [FromBody] PuntoVentaInput input, CancellationToken ct) =>
        await _service.UpdatePuntoVentaAsync(idSucursal, id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe."));

    [HttpDelete("puntos-venta/{id:int}")]
    public async Task<IActionResult> DeletePv(int idSucursal, int id, CancellationToken ct) =>
        await _service.DeletePuntoVentaAsync(idSucursal, id, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe."));

    // Puestos
    [HttpGet("puestos")]
    public async Task<IActionResult> GetPuestos(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<PuestoDto>>.Success(await _service.GetPuestosAsync(idSucursal, ct)));

    [HttpPost("puestos")]
    public async Task<IActionResult> CreatePuesto(int idSucursal, [FromBody] PuestoInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreatePuestoAsync(idSucursal, input, ct)));

    [HttpPut("puestos/{id:int}")]
    public async Task<IActionResult> UpdatePuesto(int idSucursal, int id, [FromBody] PuestoInput input, CancellationToken ct) =>
        await _service.UpdatePuestoAsync(idSucursal, id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe."));

    [HttpDelete("puestos/{id:int}")]
    public async Task<IActionResult> DeletePuesto(int idSucursal, int id, CancellationToken ct) =>
        await _service.DeletePuestoAsync(idSucursal, id, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe."));

    /// <summary>
    /// Vincula el puesto a la PC desde la que se llama ESTE endpoint (toma X-Puesto-Id del propio
    /// request) — el Administrador tiene que estar parado frente a esa PC. Sin body: el
    /// identificador no lo tipea nadie, lo manda el navegador solo.
    /// </summary>
    [HttpPost("puestos/{id:int}/vincular-equipo")]
    public async Task<IActionResult> VincularEquipo(int idSucursal, int id, CancellationToken ct)
    {
        var idEquipo = Request.Headers["X-Puesto-Id"].ToString();
        if (string.IsNullOrWhiteSpace(idEquipo))
            return BadRequest(ApiResult<bool>.Fail("SIN_ID_EQUIPO",
                "Esta PC todavía no generó su identificador — recargá la página e intentá de nuevo."));

        return await _service.VincularEquipoAsync(idSucursal, id, idEquipo.Trim(), ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe."));
    }

    // Cajas
    [HttpGet("cajas")]
    public async Task<IActionResult> GetCajas(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<CajaDto>>.Success(await _service.GetCajasAsync(idSucursal, ct)));

    [HttpPost("cajas")]
    public async Task<IActionResult> CreateCaja(int idSucursal, [FromBody] CajaInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateCajaAsync(idSucursal, input, ct)));

    [HttpPut("cajas/{id:int}")]
    public async Task<IActionResult> UpdateCaja(int idSucursal, int id, [FromBody] CajaInput input, CancellationToken ct) =>
        await _service.UpdateCajaAsync(idSucursal, id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la caja."));

    [HttpDelete("cajas/{id:int}")]
    public async Task<IActionResult> DeleteCaja(int idSucursal, int id, CancellationToken ct) =>
        await _service.DeleteCajaAsync(idSucursal, id, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la caja."));

    // Terminales de tarjeta
    [HttpGet("terminales-tarjeta")]
    public async Task<IActionResult> GetTerminales(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<TerminalTarjetaDto>>.Success(await _service.GetTerminalesAsync(idSucursal, ct)));

    [HttpPost("terminales-tarjeta")]
    public async Task<IActionResult> CreateTerminal(int idSucursal, [FromBody] TerminalTarjetaInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateTerminalAsync(idSucursal, input, ct)));

    [HttpPut("terminales-tarjeta/{id:int}")]
    public async Task<IActionResult> UpdateTerminal(int idSucursal, int id, [FromBody] TerminalTarjetaInput input, CancellationToken ct) =>
        await _service.UpdateTerminalAsync(idSucursal, id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la terminal."));

    [HttpDelete("terminales-tarjeta/{id:int}")]
    public async Task<IActionResult> DeleteTerminal(int idSucursal, int id, CancellationToken ct) =>
        await _service.DeleteTerminalAsync(idSucursal, id, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la terminal."));
}
