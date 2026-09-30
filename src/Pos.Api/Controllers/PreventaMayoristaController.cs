using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Application.PreventaMayorista;

namespace Pos.Api.Controllers;

/// <summary>
/// Módulo "Preventa Mayorista" del menú principal: consulta de solo lectura de los pedidos
/// pendientes (REPARTO en PEDIPEND/PEDIAPP) tomados por la app legacy VFP Mayorista_Release,
/// cruzados contra el padrón de Clientes/Artículos de pos-mayorista. No genera ninguna operación.
/// </summary>
[ApiController]
[Route("api/v1/preventa-mayorista")]
[Authorize]
[ModuloAutorizado("PreventaMayorista", "Administrador,Supervisor")]
public class PreventaMayoristaController : ControllerBase
{
    private readonly IPreventaMayoristaService _service;
    private readonly IRecargoLogisticaService _recargoLogistica;
    private readonly IVendedorService _vendedores;

    public PreventaMayoristaController(IPreventaMayoristaService service, IRecargoLogisticaService recargoLogistica,
        IVendedorService vendedores)
    {
        _service = service;
        _recargoLogistica = recargoLogistica;
        _vendedores = vendedores;
    }

    /// <param name="forzarRefresco">true = ignora el caché de 5 minutos y vuelve a leer el DBF.</param>
    [HttpGet("pedidos")]
    public async Task<IActionResult> GetPedidos([FromQuery] bool forzarRefresco, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<PreventaClienteDto>>.Success(
            await _service.ObtenerPedidosPendientesAsync(forzarRefresco, ct)));

    /// <summary>Tramos de recargo logístico ya importados a SQL (ver POST .../importar).</summary>
    [HttpGet("recargo-logistica")]
    public async Task<IActionResult> GetRecargoLogistica(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<RecargoLogisticaDto>>.Success(await _recargoLogistica.ObtenerAsync(ct)));

    /// <summary>Relee recargo_logistica.dbf y reemplaza el contenido de la tabla SQL.</summary>
    [HttpPost("recargo-logistica/importar")]
    public async Task<IActionResult> ImportarRecargoLogistica(CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _recargoLogistica.ImportarAsync(ct)));

    /// <summary>Vendedores ya importados a SQL (ver POST .../importar).</summary>
    [HttpGet("vendedores")]
    public async Task<IActionResult> GetVendedores(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<VendedorDto>>.Success(await _vendedores.ObtenerAsync(ct)));

    /// <summary>Relee operator.dbf y reemplaza el contenido de la tabla SQL.</summary>
    [HttpPost("vendedores/importar")]
    public async Task<IActionResult> ImportarVendedores(CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _vendedores.ImportarAsync(ct)));
}
