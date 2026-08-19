using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Administrador")]
public class ConveniosController : ControllerBase
{
    private readonly IConvenioService _service;
    public ConveniosController(IConvenioService service) => _service = service;

    [HttpGet("sucursales/{idSucursal:int}/convenios")]
    public async Task<IActionResult> Get(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ConvenioDto>>.Success(await _service.GetAsync(idSucursal, ct)));

    [HttpPost("sucursales/{idSucursal:int}/convenios")]
    public async Task<IActionResult> Create(int idSucursal, [FromBody] ConvenioInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateAsync(idSucursal, input, ct)));

    [HttpPut("sucursales/{idSucursal:int}/convenios/{id:int}")]
    public async Task<IActionResult> Update(int idSucursal, int id, [FromBody] ConvenioInput input, CancellationToken ct) =>
        await _service.UpdateAsync(idSucursal, id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el convenio."));

    [HttpDelete("sucursales/{idSucursal:int}/convenios/{id:int}")]
    public async Task<IActionResult> Delete(int idSucursal, int id, CancellationToken ct) =>
        await _service.DeleteAsync(idSucursal, id, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el convenio."));
}

[ApiController]
[Route("api/v1/admin/clusters")]
[Authorize(Roles = "Administrador")]
public class ClustersController : ControllerBase
{
    private readonly IClusterService _service;
    public ClustersController(IClusterService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ClusterDto>>.Success(await _service.GetClustersAsync(ct)));

    [HttpGet("{idCluster:int}/miembros")]
    public async Task<IActionResult> GetMiembros(int idCluster, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ClusterMiembroDto>>.Success(await _service.GetMiembrosAsync(idCluster, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ClusterInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateClusterAsync(input, ct)));

    [HttpPut("{idCluster:int}")]
    public async Task<IActionResult> Rename(int idCluster, [FromBody] ClusterInput input, CancellationToken ct) =>
        await _service.RenameClusterAsync(idCluster, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el cluster."));

    [HttpPost("{idCluster:int}/miembros")]
    public async Task<IActionResult> AddMiembro(int idCluster, [FromBody] ClusterMiembroInput input, CancellationToken ct) =>
        await _service.AddMiembroAsync(idCluster, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el cluster."));

    /// <summary>Guardado en lote de miembros (reemplaza el set completo).</summary>
    [HttpPut("{idCluster:int}/miembros")]
    public async Task<IActionResult> SetMiembros(int idCluster, [FromBody] ClusterMiembrosSetInput input, CancellationToken ct)
    {
        var r = await _service.SetMiembrosAsync(idCluster, input, ct);
        return r is null
            ? NotFound(ApiResult<ClusterMiembrosResultado>.Fail("NO_ENCONTRADO", "No existe el cluster."))
            : Ok(ApiResult<ClusterMiembrosResultado>.Success(r));
    }

    [HttpDelete("{idCluster:int}/miembros/{idCliente:int}")]
    public async Task<IActionResult> RemoveMiembro(int idCluster, int idCliente, CancellationToken ct) =>
        await _service.RemoveMiembroAsync(idCluster, idCliente, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el miembro."));

    [HttpDelete("{idCluster:int}")]
    public async Task<IActionResult> Delete(int idCluster, CancellationToken ct) =>
        await _service.DeleteClusterAsync(idCluster, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el cluster."));
}

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Administrador")]
public class TarjetasController : ControllerBase
{
    private readonly ITarjetaAdminService _service;
    public TarjetasController(ITarjetaAdminService service) => _service = service;

    [HttpGet("tipos-tarjeta")]
    public async Task<IActionResult> GetTipos(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<TipoTarjetaDto>>.Success(await _service.GetTiposAsync(ct)));

    [HttpPost("tipos-tarjeta")]
    public async Task<IActionResult> CreateTipo([FromBody] TipoTarjetaInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateTipoAsync(input, ct)));

    [HttpPut("tipos-tarjeta/{id:int}")]
    public async Task<IActionResult> UpdateTipo(int id, [FromBody] TipoTarjetaInput input, CancellationToken ct) =>
        await _service.UpdateTipoAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el tipo de tarjeta."));

    [HttpDelete("tipos-tarjeta/{id:int}")]
    public async Task<IActionResult> DeleteTipo(int id, CancellationToken ct) =>
        await _service.DeleteTipoAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el tipo de tarjeta."));

    [HttpGet("clientes/{idCliente:int}/tarjetas")]
    public async Task<IActionResult> GetTarjetas(int idCliente, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<TarjetaClienteDto>>.Success(await _service.GetTarjetasAsync(idCliente, ct)));

    [HttpPost("clientes/{idCliente:int}/tarjetas")]
    public async Task<IActionResult> AddTarjeta(int idCliente, [FromBody] TarjetaClienteInput input, CancellationToken ct)
    {
        var r = await _service.AddTarjetaAsync(idCliente, input, ct);
        return r.Ok
            ? Ok(ApiResult<AltaTarjetaResultado>.Success(r))
            : NotFound(ApiResult<AltaTarjetaResultado>.Fail("NO_ENCONTRADO", "No existe el cliente."));
    }

    [HttpDelete("clientes/{idCliente:int}/tarjetas/{idTipoTarjeta:int}/{nroTarjeta}")]
    public async Task<IActionResult> RemoveTarjeta(int idCliente, int idTipoTarjeta, string nroTarjeta, CancellationToken ct) =>
        await _service.RemoveTarjetaAsync(idCliente, idTipoTarjeta, nroTarjeta, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la tarjeta."));
}

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Administrador")]
public class CuentaCorrienteAdminController : ControllerBase
{
    private readonly IClienteEnCuentaService _service;
    public CuentaCorrienteAdminController(IClienteEnCuentaService service) => _service = service;

    [HttpGet("sucursales/{idSucursal:int}/cuenta-corriente")]
    public async Task<IActionResult> Get(int idSucursal, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<CuentaCorrienteLimiteDto>>.Success(await _service.GetAsync(idSucursal, ct)));

    [HttpPut("sucursales/{idSucursal:int}/cuenta-corriente/{idCliente:int}")]
    public async Task<IActionResult> Upsert(int idSucursal, int idCliente,
        [FromBody] CuentaCorrienteLimiteInput input, CancellationToken ct)
    {
        await _service.UpsertAsync(idSucursal, idCliente, input, ct);
        return Ok(ApiResult<bool>.Success(true));
    }

    [HttpDelete("sucursales/{idSucursal:int}/cuenta-corriente/{idCliente:int}")]
    public async Task<IActionResult> Delete(int idSucursal, int idCliente, CancellationToken ct) =>
        await _service.DeleteAsync(idSucursal, idCliente, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "El cliente no tiene cuenta corriente habilitada."));
}

[ApiController]
[Route("api/v1/admin/padrones")]
[Authorize(Roles = "Administrador")]
public class PadronesController : ControllerBase
{
    private readonly IPadronService _service;
    public PadronesController(IPadronService service) => _service = service;

    [HttpGet("iibb")]
    public async Task<IActionResult> GetIibb([FromQuery] string? q, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<PadronIibbDto>>.Success(await _service.GetIibbAsync(q, ct)));

    [HttpPut("iibb")]
    public async Task<IActionResult> UpsertIibb([FromBody] PadronIibbInput input, CancellationToken ct)
    {
        await _service.UpsertIibbAsync(input, ct);
        return Ok(ApiResult<bool>.Success(true));
    }

    /// <summary>
    /// Reemplaza el padrón de IIBB completo con un archivo PadronRGSPer (TXT con ; como separador,
    /// CUIT en la columna 5 y alícuota en la 9).
    /// </summary>
    [HttpPost("iibb/importar")]
    [DisableRequestSizeLimit]
    [DisableFormValueModelBinding]
    public Task<IActionResult> ImportarIibb([FromQuery] bool incluirSinPercepcion, CancellationToken ct) =>
        ConArchivoDelRequestAsync(stream => _service.ImportarIibbAsync(stream, incluirSinPercepcion, ct), ct);

    /// <summary>
    /// Reemplaza el padrón de excepción de percepción de IVA. Archivo de ancho fijo: el CUIT son
    /// los primeros 11 caracteres de cada línea.
    /// </summary>
    [HttpPost("excepcion-iva/importar")]
    [DisableRequestSizeLimit]
    [DisableFormValueModelBinding]
    public Task<IActionResult> ImportarExcepcionIva(CancellationToken ct) =>
        ConArchivoDelRequestAsync(stream => _service.ImportarExcepcionIvaAsync(stream, ct), ct);

    /// <summary>
    /// Toma el archivo del multipart y se lo pasa al importador A MEDIDA QUE LLEGA, con
    /// MultipartReader en vez de IFormFile: los padrones reales pesan cientos de MB y IFormFile los
    /// bufferearía enteros en disco antes de empezar. Por lo mismo estas acciones desactivan el
    /// límite global de 2 MB del body y el model binding de formularios (que leería el stream).
    /// </summary>
    private async Task<IActionResult> ConArchivoDelRequestAsync(
        Func<Stream, Task<ImportacionPadronDto>> importar, CancellationToken ct)
    {
        static IActionResult FaltaArchivo(string mensaje) =>
            new BadRequestObjectResult(ApiResult<ImportacionPadronDto>.Fail("ARCHIVO_REQUERIDO", mensaje));

        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || !contentType.MediaType.HasValue
            || !contentType.MediaType.Value!.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            return FaltaArchivo("Hay que enviar el archivo del padrón como multipart/form-data.");

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary)) return FaltaArchivo("Falta el archivo.");

        var reader = new MultipartReader(boundary, Request.Body);
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd)
                || (!cd.FileName.HasValue && !cd.FileNameStar.HasValue))
                continue;

            return Ok(ApiResult<ImportacionPadronDto>.Success(await importar(section.Body)));
        }

        return FaltaArchivo("No se encontró ningún archivo en el envío.");
    }

    [HttpDelete("iibb/{cuit}")]
    public async Task<IActionResult> DeleteIibb(string cuit, CancellationToken ct) =>
        await _service.DeleteIibbAsync(cuit, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el CUIT."));

    [HttpGet("excepcion-iva")]
    public async Task<IActionResult> GetExIva([FromQuery] string? q, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<PadronExIvaDto>>.Success(await _service.GetExIvaAsync(q, ct)));

    [HttpPost("excepcion-iva")]
    public async Task<IActionResult> AddExIva([FromBody] PadronExIvaDto input, CancellationToken ct)
    {
        await _service.AddExIvaAsync(input.Cuit, ct);
        return Ok(ApiResult<bool>.Success(true));
    }

    [HttpDelete("excepcion-iva/{cuit}")]
    public async Task<IActionResult> DeleteExIva(string cuit, CancellationToken ct) =>
        await _service.DeleteExIvaAsync(cuit, ct)
            ? Ok(ApiResult<bool>.Success(true)) : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el CUIT."));
}
