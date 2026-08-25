using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Api.Common;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Api.Controllers.Admin;

/// <summary>Listas de referencia de solo lectura para poblar combos del ABM.</summary>
[ApiController]
[Route("api/v1/admin/referencias")]
[Authorize]
[ModuloAutorizado("Administracion", "Administrador")]
public class ReferenciasController : ControllerBase
{
    private readonly PosDbContext _db;
    public ReferenciasController(PosDbContext db) => _db = db;

    [HttpGet("modos-iva")]
    public async Task<IActionResult> ModosIva(CancellationToken ct)
    {
        var items = await _db.ModosIva.AsNoTracking()
            .OrderBy(m => m.Descripcion)
            .Select(m => new LookupDto(m.IdModoIva, m.Descripcion))
            .ToListAsync(ct);
        return Ok(ApiResult<IReadOnlyList<LookupDto>>.Success(items));
    }

    // Único lookup de este controller (Administrador-only por defecto) que también consumen
    // pantallas fuera del ABM: Reimpresión y Cupones habilitan Supervisor/Tesorero además de
    // Administrador (ver App.tsx) para poblar el combo de sucursal — sin este override esos roles
    // recibían 403 al pedir la lista y el combo quedaba vacío ("no puede ver la sucursal").
    [HttpGet("sucursales")]
    [Authorize]
    [ModuloAutorizado("Reimpresion,Tesoreria,Administracion", "Supervisor,Tesorero,Administrador")]
    public async Task<IActionResult> Sucursales(CancellationToken ct)
    {
        var items = await _db.Sucursales.AsNoTracking()
            .OrderBy(s => s.Descripcion)
            .Select(s => new LookupDto(s.IdSucursal, s.Descripcion))
            .ToListAsync(ct);
        return Ok(ApiResult<IReadOnlyList<LookupDto>>.Success(items));
    }

    [HttpGet("listas-precios")]
    public async Task<IActionResult> ListasPrecios(CancellationToken ct)
    {
        var items = await _db.ListasPrecios.AsNoTracking()
            .OrderBy(l => l.CodigoInterno)
            .Select(l => new LookupDto(l.IdListaPrecio, l.CodigoInterno))
            .ToListAsync(ct);
        return Ok(ApiResult<IReadOnlyList<LookupDto>>.Success(items));
    }

    [HttpGet("condiciones-iva")]
    public async Task<IActionResult> CondicionesIva(CancellationToken ct)
    {
        var items = await _db.CondicionesIva.AsNoTracking()
            .OrderBy(c => c.Descripcion)
            .Select(c => new LookupDto(c.IdCondIva, c.Descripcion))
            .ToListAsync(ct);
        return Ok(ApiResult<IReadOnlyList<LookupDto>>.Success(items));
    }
}
