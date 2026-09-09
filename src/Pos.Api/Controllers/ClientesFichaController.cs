using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Clientes;
using Pos.Application.Common;

namespace Pos.Api.Controllers;

/// <summary>
/// Módulo "Clientes" del menú principal (búsqueda manual, distinto del módulo "Clientes" de
/// escaneo de DNI — ver ClientesController): buscar un cliente por nombre/fantasía/código/CUIT/
/// documento, igual que el ABM de Administración, pero de SOLO LECTURA (sin editar ni dar de baja)
/// y con la misma impresión de ticket que el módulo de autoservicio. Permiso propio
/// ("ClientesFicha") porque expone datos que el kiosco de autoservicio no muestra (CUIT, condición
/// de IVA, cuenta corriente).
/// </summary>
[ApiController]
[Route("api/v1/clientes-ficha")]
[Authorize]
[ModuloAutorizado("ClientesFicha", "Administrador")]
public class ClientesFichaController : ControllerBase
{
    private readonly IClienteService _service;
    public ClientesFichaController(IClienteService service) => _service = service;

    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string? q, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ClienteDto>>.Success(await _service.GetAllAsync(q, null, ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var c = await _service.GetByIdAsync(id, ct);
        return c is null
            ? NotFound(ApiResult<ClienteDto>.Fail("NO_ENCONTRADO", "No existe el cliente."))
            : Ok(ApiResult<ClienteDto>.Success(c));
    }

    [HttpGet("{id:int}/ticket")]
    public async Task<IActionResult> GetTicket(int id, CancellationToken ct)
    {
        var t = await _service.GetTicketAsync(id, ct);
        return t is null
            ? NotFound(ApiResult<ClienteTicketDto>.Fail("NO_ENCONTRADO", "No existe el cliente."))
            : Ok(ApiResult<ClienteTicketDto>.Success(t));
    }
}
