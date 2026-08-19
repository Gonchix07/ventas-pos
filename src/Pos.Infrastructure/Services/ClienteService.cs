using Microsoft.EntityFrameworkCore;
using Pos.Application.Clientes;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class ClienteService : IClienteService
{
    public const int MaxResultados = 50;

    private readonly PosDbContext _db;
    public ClienteService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<ClienteDto>> GetAllAsync(string? filtro, bool? soloCuentaCorriente = null,
        CancellationToken ct = default)
    {
        var q = _db.Clientes.AsNoTracking()
            .Include(c => c.CondicionIva)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = filtro.Trim();
            // También por nombre de fantasía: en el mostrador al cliente se lo conoce por ahí
            // ("LA VACA LOCA") mucho más que por su razón social.
            q = q.Where(c => c.Descripcion.Contains(f) || c.CodigoInt.Contains(f)
                || (c.NombreFantasia != null && c.NombreFantasia.Contains(f))
                || (c.Cuit != null && c.Cuit.Contains(f))
                || (c.Documento != null && c.Documento.Contains(f)));
        }

        if (soloCuentaCorriente == true) q = q.Where(c => c.AdmiteCuentaCorriente);

        // Tope de 50: la lista es para buscar y editar, no para volcar el padrón completo.
        var clientes = await q.OrderBy(c => c.Descripcion).Take(MaxResultados).ToListAsync(ct);
        return clientes.Select(Map).ToList();
    }

    public async Task<ClienteDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var c = await _db.Clientes.AsNoTracking().Include(x => x.CondicionIva).Include(x => x.Autorizados)
            .FirstOrDefaultAsync(x => x.IdCliente == id, ct);
        if (c is null) return null;
        return Map(c) with
        {
            Autorizados = c.Autorizados.OrderBy(a => a.Descripcion)
                .Select(a => new AutorizadoDto(a.IdAutorizado, a.Dni, a.Descripcion, a.FechaAlta, a.Activo))
                .ToList()
        };
    }

    public async Task<int> CreateAsync(ClienteInput input, CancellationToken ct = default)
    {
        if (await _db.Clientes.AnyAsync(c => c.CodigoInt == input.CodigoInt, ct))
            throw new DomainException("CODIGO_DUPLICADO", $"Ya existe un cliente con código {input.CodigoInt}.");

        var autorizados = ValidarAutorizados(input.Autorizados);

        var cliente = new Cliente
        {
            CodigoInt = input.CodigoInt.Trim(),
            Cuit = string.IsNullOrWhiteSpace(input.Cuit) ? null : input.Cuit.Trim(),
            Documento = string.IsNullOrWhiteSpace(input.Documento) ? null : input.Documento.Trim(),
            Descripcion = input.Descripcion.Trim(),
            NombreFantasia = Limpiar(input.NombreFantasia),
            IdCondIva = input.IdCondIva,
            PermitePresupuesto = input.PermitePresupuesto,
            AdmiteCuentaCorriente = input.AdmiteCuentaCorriente,
            Activo = input.Activo,
            Domicilio = Limpiar(input.Domicilio),
            CodigoPostal = Limpiar(input.CodigoPostal),
            Localidad = Limpiar(input.Localidad),
            Provincia = Limpiar(input.Provincia),
            Email = Limpiar(input.Email)
        };
        // Los autorizados van por navegación: el IdCliente todavía no existe.
        foreach (var a in autorizados)
            cliente.Autorizados.Add(new Autorizado
            {
                Dni = a.Dni, Descripcion = a.Descripcion,
                FechaAlta = a.FechaAlta ?? DateTime.Today, Activo = a.Activo
            });

        _db.Clientes.Add(cliente);
        await _db.SaveChangesAsync(ct);
        return cliente.IdCliente;
    }

    public async Task<bool> UpdateAsync(int id, ClienteInput input, CancellationToken ct = default)
    {
        var cliente = await _db.Clientes.Include(c => c.Autorizados)
            .FirstOrDefaultAsync(c => c.IdCliente == id, ct);
        if (cliente is null) return false;

        var autorizados = ValidarAutorizados(input.Autorizados);

        cliente.CodigoInt = input.CodigoInt.Trim();
        cliente.Cuit = string.IsNullOrWhiteSpace(input.Cuit) ? null : input.Cuit.Trim();
        cliente.Documento = string.IsNullOrWhiteSpace(input.Documento) ? null : input.Documento.Trim();
        cliente.Descripcion = input.Descripcion.Trim();
        cliente.NombreFantasia = Limpiar(input.NombreFantasia);
        cliente.IdCondIva = input.IdCondIva;
        cliente.PermitePresupuesto = input.PermitePresupuesto;
        cliente.AdmiteCuentaCorriente = input.AdmiteCuentaCorriente;
        cliente.Activo = input.Activo;
        cliente.Domicilio = Limpiar(input.Domicilio);
        cliente.CodigoPostal = Limpiar(input.CodigoPostal);
        cliente.Localidad = Limpiar(input.Localidad);
        cliente.Provincia = Limpiar(input.Provincia);
        cliente.Email = Limpiar(input.Email);

        // Merge por IdAutorizado (no borrar-y-recrear): así los autorizados conservan su id y su
        // fecha de alta original aunque se edite cualquier otro dato del cliente.
        if (input.Autorizados is not null)
        {
            var vigentes = autorizados.Where(a => a.IdAutorizado is > 0).Select(a => a.IdAutorizado!.Value).ToHashSet();
            foreach (var baja in cliente.Autorizados.Where(a => !vigentes.Contains(a.IdAutorizado)).ToList())
            {
                cliente.Autorizados.Remove(baja);
                _db.Autorizados.Remove(baja);
            }

            foreach (var a in autorizados)
            {
                var actual = a.IdAutorizado is > 0
                    ? cliente.Autorizados.FirstOrDefault(x => x.IdAutorizado == a.IdAutorizado)
                    : null;
                if (actual is null)
                    cliente.Autorizados.Add(new Autorizado
                    {
                        Dni = a.Dni, Descripcion = a.Descripcion,
                        FechaAlta = a.FechaAlta ?? DateTime.Today, Activo = a.Activo
                    });
                else
                {
                    actual.Dni = a.Dni;
                    actual.Descripcion = a.Descripcion;
                    if (a.FechaAlta is DateTime f) actual.FechaAlta = f;
                    actual.Activo = a.Activo;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Normaliza y valida la lista de autorizados (DNI y nombre obligatorios, sin DNI repetido).</summary>
    private static List<AutorizadoInput> ValidarAutorizados(List<AutorizadoInput>? input)
    {
        if (input is null || input.Count == 0) return new List<AutorizadoInput>();

        var limpios = input.Select(a => a with { Dni = (a.Dni ?? "").Trim(), Descripcion = (a.Descripcion ?? "").Trim() }).ToList();

        if (limpios.Any(a => a.Dni.Length == 0))
            throw new DomainException("DNI_REQUERIDO", "Cada autorizado necesita su DNI.");
        if (limpios.Any(a => a.Descripcion.Length == 0))
            throw new DomainException("NOMBRE_REQUERIDO", "Cada autorizado necesita su nombre completo.");
        if (limpios.Select(a => a.Dni).Distinct(StringComparer.OrdinalIgnoreCase).Count() != limpios.Count)
            throw new DomainException("DNI_DUPLICADO", "Hay un DNI repetido en la lista de autorizados.");

        return limpios;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.IdCliente == id, ct);
        if (cliente is null) return false;
        cliente.Activo = false; // baja lógica
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static ClienteDto Map(Cliente c) => new(
        c.IdCliente, c.CodigoInt, c.Cuit, c.Documento, c.Descripcion, c.NombreFantasia,
        c.IdCondIva, c.CondicionIva?.Descripcion, c.PermitePresupuesto, c.AdmiteCuentaCorriente, c.Activo,
        c.Domicilio, c.CodigoPostal, c.Localidad, c.Provincia, c.Email);
}
