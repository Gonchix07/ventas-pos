namespace Pos.Application.Abm;

/// <summary>
/// Tipo de punto de venta. Es un catálogo FIJO (ELECTRONICA / FISCAL / PRESUPUESTO, ver
/// TiposPuntoVentaFijos): no se dan de alta ni se borran, solo se listan.
/// </summary>
public record TipoPuntoVentaDto(int IdSucursal, int IdTipoPuntoVenta, string Descripcion, string? TipoArca,
    string Detalle, bool RequiereIpControlador);

public record PuntoVentaDto(int IdSucursal, int IdPuntoVenta, int IdTipoPuntoVenta, string? TipoDescripcion,
    int NumeroPuntoVenta, string? IpControlador);
/// <summary>IpControlador solo se guarda si el tipo es FISCAL; en los otros se ignora.</summary>
public record PuntoVentaInput(int IdTipoPuntoVenta, int NumeroPuntoVenta, string? IpControlador);

/// <summary>NombrePc es solo una etiqueta libre para el ABM (ej. "Caja 1 - Mostrador"). La caja se
/// resuelve al loguear por IdentificadorEquipo (null hasta que se vincula, ver
/// ICajaEstructuraService.VincularEquipoAsync) — Ip queda solo como dato informativo/auditoría,
/// ver AuthController/LoginCommand.</summary>
public record PuestoDto(int IdSucursal, int IdPuestoAsignado, string NombrePc, string? IdentificadorEquipo, string? Ip);
public record PuestoInput(string NombrePc, string? Ip);

public record CajaDto(int IdSucursal, int IdCaja, int IdPuntoVenta, string Descripcion, int? IdPuestoAsignado,
    string? NombrePc, string? Ip, bool AdmitePresupuesto);
/// <summary>
/// <c>AdmitePresupuesto</c>: si esta caja puede vender con modo Presupuesto. Se exige junto con
/// <c>Cliente.PermitePresupuesto</c> — las dos condiciones tienen que darse (ver FacturacionService).
/// </summary>
public record CajaInput(int IdPuntoVenta, string Descripcion, int? IdPuestoAsignado, bool AdmitePresupuesto);

/// <summary>
/// Tipo: FiServ / PayWay / PinPad — ver <c>TipoTerminalTarjeta</c>. IdCajaAsignada/CajaDescripcion
/// null = terminal sin asignar a ninguna caja todavía.
/// </summary>
public record TerminalTarjetaDto(int IdSucursal, int IdTerminal, string NumeroTerminal, int Tipo, string TipoDescripcion,
    int? IdCajaAsignada, string? CajaDescripcion);
public record TerminalTarjetaInput(string NumeroTerminal, int Tipo, int? IdCajaAsignada);

/// <summary>ABM de la estructura de caja por sucursal. Los IDs locales se autoasignan (max+1).</summary>
public interface ICajaEstructuraService
{
    /// <summary>Los 3 tipos fijos de la sucursal (se crean solos la primera vez que se consultan).</summary>
    Task<IReadOnlyList<TipoPuntoVentaDto>> GetTiposPvAsync(int idSucursal, CancellationToken ct = default);

    Task<IReadOnlyList<PuntoVentaDto>> GetPuntosVentaAsync(int idSucursal, CancellationToken ct = default);
    Task<int> CreatePuntoVentaAsync(int idSucursal, PuntoVentaInput input, CancellationToken ct = default);
    Task<bool> UpdatePuntoVentaAsync(int idSucursal, int id, PuntoVentaInput input, CancellationToken ct = default);
    Task<bool> DeletePuntoVentaAsync(int idSucursal, int id, CancellationToken ct = default);

    Task<IReadOnlyList<PuestoDto>> GetPuestosAsync(int idSucursal, CancellationToken ct = default);
    Task<int> CreatePuestoAsync(int idSucursal, PuestoInput input, CancellationToken ct = default);
    Task<bool> UpdatePuestoAsync(int idSucursal, int id, PuestoInput input, CancellationToken ct = default);
    Task<bool> DeletePuestoAsync(int idSucursal, int id, CancellationToken ct = default);
    /// <summary>
    /// Vincula este puesto al equipo desde el que se llama AHORA (el Administrador tiene que estar
    /// parado físicamente frente a esa PC) — toma <paramref name="identificadorEquipo"/> del header
    /// X-Puesto-Id del propio request. Pisa el vínculo anterior si ya tenía uno (reemplazo de PC).
    /// </summary>
    Task<bool> VincularEquipoAsync(int idSucursal, int id, string identificadorEquipo, CancellationToken ct = default);

    Task<IReadOnlyList<CajaDto>> GetCajasAsync(int idSucursal, CancellationToken ct = default);
    Task<int> CreateCajaAsync(int idSucursal, CajaInput input, CancellationToken ct = default);
    Task<bool> UpdateCajaAsync(int idSucursal, int idCaja, CajaInput input, CancellationToken ct = default);
    Task<bool> DeleteCajaAsync(int idSucursal, int id, CancellationToken ct = default);

    Task<IReadOnlyList<TerminalTarjetaDto>> GetTerminalesAsync(int idSucursal, CancellationToken ct = default);
    Task<int> CreateTerminalAsync(int idSucursal, TerminalTarjetaInput input, CancellationToken ct = default);
    Task<bool> UpdateTerminalAsync(int idSucursal, int id, TerminalTarjetaInput input, CancellationToken ct = default);
    Task<bool> DeleteTerminalAsync(int idSucursal, int id, CancellationToken ct = default);
}

// ---- Usuarios / Roles ----
public record RolDto(int IdRol, string Descripcion);
// CodigoSupervisor: el de 8 dígitos del control de supervisor (ver ISupervisorAuthService). Solo
// tiene sentido cargado en usuarios Supervisor/Administrador, pero no se restringe por rol acá —
// el que sí importa es el rol de a quién pertenece el código al momento de USARLO.
public record UsuarioDto(int IdUsuario, string NombreUsuario, int IdRol, string? Rol, bool Activo, string? CodigoSupervisor);
public record UsuarioCreateInput(string NombreUsuario, string Clave, int IdRol, bool Activo, string? CodigoSupervisor = null);
public record UsuarioUpdateInput(string NombreUsuario, int IdRol, bool Activo, string? CodigoSupervisor = null);
public record ResetClaveInput(string NuevaClave);

public interface IUsuarioAdminService
{
    Task<IReadOnlyList<RolDto>> GetRolesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UsuarioDto>> GetAllAsync(CancellationToken ct = default);
    Task<int> CreateAsync(UsuarioCreateInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UsuarioUpdateInput input, CancellationToken ct = default);
    Task<bool> ResetClaveAsync(int id, ResetClaveInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
