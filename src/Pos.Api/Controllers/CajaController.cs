using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Caja;
using Pos.Application.Common;

namespace Pos.Api.Controllers;

/// <summary>Operaciones de caja (módulo de caja del SRS): apertura, identificación, lectura, operación.</summary>
[ApiController]
[Route("api/v1/caja")]
[Authorize]
[ModuloAutorizado("Caja", "Cajero,Supervisor,Administrador")]
public class CajaController : ControllerBase
{
    private readonly ICajaService _service;
    public CajaController(ICajaService service) => _service = service;

    [HttpPost("apertura")]
    public async Task<IActionResult> Abrir([FromBody] AperturaRequest req, CancellationToken ct) =>
        Ok(ApiResult<LoteDto>.Success(await _service.AbrirCajaAsync(req, ct)));

    [HttpGet("lote-actual")]
    public async Task<IActionResult> LoteActual([FromQuery] int idSucursal, [FromQuery] int idCaja, CancellationToken ct)
    {
        var l = await _service.ObtenerLoteActualAsync(idSucursal, idCaja, ct);
        return l is null
            ? NotFound(ApiResult<LoteDto>.Fail("SIN_LOTE", "No hay lote abierto para esta caja hoy."))
            : Ok(ApiResult<LoteDto>.Success(l));
    }

    [HttpGet("mis-turnos")]
    public async Task<IActionResult> MisTurnos([FromQuery] int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<TurnoAbiertoDto>>.Success(await _service.GetMisTurnosAbiertosAsync(idSucursal, ct)));

    [HttpGet("cajas")]
    public async Task<IActionResult> Cajas([FromQuery] int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<CajaDisponibleDto>>.Success(await _service.GetCajasAsync(idSucursal, ct)));

    [HttpGet("descripcion")]
    public async Task<IActionResult> Descripcion([FromQuery] int idSucursal, [FromQuery] int idCaja, CancellationToken ct) =>
        Ok(ApiResult<string?>.Success(await _service.ObtenerDescripcionCajaAsync(idSucursal, idCaja, ct)));

    /// <summary>Medios habilitados para el cliente (los restringidos a un cluster se filtran).</summary>
    [HttpGet("medios-pago")]
    public async Task<IActionResult> MediosPago([FromQuery] int? idCliente, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<MedioPagoResumen>>.Success(await _service.GetMediosPagoAsync(idCliente, ct)));

    /// <summary>Planes de cuotas del medio (vacío si no tiene ninguno cargado, ej. no es Tarjeta).</summary>
    [HttpGet("medios-pago/{idMedioPago:int}/planes")]
    public async Task<IActionResult> PlanesMedio(int idMedioPago, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<PlanCuotaResumen>>.Success(await _service.GetPlanesMedioAsync(idMedioPago, ct)));

    /// <summary>Para calcular en vivo, en la pantalla de cobro, cuánto se le informa al cliente que abona por medio.</summary>
    [HttpGet("ofertas-medio-pago")]
    public async Task<IActionResult> OfertasMedioPagoVigentes([FromQuery] int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<OfertaMedioPagoVigenteDto>>.Success(await _service.GetOfertasMedioPagoVigentesAsync(idSucursal, ct)));

    /// <summary>Bancos para el combo de banco emisor al cobrar con Cheque.</summary>
    [HttpGet("bancos")]
    public async Task<IActionResult> Bancos(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<BancoResumen>>.Success(await _service.GetBancosAsync(ct)));

    [HttpGet("clientes/buscar")]
    public async Task<IActionResult> BuscarCliente([FromQuery] int idSucursal, [FromQuery] string q, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ClienteResumen>>.Success(await _service.BuscarClienteAsync(idSucursal, q ?? "", ct)));

    [HttpGet("articulos/buscar")]
    public async Task<IActionResult> BuscarArticulo([FromQuery] int idSucursal, [FromQuery] string codigo,
        [FromQuery] int? idCliente, CancellationToken ct)
    {
        var a = await _service.BuscarArticuloAsync(idSucursal, codigo ?? "", idCliente, ct);
        return a is null
            ? NotFound(ApiResult<ArticuloEncontrado>.Fail("NO_ENCONTRADO", "Artículo no encontrado."))
            : Ok(ApiResult<ArticuloEncontrado>.Success(a));
    }

    /// <summary>Búsqueda manual desde la lupa del campo de escaneo.</summary>
    [HttpGet("articulos/buscar-lista")]
    public async Task<IActionResult> BuscarArticulos([FromQuery] int idSucursal, [FromQuery] string texto,
        [FromQuery] int? idCliente, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ArticuloEncontrado>>.Success(
            await _service.BuscarArticulosAsync(idSucursal, texto ?? "", idCliente, ct)));

