using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Cierres;
using Pos.Application.Common;

namespace Pos.Api.Controllers;

/// <summary>
/// Arqueo X y cierre de TURNO del cajero (negocio, sobre el lote), disparados desde el módulo de
/// Caja (SRS). El Cierre Z del controlador fiscal vive en el mismo controller (mismos roles
/// habilitados a entrar) pero es una operación de máquina aparte, ver ICierreZFiscalService.
/// </summary>
[ApiController]
[Route("api/v1/caja")]
[Authorize]
[ModuloAutorizado("Caja", "Cajero,Supervisor,Administrador")]
public class CierresController : ControllerBase
{
    private readonly ICierreCajaService _service;
    private readonly ICierreZFiscalService _cierreZFiscal;
    public CierresController(ICierreCajaService service, ICierreZFiscalService cierreZFiscal)
    {
        _service = service;
        _cierreZFiscal = cierreZFiscal;
    }

    [HttpGet("motivos-diferencia")]
    public async Task<IActionResult> MotivosDiferencia(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<MotivoDto>>.Success(await _service.GetMotivosDiferenciaAsync(ct)));

    // imprimir=false: solo trae los acumulados para armar una pantalla (ej. el preview de "Cerrar
    // turno"), sin disparar la impresión del reporte X en el controlador fiscal. El botón "Arqueo X"
    // de la caja no manda este parámetro (default true): ahí sí corresponde imprimir.
    [HttpGet("arqueo-x")]
    public async Task<IActionResult> ArqueoX([FromQuery] int idSucursal, [FromQuery] int idCaja,
        [FromQuery] bool imprimir = true, CancellationToken ct = default) =>
        Ok(ApiResult<ArqueoXResponse>.Success(await _service.ArqueoXAsync(idSucursal, idCaja, imprimir, ct)));

    [HttpPost("cerrar-turno")]
    public async Task<IActionResult> CerrarTurno([FromQuery] int idSucursal, [FromQuery] int idCaja,
        [FromBody] CerrarTurnoRequest req, CancellationToken ct) =>
        Ok(ApiResult<CerrarTurnoResponse>.Success(await _service.CerrarTurnoAsync(idSucursal, idCaja, req, ct)));

    // No exige lote/turno abierto — pensado para dispararse desde la pantalla de apertura, antes de
    // (o sin necesidad de) abrir uno. Autorización por código de supervisor, ver CierreZFiscalService.
    [HttpPost("cierre-z-fiscal")]
    public async Task<IActionResult> CierreZFiscal([FromQuery] int idSucursal, [FromQuery] int idCaja,
        [FromBody] CierreZFiscalRequest req, CancellationToken ct) =>
        Ok(ApiResult<CierreZFiscalResponse>.Success(await _cierreZFiscal.EjecutarAsync(idSucursal, idCaja, req, ct)));
}
