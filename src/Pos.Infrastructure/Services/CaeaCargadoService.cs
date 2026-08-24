using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Facturacion;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// ABM del CAEA precargado a mano (ver <see cref="CaeaCargado"/>) — el valor en sí lo consigue un
/// administrador con conexión (FECAEASolicitar, por quincena) y lo carga acá; este servicio no le
/// pide nada a ARCA, solo guarda y consulta.
/// </summary>
public class CaeaCargadoService : ICaeaCargadoService
{
    private readonly PosDbContext _db;
    public CaeaCargadoService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<CaeaCargadoDto>> GetAsync(int? idEmpresa = null, CancellationToken ct = default)
    {
        var q = _db.CaeaCargados.AsNoTracking().AsQueryable();
        if (idEmpresa.HasValue) q = q.Where(c => c.IdEmpresa == idEmpresa.Value);

        var hoy = DateTime.UtcNow;
        return (await q.OrderByDescending(c => c.Anio).ThenByDescending(c => c.Mes).ThenByDescending(c => c.Orden)
                .ToListAsync(ct))
            .Select(c => new CaeaCargadoDto(c.IdCaea, c.IdEmpresa, c.Anio, c.Mes, c.Orden, c.Valor,
                c.VigenciaDesde, c.VigenciaHasta, CaeaReglas.Vigente(hoy, c.VigenciaDesde, c.VigenciaHasta)))
            .ToList();
    }

    public async Task<int> CreateAsync(CaeaCargadoInput input, CancellationToken ct = default)
    {
        Validar(input);
        if (await _db.CaeaCargados.AnyAsync(c => c.IdEmpresa == input.IdEmpresa && c.Anio == input.Anio
                && c.Mes == input.Mes && c.Orden == input.Orden, ct))
            throw new DomainException("CAEA_DUPLICADO",
                $"Ya hay un CAEA cargado para esta empresa en {input.Anio}-{input.Mes:D2} (quincena {input.Orden}).");

        var e = new CaeaCargado
        {
            IdEmpresa = input.IdEmpresa, Anio = input.Anio, Mes = input.Mes, Orden = input.Orden,
            Valor = input.Valor.Trim(), VigenciaDesde = input.VigenciaDesde.Date, VigenciaHasta = input.VigenciaHasta.Date
        };
        _db.CaeaCargados.Add(e);
        await _db.SaveChangesAsync(ct);
        return e.IdCaea;
    }

    public async Task<bool> UpdateAsync(int id, CaeaCargadoInput input, CancellationToken ct = default)
    {
        Validar(input);
        var e = await _db.CaeaCargados.FirstOrDefaultAsync(x => x.IdCaea == id, ct);
        if (e is null) return false;

        if (await _db.CaeaCargados.AnyAsync(c => c.IdCaea != id && c.IdEmpresa == input.IdEmpresa
                && c.Anio == input.Anio && c.Mes == input.Mes && c.Orden == input.Orden, ct))
            throw new DomainException("CAEA_DUPLICADO",
                $"Ya hay OTRO CAEA cargado para esta empresa en {input.Anio}-{input.Mes:D2} (quincena {input.Orden}).");

        e.IdEmpresa = input.IdEmpresa; e.Anio = input.Anio; e.Mes = input.Mes; e.Orden = input.Orden;
        e.Valor = input.Valor.Trim(); e.VigenciaDesde = input.VigenciaDesde.Date; e.VigenciaHasta = input.VigenciaHasta.Date;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.CaeaCargados.FirstOrDefaultAsync(x => x.IdCaea == id, ct);
        if (e is null) return false;
        _db.CaeaCargados.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CaeaCargadoDto?> BuscarVigenteAsync(int idEmpresa, DateTime fecha, CancellationToken ct = default)
    {
        // Se filtra por empresa+fecha en el cliente (no en SQL) porque CaeaReglas.Vigente es la
        // única fuente de verdad de "vigente" — una tabla con pocas filas por empresa, no vale la
        // pena duplicar la regla como where de EF.
        var candidatos = await _db.CaeaCargados.AsNoTracking()
            .Where(c => c.IdEmpresa == idEmpresa).ToListAsync(ct);
        var vigente = candidatos.FirstOrDefault(c => CaeaReglas.Vigente(fecha, c.VigenciaDesde, c.VigenciaHasta));
        return vigente is null ? null
            : new CaeaCargadoDto(vigente.IdCaea, vigente.IdEmpresa, vigente.Anio, vigente.Mes, vigente.Orden,
                vigente.Valor, vigente.VigenciaDesde, vigente.VigenciaHasta, true);
    }

    private static void Validar(CaeaCargadoInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Valor))
            throw new DomainException("VALOR_REQUERIDO", "El valor del CAEA es obligatorio.");
        if (input.Mes < 1 || input.Mes > 12)
            throw new DomainException("MES_INVALIDO", "El mes debe estar entre 1 y 12.");
        if (input.Orden != 1 && input.Orden != 2)
            throw new DomainException("ORDEN_INVALIDO", "La quincena debe ser 1 (del 1 al 15) o 2 (del 16 a fin de mes).");
        if (input.VigenciaHasta.Date < input.VigenciaDesde.Date)
            throw new DomainException("VIGENCIA_INVALIDA", "La vigencia hasta no puede ser anterior a la vigencia desde.");
    }
}
