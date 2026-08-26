using Microsoft.EntityFrameworkCore;
using Pos.Application.Abm;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class CajaEstructuraService : ICajaEstructuraService
{
    private readonly PosDbContext _db;
    public CajaEstructuraService(PosDbContext db) => _db = db;

    // ---- Tipos de Punto de Venta (catálogo FIJO: no hay alta ni baja) ----

    /// <summary>
    /// Devuelve los 3 tipos fijos de la sucursal. Si faltan (sucursal nueva, o creada por fuera del
    /// ABM) se dan de alta acá: como ya no existe el alta manual, sin esto una sucursal se quedaría
    /// sin tipos y no se le podría crear ningún punto de venta. Es idempotente y son 3 filas.
    /// </summary>
    public async Task<IReadOnlyList<TipoPuntoVentaDto>> GetTiposPvAsync(int idSucursal, CancellationToken ct = default)
    {
        var existentes = await _db.TiposPuntoVenta.Where(t => t.IdSucursal == idSucursal).ToListAsync(ct);

        var faltantes = TiposPuntoVentaFijos.Todos.Where(d => existentes.All(e => e.IdTipoPuntoVenta != d.Id)).ToList();
        if (faltantes.Count > 0)
        {
            foreach (var d in faltantes)
            {
                var nuevo = new TipoPuntoVenta
                {
                    IdSucursal = idSucursal, IdTipoPuntoVenta = d.Id,
                    Descripcion = d.Descripcion, TipoArca = d.TipoArca
                };
                _db.TiposPuntoVenta.Add(nuevo);
                existentes.Add(nuevo);
            }
            await _db.SaveChangesAsync(ct);
        }

        // Se listan según el catálogo fijo (y no lo que haya en la tabla) para que el orden y los
        // textos sean siempre los mismos, incluso si alguna fila vieja quedó con otra descripción.
        return TiposPuntoVentaFijos.Todos
            .Select(d => new TipoPuntoVentaDto(idSucursal, d.Id, d.Descripcion, d.TipoArca, d.Detalle,
                TiposPuntoVentaFijos.RequiereIpControlador(d.Id)))
            .ToList();
    }

    // ---- Puntos de Venta ----
    public async Task<IReadOnlyList<PuntoVentaDto>> GetPuntosVentaAsync(int idSucursal, CancellationToken ct = default)
    {
        var query =
            from p in _db.PuntosVenta.AsNoTracking().Where(x => x.IdSucursal == idSucursal)
            join t in _db.TiposPuntoVenta.AsNoTracking()
                on new { p.IdSucursal, p.IdTipoPuntoVenta } equals new { t.IdSucursal, t.IdTipoPuntoVenta } into tj
            from t in tj.DefaultIfEmpty()
            orderby p.IdPuntoVenta
            select new PuntoVentaDto(p.IdSucursal, p.IdPuntoVenta, p.IdTipoPuntoVenta,
                t != null ? t.Descripcion : null, p.NumeroPuntoVenta, p.IpControlador);
        return await query.ToListAsync(ct);
    }

    public async Task<int> CreatePuntoVentaAsync(int idSucursal, PuntoVentaInput input, CancellationToken ct = default)
    {
        var ip = ValidarPuntoVenta(input);
        // Asegura que existan los tipos fijos antes de referenciar uno (FK).
        await GetTiposPvAsync(idSucursal, ct);

        var next = await NextIdAsync(_db.PuntosVenta.Where(p => p.IdSucursal == idSucursal).Select(p => p.IdPuntoVenta), ct);
        _db.PuntosVenta.Add(new PuntoVenta
        {
            IdSucursal = idSucursal, IdPuntoVenta = next, IdTipoPuntoVenta = input.IdTipoPuntoVenta,
            NumeroPuntoVenta = input.NumeroPuntoVenta, IpControlador = ip
        });
        await _db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<bool> UpdatePuntoVentaAsync(int idSucursal, int id, PuntoVentaInput input, CancellationToken ct = default)
    {
        var ip = ValidarPuntoVenta(input);
        var pv = await _db.PuntosVenta.FirstOrDefaultAsync(p => p.IdSucursal == idSucursal && p.IdPuntoVenta == id, ct);
        if (pv is null) return false;

        pv.IdTipoPuntoVenta = input.IdTipoPuntoVenta;
        pv.NumeroPuntoVenta = input.NumeroPuntoVenta;
        pv.IpControlador = ip;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Valida el tipo y resuelve la IP del controlador: obligatoria y bien formada en FISCAL (habla
    /// con la impresora por red), y descartada en los otros dos (imprimen en la comandera local, no
    /// hay controlador al que apuntar).
    /// </summary>
    private static string? ValidarPuntoVenta(PuntoVentaInput input)
    {
        if (TiposPuntoVentaFijos.Buscar(input.IdTipoPuntoVenta) is null)
            throw new DomainException("TIPO_PUNTO_VENTA_INVALIDO",
                "El tipo de punto de venta debe ser ELECTRONICA, FISCAL o PRESUPUESTO.");
        if (input.NumeroPuntoVenta <= 0)
            throw new DomainException("NUMERO_INVALIDO", "El número de punto de venta debe ser mayor a cero.");

        if (!TiposPuntoVentaFijos.RequiereIpControlador(input.IdTipoPuntoVenta)) return null;

        var ip = (input.IpControlador ?? "").Trim();
        if (ip.Length == 0)
            throw new DomainException("IP_CONTROLADOR_REQUERIDA",
                "El punto de venta FISCAL necesita la IP del controlador fiscal.");
        if (!System.Net.IPAddress.TryParse(ip, out _))
            throw new DomainException("IP_CONTROLADOR_INVALIDA", $"«{ip}» no es una dirección IP válida.");
        return ip;
    }

    public async Task<bool> DeletePuntoVentaAsync(int idSucursal, int id, CancellationToken ct = default)
    {
        if (await _db.Cajas.AnyAsync(c => c.IdSucursal == idSucursal && c.IdPuntoVenta == id, ct))
            throw new DomainException("EN_USO", "El punto de venta tiene cajas asociadas.");
        var e = await _db.PuntosVenta.FirstOrDefaultAsync(p => p.IdSucursal == idSucursal && p.IdPuntoVenta == id, ct);
        if (e is null) return false;
        _db.PuntosVenta.Remove(e); await _db.SaveChangesAsync(ct); return true;
    }

    // ---- Puestos ----
    public async Task<IReadOnlyList<PuestoDto>> GetPuestosAsync(int idSucursal, CancellationToken ct = default) =>
        (await _db.PuestosCaja.AsNoTracking().Where(p => p.IdSucursal == idSucursal)
            .OrderBy(p => p.IdPuestoAsignado).ToListAsync(ct))
            .Select(p => new PuestoDto(p.IdSucursal, p.IdPuestoAsignado, p.NombrePc, p.IdentificadorEquipo, p.Ip)).ToList();

    public async Task<int> CreatePuestoAsync(int idSucursal, PuestoInput input, CancellationToken ct = default)
    {
        var next = await NextIdAsync(_db.PuestosCaja.Where(p => p.IdSucursal == idSucursal).Select(p => p.IdPuestoAsignado), ct);
        _db.PuestosCaja.Add(new PuestoCaja
        {
            IdSucursal = idSucursal, IdPuestoAsignado = next,
            NombrePc = input.NombrePc.Trim(), Ip = string.IsNullOrWhiteSpace(input.Ip) ? null : input.Ip.Trim(),
        });
        await _db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<bool> UpdatePuestoAsync(int idSucursal, int id, PuestoInput input, CancellationToken ct = default)
    {
        var p = await _db.PuestosCaja.FirstOrDefaultAsync(x => x.IdSucursal == idSucursal && x.IdPuestoAsignado == id, ct);
        if (p is null) return false;
        p.NombrePc = input.NombrePc.Trim();
        p.Ip = string.IsNullOrWhiteSpace(input.Ip) ? null : input.Ip.Trim();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeletePuestoAsync(int idSucursal, int id, CancellationToken ct = default)
    {
        var e = await _db.PuestosCaja.FirstOrDefaultAsync(p => p.IdSucursal == idSucursal && p.IdPuestoAsignado == id, ct);
        if (e is null) return false;
        _db.PuestosCaja.Remove(e); await _db.SaveChangesAsync(ct); return true;
    }

    public async Task<bool> VincularEquipoAsync(int idSucursal, int id, string identificadorEquipo, CancellationToken ct = default)
    {
        var p = await _db.PuestosCaja.FirstOrDefaultAsync(x => x.IdSucursal == idSucursal && x.IdPuestoAsignado == id, ct);
        if (p is null) return false;

        // El índice único de abajo (IsUnique en PosDbContext) es la garantía real; esto solo
        // adelanta un mensaje de negocio legible en vez de dejar reventar la excepción de SQL.
        var yaUsadoPorOtro = await _db.PuestosCaja.AsNoTracking().AnyAsync(x =>
            x.IdentificadorEquipo == identificadorEquipo &&
            (x.IdSucursal != idSucursal || x.IdPuestoAsignado != id), ct);
        if (yaUsadoPorOtro)
            throw new DomainException("EQUIPO_YA_VINCULADO",
                "Este equipo ya está vinculado a otro puesto. Desvinculalo ahí primero si lo estás reemplazando.");

        p.IdentificadorEquipo = identificadorEquipo;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Cajas ----
    public async Task<IReadOnlyList<CajaDto>> GetCajasAsync(int idSucursal, CancellationToken ct = default)
    {
        var query =
            from c in _db.Cajas.AsNoTracking().Where(x => x.IdSucursal == idSucursal)
            join p in _db.PuestosCaja.AsNoTracking()
                on new { c.IdSucursal, Id = c.IdPuestoAsignado ?? -1 } equals new { p.IdSucursal, Id = p.IdPuestoAsignado } into pj
            from p in pj.DefaultIfEmpty()
            orderby c.IdCaja
            select new CajaDto(c.IdSucursal, c.IdCaja, c.IdPuntoVenta, c.Descripcion, c.IdPuestoAsignado,
                p != null ? p.NombrePc : null, p != null ? p.Ip : null, c.AdmitePresupuesto);
        return await query.ToListAsync(ct);
    }

    public async Task<int> CreateCajaAsync(int idSucursal, CajaInput input, CancellationToken ct = default)
    {
        await ValidarPuntoVentaDeCajaAsync(idSucursal, input.IdPuntoVenta, ct);
        var next = await NextIdAsync(_db.Cajas.Where(c => c.IdSucursal == idSucursal).Select(c => c.IdCaja), ct);
        _db.Cajas.Add(new Caja { IdSucursal = idSucursal, IdCaja = next, IdPuntoVenta = input.IdPuntoVenta,
            Descripcion = input.Descripcion.Trim(), IdPuestoAsignado = input.IdPuestoAsignado,
            AdmitePresupuesto = input.AdmitePresupuesto });
        await _db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<bool> UpdateCajaAsync(int idSucursal, int idCaja, CajaInput input, CancellationToken ct = default)
    {
        await ValidarPuntoVentaDeCajaAsync(idSucursal, input.IdPuntoVenta, ct);
        var c = await _db.Cajas.FirstOrDefaultAsync(x => x.IdSucursal == idSucursal && x.IdCaja == idCaja, ct);
        if (c is null) return false;
        c.IdPuntoVenta = input.IdPuntoVenta;
        c.Descripcion = input.Descripcion.Trim();
        c.IdPuestoAsignado = input.IdPuestoAsignado;
        c.AdmitePresupuesto = input.AdmitePresupuesto;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Una caja solo puede tener DOS modos de facturación habilitados a la vez: el de su punto de
    /// venta asignado (Fiscal o Electrónica) y Presupuesto — pero este último NO se asigna por
    /// caja, está siempre disponible en toda la sucursal (ver <c>FacturacionService</c>, que lo
    /// resuelve solo). Por eso el único punto que hay que cuidar acá es que a una caja nunca se le
    /// asigne DIRECTAMENTE el punto de venta de tipo Presupuesto como si fuera su modo principal:
    /// eso la dejaría sin Fiscal/Electrónica (rompe la venta normal) y es redundante (Presupuesto ya
    /// le llega igual). "Nunca Fiscal y Electrónica a la vez" queda garantizado solo por el modelo:
    /// <c>Caja.IdPuntoVenta</c> es una columna única, no admite dos valores.
    /// </summary>
    private async Task ValidarPuntoVentaDeCajaAsync(int idSucursal, int idPuntoVenta, CancellationToken ct)
    {
        var pv = await _db.PuntosVenta.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdSucursal == idSucursal && p.IdPuntoVenta == idPuntoVenta, ct)
            ?? throw new DomainException("PUNTO_VENTA_INEXISTENTE", "El punto de venta no existe en la sucursal.");
        if (pv.IdTipoPuntoVenta == (int)ModalidadPuntoVenta.Presupuesto)
            throw new DomainException("CAJA_NO_ADMITE_PRESUPUESTO_COMO_PRINCIPAL",
                "Una caja no puede asignarse al punto de venta de tipo Presupuesto: el Presupuesto ya " +
                "está disponible en todas las cajas de la sucursal automáticamente. Elegí el punto de " +
                "venta Fiscal o Electrónica de esta caja.");
    }

    public async Task<bool> DeleteCajaAsync(int idSucursal, int id, CancellationToken ct = default)
    {
        var e = await _db.Cajas.FirstOrDefaultAsync(c => c.IdSucursal == idSucursal && c.IdCaja == id, ct);
        if (e is null) return false;
        _db.Cajas.Remove(e); await _db.SaveChangesAsync(ct); return true;
    }

    private static async Task<int> NextIdAsync(IQueryable<int> ids, CancellationToken ct)
    {
        var max = await ids.MaxAsync(x => (int?)x, ct);
        return (max ?? 0) + 1;
    }

    // ---- Terminales de tarjeta ----
    private static string TipoTerminalDesc(TipoTerminalTarjeta t) => t switch
    {
        TipoTerminalTarjeta.FiServ => "FiServ",
        TipoTerminalTarjeta.PayWay => "PayWay",
        TipoTerminalTarjeta.PinPad => "PinPad",
        _ => t.ToString()
    };

    public async Task<IReadOnlyList<TerminalTarjetaDto>> GetTerminalesAsync(int idSucursal, CancellationToken ct = default)
    {
        var query =
            from t in _db.TerminalesTarjeta.AsNoTracking().Where(x => x.IdSucursal == idSucursal)
            join c in _db.Cajas.AsNoTracking()
                on new { t.IdSucursal, Id = t.IdCajaAsignada ?? -1 } equals new { c.IdSucursal, Id = c.IdCaja } into cj
            from c in cj.DefaultIfEmpty()
            orderby t.IdTerminal
            select new TerminalTarjetaDto(t.IdSucursal, t.IdTerminal, t.NumeroTerminal, (int)t.Tipo, "",
                t.IdCajaAsignada, c != null ? c.Descripcion : null);
        // TipoDescripcion se completa en memoria: el switch de TipoTerminalDesc no traduce a SQL.
        return (await query.ToListAsync(ct))
            .Select(d => d with { TipoDescripcion = TipoTerminalDesc((TipoTerminalTarjeta)d.Tipo) })
            .ToList();
    }

    public async Task<int> CreateTerminalAsync(int idSucursal, TerminalTarjetaInput input, CancellationToken ct = default)
    {
        var tipo = await ValidarTerminalAsync(idSucursal, input, ct);
        var next = await NextIdAsync(_db.TerminalesTarjeta.Where(t => t.IdSucursal == idSucursal).Select(t => t.IdTerminal), ct);
        _db.TerminalesTarjeta.Add(new TerminalTarjeta
        {
            IdSucursal = idSucursal, IdTerminal = next,
            NumeroTerminal = input.NumeroTerminal.Trim(), Tipo = tipo, IdCajaAsignada = input.IdCajaAsignada,
        });
        await _db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<bool> UpdateTerminalAsync(int idSucursal, int id, TerminalTarjetaInput input, CancellationToken ct = default)
    {
        var tipo = await ValidarTerminalAsync(idSucursal, input, ct);
        var t = await _db.TerminalesTarjeta.FirstOrDefaultAsync(x => x.IdSucursal == idSucursal && x.IdTerminal == id, ct);
        if (t is null) return false;
        t.NumeroTerminal = input.NumeroTerminal.Trim();
        t.Tipo = tipo;
        t.IdCajaAsignada = input.IdCajaAsignada;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<TipoTerminalTarjeta> ValidarTerminalAsync(int idSucursal, TerminalTarjetaInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.NumeroTerminal))
            throw new DomainException("NUMERO_TERMINAL_REQUERIDO", "El número de terminal es obligatorio.");
        if (!Enum.IsDefined(typeof(TipoTerminalTarjeta), input.Tipo))
            throw new DomainException("TIPO_TERMINAL_INVALIDO", "El tipo de terminal debe ser FiServ, PayWay o PinPad.");
        if (input.IdCajaAsignada is { } idCaja &&
            !await _db.Cajas.AsNoTracking().AnyAsync(c => c.IdSucursal == idSucursal && c.IdCaja == idCaja, ct))
            throw new DomainException("CAJA_INEXISTENTE", "La caja no existe en la sucursal.");
        return (TipoTerminalTarjeta)input.Tipo;
    }

    public async Task<bool> DeleteTerminalAsync(int idSucursal, int id, CancellationToken ct = default)
    {
        var e = await _db.TerminalesTarjeta.FirstOrDefaultAsync(t => t.IdSucursal == idSucursal && t.IdTerminal == id, ct);
        if (e is null) return false;
        _db.TerminalesTarjeta.Remove(e); await _db.SaveChangesAsync(ct); return true;
    }
}
