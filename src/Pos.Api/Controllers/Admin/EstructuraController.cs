using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Abm;
using Pos.Application.Common;
using Pos.Application.Facturacion;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class EstructuraController : ControllerBase
{
    private readonly IEstructuraService _service;
    private readonly ICaeaCargadoService _caea;
    public EstructuraController(IEstructuraService service, ICaeaCargadoService caea)
    {
        _service = service;
        _caea = caea;
    }

    // ---- Empresas ----
    [HttpGet("empresas")]
    public async Task<IActionResult> GetEmpresas(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<EmpresaDto>>.Success(await _service.GetEmpresasAsync(ct)));

    [HttpPost("empresas")]
    public async Task<IActionResult> CreateEmpresa([FromBody] EmpresaInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateEmpresaAsync(input, ct)));

    [HttpPut("empresas/{id:int}")]
    public async Task<IActionResult> UpdateEmpresa(int id, [FromBody] EmpresaInput input, CancellationToken ct) =>
        await _service.UpdateEmpresaAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la empresa."));

    [HttpDelete("empresas/{id:int}")]
    public async Task<IActionResult> DeleteEmpresa(int id, CancellationToken ct) =>
        await _service.DeleteEmpresaAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la empresa."));

    // ---- Certificado CAE (facturación electrónica) ----
    [HttpGet("empresas/{id:int}/certificado")]
    public async Task<IActionResult> GetCertificado(int id, CancellationToken ct) =>
        Ok(ApiResult<CertificadoCaeDto>.Success(await _service.GetCertificadoAsync(id, ct)));

    // multipart/form-data: el .pfx (unos pocos KB) + su contraseña. El límite global de Kestrel
    // (2 MB, ver Program.cs) ya lo cubre de sobra; no hace falta [RequestSizeLimit] propio.
    [HttpPost("empresas/{id:int}/certificado")]
    public async Task<IActionResult> SubirCertificado(int id, IFormFile? archivo, [FromForm] string clave, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(ApiResult<CertificadoCaeDto>.Fail("ARCHIVO_REQUERIDO", "Adjuntá el archivo .pfx/.p12 del certificado."));
        if (string.IsNullOrWhiteSpace(clave))
            return BadRequest(ApiResult<CertificadoCaeDto>.Fail("CLAVE_REQUERIDA", "Ingresá la contraseña del certificado."));

        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms, ct);
        var dto = await _service.SubirCertificadoAsync(id, ms.ToArray(), archivo.FileName, clave, ct);
        return Ok(ApiResult<CertificadoCaeDto>.Success(dto));
    }

    // Alternativa cuando no se tiene un .pfx ya armado: se suben la clave privada (.key, PEM) y el
    // certificado (.crt/.cer) que entregó ARCA por separado, y el backend los combina.
    [HttpPost("empresas/{id:int}/certificado/clave-cert")]
    public async Task<IActionResult> SubirCertificadoDesdeClaveYCert(int id, IFormFile? clavePrivada,
        IFormFile? certificado, [FromForm] string? passphrase, CancellationToken ct)
    {
        if (clavePrivada is null || clavePrivada.Length == 0)
            return BadRequest(ApiResult<CertificadoCaeDto>.Fail("ARCHIVO_REQUERIDO", "Adjuntá el archivo de la clave privada (.key)."));
        if (certificado is null || certificado.Length == 0)
            return BadRequest(ApiResult<CertificadoCaeDto>.Fail("ARCHIVO_REQUERIDO", "Adjuntá el archivo del certificado (.crt/.cer)."));

        using var msKey = new MemoryStream();
        await clavePrivada.CopyToAsync(msKey, ct);
        using var msCert = new MemoryStream();
        await certificado.CopyToAsync(msCert, ct);

        var dto = await _service.SubirCertificadoDesdeClaveYCertAsync(id, msKey.ToArray(), msCert.ToArray(),
            string.IsNullOrWhiteSpace(passphrase) ? null : passphrase, ct);
        return Ok(ApiResult<CertificadoCaeDto>.Success(dto));
    }

    [HttpDelete("empresas/{id:int}/certificado")]
    public async Task<IActionResult> EliminarCertificado(int id, CancellationToken ct) =>
        await _service.EliminarCertificadoAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la empresa."));

    // Solo lectura contra ARCA (login WSAA + FEDummy + FECompUltimoAutorizado) — nunca emite ni
    // autoriza un comprobante. cbteTipo default 6 = Factura B (lo que más se emite en este negocio).
    [HttpGet("empresas/{id:int}/certificado/probar-conexion")]
    public async Task<IActionResult> ProbarConexionAfip(int id, [FromQuery] int ptoVta, [FromQuery] int cbteTipo = 6, CancellationToken ct = default) =>
        Ok(ApiResult<ProbarConexionAfipDto>.Success(await _service.ProbarConexionAfipAsync(id, ptoVta, cbteTipo, ct)));

    // OJO: esto pide un CAE REAL (no es solo lectura) — en producción autoriza un comprobante de
    // verdad. Pensado para probar el circuito completo en homologación.
    [HttpPost("empresas/{id:int}/certificado/probar-cae")]
    public async Task<IActionResult> ProbarCae(int id, [FromQuery] int ptoVta, [FromQuery] int cbteTipo,
        [FromQuery] decimal importeTotal, CancellationToken ct = default) =>
        Ok(ApiResult<ProbarCaeDto>.Success(await _service.ProbarCaeAsync(id, ptoVta, cbteTipo, importeTotal, ct)));

    // ---- CAEA precargado (contingencia cuando WSFEv1/CAE no responde) ----
    [HttpGet("empresas/{id:int}/caea")]
    public async Task<IActionResult> GetCaea(int id, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<CaeaCargadoDto>>.Success(await _caea.GetAsync(id, ct)));

    [HttpPost("empresas/{id:int}/caea")]
    public async Task<IActionResult> CreateCaea(int id, [FromBody] CaeaCargadoInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _caea.CreateAsync(input with { IdEmpresa = id }, ct)));

    [HttpPut("caea/{idCaea:int}")]
    public async Task<IActionResult> UpdateCaea(int idCaea, [FromBody] CaeaCargadoInput input, CancellationToken ct) =>
        await _caea.UpdateAsync(idCaea, input, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe ese CAEA cargado."));

    [HttpDelete("caea/{idCaea:int}")]
    public async Task<IActionResult> DeleteCaea(int idCaea, CancellationToken ct) =>
        await _caea.DeleteAsync(idCaea, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe ese CAEA cargado."));

    // ---- Sucursales ----
    [HttpGet("sucursales")]
    public async Task<IActionResult> GetSucursales(CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<SucursalDto>>.Success(await _service.GetSucursalesAsync(ct)));

    [HttpPost("sucursales")]
    public async Task<IActionResult> CreateSucursal([FromBody] SucursalInput input, CancellationToken ct) =>
        Ok(ApiResult<int>.Success(await _service.CreateSucursalAsync(input, ct)));

    [HttpPut("sucursales/{id:int}")]
    public async Task<IActionResult> UpdateSucursal(int id, [FromBody] SucursalInput input, CancellationToken ct) =>
        await _service.UpdateSucursalAsync(id, input, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la sucursal."));

    [HttpDelete("sucursales/{id:int}")]
    public async Task<IActionResult> DeleteSucursal(int id, CancellationToken ct) =>
        await _service.DeleteSucursalAsync(id, ct)
            ? Ok(ApiResult<bool>.Success(true))
            : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe la sucursal."));
}
