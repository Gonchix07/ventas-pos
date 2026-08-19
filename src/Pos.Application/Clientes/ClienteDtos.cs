namespace Pos.Application.Clientes;

/// <summary>Persona autorizada a comprar en nombre del cliente.</summary>
public record AutorizadoDto(int IdAutorizado, string Dni, string Descripcion, DateTime FechaAlta, bool Activo);

/// <summary>
/// Alta/edición de un autorizado dentro del cliente. <paramref name="IdAutorizado"/> nulo = nuevo;
/// los que no vengan en la lista se borran.
/// </summary>
public record AutorizadoInput(int? IdAutorizado, string Dni, string Descripcion, DateTime? FechaAlta, bool Activo);

public record ClienteDto(
    int IdCliente, string CodigoInt, string? Cuit, string? Documento,
    string Descripcion, string? NombreFantasia, int IdCondIva, string? CondIvaDescripcion,
    bool PermitePresupuesto, bool AdmiteCuentaCorriente, bool Activo,
    string? Domicilio, string? CodigoPostal, string? Localidad, string? Provincia, string? Email,
    // Solo viene en el detalle (GetById); el listado no los trae por peso.
    List<AutorizadoDto>? Autorizados = null);

public record ClienteInput(
    string CodigoInt, string? Cuit, string? Documento,
    string Descripcion, string? NombreFantasia, int IdCondIva, bool PermitePresupuesto,
    bool AdmiteCuentaCorriente, bool Activo,
    string? Domicilio, string? CodigoPostal, string? Localidad, string? Provincia, string? Email,
    List<AutorizadoInput>? Autorizados = null);

public interface IClienteService
{
    /// <param name="soloCuentaCorriente">true = solo los que admiten cuenta corriente.</param>
    Task<IReadOnlyList<ClienteDto>> GetAllAsync(string? filtro, bool? soloCuentaCorriente = null,
        CancellationToken ct = default);
    Task<ClienteDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(ClienteInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, ClienteInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
