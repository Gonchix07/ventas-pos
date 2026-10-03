using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Common;
using Pos.Application.Articulos;
using Pos.Application.Catalogo;
using Pos.Application.Common;

namespace Pos.Api.Controllers.Admin;

[ApiController]
[Route("api/v1/admin/articulos")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class ArticulosController : ControllerBase
{
    private readonly IArticuloService _service;
    private readonly IArticuloDbfImportService _importDbf;

    public ArticulosController(IArticuloService service, IArticuloDbfImportService importDbf)
    {
        _service = service;
        _importDbf = importDbf;
    }

    // ---- Comparador de diferencias / import desde articulo.dbf + cbarras.dbf (app legacy VFP) ----

    /// <summary>Artículos de articulo.dbf que todavía no están en SQL (por CodigoInterno). Solo
    /// lectura — vista previa antes de "Importar".</summary>
    [HttpGet("nuevos-dbf")]
    public async Task<IActionResult> GetNuevosDbf(CancellationToken ct) =>
        Ok(ApiResult<ComparadorArticulosDto>.Success(await _importDbf.ObtenerNuevosAsync(ct)));

    /// <summary>Crea los artículos nuevos con presentaciones y códigos de barra (nunca toca los que ya existen).</summary>
    [HttpPost("nuevos-dbf/importar")]
    public async Task<IActionResult> ImportarNuevosDbf(CancellationToken ct) =>
        Ok(ApiResult<ImportacionArticulosResultado>.Success(await _importDbf.ImportarNuevosAsync(ct)));

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? texto, [FromQuery] int? idSector,
        [FromQuery] int? idLinea, [FromQuery] int? idFamilia, [FromQuery] bool? activo,
        [FromQuery] int? max, CancellationToken ct) =>
        Ok(ApiResult<IReadOnlyList<ArticuloListItem>>.Success(
            await _service.GetAllAsync(new ArticuloFiltro(texto, idSector, idLinea, idFamilia, activo, max), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var a = await _service.GetByIdAsync(id, ct);
        return a is null
            ? NotFound(ApiResult<ArticuloDetail>.Fail("NO_ENCONTRADO", "No existe el artículo."))
            : Ok(ApiResult<ArticuloDetail>.Success(a));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ArticuloInput input, CancellationToken ct)
    {
        var id = await _service.CreateAsync(input, ct);
        return Ok(ApiResult<int>.Success(id));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ArticuloInput input, CancellationToken ct)
    {
        var ok = await _service.UpdateAsync(id, input, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el artículo."));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? Ok(ApiResult<bool>.Success(true))
                  : NotFound(ApiResult<bool>.Fail("NO_ENCONTRADO", "No existe el artículo."));
    }
}