    [HttpGet("operaciones/pendientes")]
    public async Task<IActionResult> OperacionesPendientes([FromQuery] int idSucursal, [FromQuery] int idCaja,
        [FromQuery] int idCliente, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<OperacionPendienteDto>>.Success(
            await _service.GetOperacionesPendientesAsync(idSucursal, idCaja, idCliente, ct)));

    [HttpPost("operaciones")]
    public async Task<IActionResult> CrearOperacion([FromBody] CrearOperacionRequest req, CancellationToken ct) =>
        Ok(ApiResult<OperacionDto>.Success(await _service.CrearOperacionAsync(req, ct)));

    [HttpGet("operaciones/{idOperacion:int}")]
    public async Task<IActionResult> ObtenerOperacion(int idOperacion, [FromQuery] int idSucursal, CancellationToken ct)
    {
        var op = await _service.ObtenerOperacionAsync(idSucursal, idOperacion, ct);
        return op is null
            ? NotFound(ApiResult<OperacionDto>.Fail("NO_ENCONTRADA", "La operación no existe."))
            : Ok(ApiResult<OperacionDto>.Success(op));
    }

    [HttpPost("operaciones/{idOperacion:int}/lineas")]
    public async Task<IActionResult> AgregarLinea(int idOperacion, [FromQuery] int idSucursal,
        [FromBody] AgregarLineaRequest req, CancellationToken ct)
    {
        var op = await _service.AgregarLineaAsync(idSucursal, idOperacion, req, ct);
        return op is null
            ? NotFound(ApiResult<OperacionDto>.Fail("NO_ENCONTRADA", "La operación no existe."))
            : Ok(ApiResult<OperacionDto>.Success(op));
    }

    /// <summary>Los +/- de la tabla de artículos leídos. Cantidad 0 saca la línea.</summary>
    [HttpPut("operaciones/{idOperacion:int}/lineas/{idDetalle:long}/cantidad")]
    public async Task<IActionResult> CambiarCantidad(int idOperacion, long idDetalle, [FromQuery] int idSucursal,
        [FromBody] CambiarCantidadRequest req, CancellationToken ct)
    {
        var op = await _service.CambiarCantidadLineaAsync(idSucursal, idOperacion, idDetalle, req.Cantidad, req.CodigoSupervisor, ct);
        return op is null
            ? NotFound(ApiResult<OperacionDto>.Fail("NO_ENCONTRADA", "La operación no existe."))
            : Ok(ApiResult<OperacionDto>.Success(op));
    }

    [HttpPost("operaciones/{idOperacion:int}/lineas/{idDetalle:long}/anular")]
    public async Task<IActionResult> AnularLinea(int idOperacion, long idDetalle, [FromQuery] int idSucursal,
        [FromQuery] string? codigoSupervisor, CancellationToken ct)
    {
        var op = await _service.AnularLineaAsync(idSucursal, idOperacion, idDetalle, codigoSupervisor, ct);
        return op is null
            ? NotFound(ApiResult<OperacionDto>.Fail("NO_ENCONTRADA", "La operación no existe."))
            : Ok(ApiResult<OperacionDto>.Success(op));
    }

    [HttpPost("operaciones/{idOperacion:int}/finalizar")]
    public async Task<IActionResult> Finalizar(int idOperacion, [FromQuery] int idSucursal, CancellationToken ct)
    {
        var op = await _service.FinalizarOperacionAsync(idSucursal, idOperacion, ct);
        return op is null
            ? NotFound(ApiResult<OperacionDto>.Fail("NO_ENCONTRADA", "La operación no existe."))
            : Ok(ApiResult<OperacionDto>.Success(op));
    }

    [HttpPost("operaciones/{idOperacion:int}/reabrir")]
    public async Task<IActionResult> Reabrir(int idOperacion, [FromQuery] int idSucursal, CancellationToken ct)
    {
        var op = await _service.ReabrirOperacionAsync(idSucursal, idOperacion, ct);
        return op is null
            ? NotFound(ApiResult<OperacionDto>.Fail("NO_ENCONTRADA", "La operación no existe."))
            : Ok(ApiResult<OperacionDto>.Success(op));
    }

    [HttpGet("redondeo")]
    public async Task<IActionResult> Redondeo([FromQuery] decimal total, CancellationToken ct) =>
        Ok(ApiResult<RedondeoDto>.Success(await _service.CalcularRedondeoAsync(total, ct)));
}
