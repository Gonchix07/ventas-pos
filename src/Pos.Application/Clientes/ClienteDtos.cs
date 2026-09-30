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
    List<AutorizadoDto>? Autorizados = null,
    // Null si el cliente nunca sincronizó desde el ERP (cargado a mano en el ABM).
    DateTime? UltimaSincronizacionErpUtc = null);

public record ClienteInput(
    string CodigoInt, string? Cuit, string? Documento,
    string Descripcion, string? NombreFantasia, int IdCondIva, bool PermitePresupuesto,
    bool AdmiteCuentaCorriente, bool Activo,
    string? Domicilio, string? CodigoPostal, string? Localidad, string? Provincia, string? Email,
    List<AutorizadoInput>? Autorizados = null);

/// <summary>
/// Resultado de búsqueda para el módulo "Clientes" (ficha + ticket de comandera): a diferencia de
/// <see cref="Pos.Application.Caja.ClienteResumen"/> no depende de una sucursal (no hay convenio ni
/// precio de por medio, solo se busca e imprime al cliente) y solo trae la tarjeta VIGENTE.
/// </summary>
/// <param name="Origen">"Titular" si el DNI escaneado es el documento propio del cliente, o
/// "Autorizado" si el DNI pertenece a alguien autorizado a comprar en esa cuenta.</param>
public record ClienteTicketDto(int IdCliente, string CodigoInt, string Descripcion, string? Documento,
    string? NroTarjeta, string? TipoTarjeta, string Origen);

public interface IClienteService
{
    /// <param name="soloCuentaCorriente">true = solo los que admiten cuenta corriente.</param>
    Task<IReadOnlyList<ClienteDto>> GetAllAsync(string? filtro, bool? soloCuentaCorriente = null,
        CancellationToken ct = default);
    Task<ClienteDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(ClienteInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, ClienteInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Busca por DNI escaneado (QR del documento): trae la cuenta propia del cliente cuyo Documento
    /// coincide Y toda cuenta donde ese DNI figure como Autorizado activo — un mismo DNI puede
    /// aparecer en más de una cuenta (la propia + las que puede comprar en nombre de otro).
    /// </summary>
    Task<IReadOnlyList<ClienteTicketDto>> BuscarPorDniAsync(string dni, CancellationToken ct = default);

    /// <summary>
    /// Ticket de un cliente puntual (elegido por búsqueda manual, no por DNI escaneado) — mismo
    /// formato que <see cref="BuscarPorDniAsync"/> (para reusar el mismo <c>TicketCliente</c> del
    /// frontend), "Origen" siempre "Titular" porque acá no hay concepto de autorizado.
    /// </summary>
    Task<ClienteTicketDto?> GetTicketAsync(int idCliente, CancellationToken ct = default);
}

/// <summary>
/// Cliente encontrado en clientes.dbf (app legacy VFP "Mayorista") que todavía no existe en SQL por
/// CodigoInt — fila del "comparador de diferencias" antes de importar. La tarjeta (de codtarje.dbf)
/// es la vigente del cliente (TIPOTARJE '03'=Roja/'04'=Azul, INHABILITA=0), null si no tiene.
/// </summary>
public record ClienteNuevoDto(
    string CodigoInt, string Descripcion, string? NombreFantasia, string? Cuit, string? Documento,
    string CondIvaDescripcion, bool PermitePresupuesto, string? Localidad, string? Provincia,
    string? NroTarjeta, string? TipoTarjetaDescripcion);

public interface IClienteDbfImportService
{
    /// <summary>Solo lectura: compara clientes.dbf contra Clientes por CodigoInt, sin tocar SQL.</summary>
    Task<IReadOnlyList<ClienteNuevoDto>> ObtenerNuevosAsync(CancellationToken ct = default);

    /// <summary>Crea en SQL los clientes de clientes.dbf que todavía no existen (nunca actualiza ni
    /// borra los que ya están) y les asigna su tarjeta vigente de codtarje.dbf si tienen.</summary>
    /// <returns>Cantidad de clientes creados.</returns>
    Task<int> ImportarNuevosAsync(CancellationToken ct = default);
}
