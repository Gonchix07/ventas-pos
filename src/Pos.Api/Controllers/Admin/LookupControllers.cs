using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Abm;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Domain.Entities;

namespace Pos.Api.Controllers.Admin;

[Route("api/v1/admin/sectores")]
public class SectoresController : LookupController<Sector>
{
    public SectoresController(ICrudService<Sector> crud) : base(crud) { }
}

[Route("api/v1/admin/lineas")]
public class LineasController : LookupController<Linea>
{
    public LineasController(ICrudService<Linea> crud) : base(crud) { }
}

// Familias NO usa el CRUD genérico de lookup: cuelga de un sector (ver FamiliasController).

// Los tipos de oferta son fijos: cada uno tiene un comportamiento programado en el motor, así que
// solo se listan para poblar el combo de Acción en el ABM de Ofertas. Sin alta/edición/baja.
[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/v1/admin/tipos-oferta")]
public class TiposOfertaController : ControllerBase
{
    private readonly ICrudService<TipoOferta> _crud;
    public TiposOfertaController(ICrudService<TipoOferta> crud) => _crud = crud;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        // Los legacy (Seleccionable = false) siguen existiendo para las ofertas ya cargadas,
        // pero no se ofrecen para armar una nueva.
        var items = (await _crud.GetAllAsync(ct))
            .Where(e => e.Seleccionable)
            .Select(e => new TipoOfertaDto(e.Id, e.Descripcion, e.Codigo))
            .OrderBy(x => x.Codigo)
            .ToList();
        return Ok(ApiResult<IReadOnlyList<TipoOfertaDto>>.Success(items));
    }
}

[Route("api/v1/admin/motivos-diferencia")]
public class MotivosDiferenciaController : LookupController<MotivoDiferencia>
{
    public MotivosDiferenciaController(ICrudService<MotivoDiferencia> crud) : base(crud) { }
}

[Route("api/v1/admin/motivos-cierre")]
public class MotivosCierreController : LookupController<MotivoCierre>
{
    public MotivosCierreController(ICrudService<MotivoCierre> crud) : base(crud) { }
}
