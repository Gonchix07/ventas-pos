using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Abm;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Administrador")]
public class EstructuraController : ControllerBase
{
    private readonly IEstructuraService _service;
    public EstructuraController(IEstructuraService service) => _service = service;

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
