using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Clientes;
using Pos.Application.Common;

namespace Pos.Api.Controllers;

/// <summary>
/// Módulo "Clientes" del menú principal: buscar un cliente (por nombre/código/CUIT/DNI, o escaneando
/// el número de tarjeta/documento) e imprimir un ticket de mostrador con sus datos, para identificarlo
/// rápido en cualquier puesto (no solo en Caja). No toca precios/convenios — para eso está
/// CajaController.BuscarCliente, que sí depende de la sucursal.
/// </summary>
[ApiController]
[Route("api/v1/clientes")]
[Authorize]
[ModuloAutorizado("Clientes", "Cajero,Supervisor,Tesorero,Administrador")]
public class ClientesController : ControllerBase
{
    private readonly IClienteService _service;
    public ClientesController(IClienteService service) => _service = service;

    [HttpGet("buscar-por-dni")]
    public async Task<IActionResult> BuscarPorDni([FromQuery] string dni, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ClienteTicketDto>>.Success(await _service.BuscarPorDniAsync(dni ?? "", ct)));
}
