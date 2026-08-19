namespace Pos.Application.Abm;

// ---- Convenios (por sucursal) ----
public record ConvenioDto(int IdSucursal, int IdConvenio, int IdCliente, string? ClienteDescripcion,
    decimal Descuento, int? IdListaPrecio, string? ListaCodigo);
public record ConvenioInput(int IdCliente, decimal Descuento, int? IdListaPrecio);

public interface IConvenioService
{
    Task<IReadOnlyList<ConvenioDto>> GetAsync(int idSucursal, CancellationToken ct = default);
    Task<int> CreateAsync(int idSucursal, ConvenioInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int idSucursal, int idConvenio, ConvenioInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int idSucursal, int idConvenio, CancellationToken ct = default);
}

// ---- Clusters de clientes ----
public record ClusterDto(int IdCluster, string Descripcion, int CantidadClientes);
public record ClusterMiembroDto(int IdCliente, string ClienteDescripcion, string CodigoInt);
/// <summary>Alta de cluster: solo el nombre. El cluster puede existir sin miembros.</summary>
public record ClusterInput(string Descripcion);
public record ClusterMiembroInput(int IdCliente);
/// <summary>Reemplaza el set completo de miembros del cluster en una sola operación.</summary>
public record ClusterMiembrosSetInput(List<int> IdsClientes);

/// <summary>Resultado de un guardado masivo de miembros (para informar qué cambió).</summary>
public record ClusterMiembrosResultado(int Agregados, int Quitados, int Total);

public interface IClusterService
{
    Task<IReadOnlyList<ClusterDto>> GetClustersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ClusterMiembroDto>> GetMiembrosAsync(int idCluster, CancellationToken ct = default);
    Task<int> CreateClusterAsync(ClusterInput input, CancellationToken ct = default);
    Task<bool> RenameClusterAsync(int idCluster, ClusterInput input, CancellationToken ct = default);
    Task<bool> AddMiembroAsync(int idCluster, ClusterMiembroInput input, CancellationToken ct = default);
    Task<bool> RemoveMiembroAsync(int idCluster, int idCliente, CancellationToken ct = default);
    /// <summary>Guardado en lote: deja como miembros exactamente los clientes indicados.</summary>
    Task<ClusterMiembrosResultado?> SetMiembrosAsync(int idCluster, ClusterMiembrosSetInput input, CancellationToken ct = default);
    Task<bool> DeleteClusterAsync(int idCluster, CancellationToken ct = default);
}

// ---- Tarjetas ----
public record TipoTarjetaDto(int IdTipoTarjeta, string Descripcion, int? IdListaPrecio, string? ListaCodigo);
public record TipoTarjetaInput(string Descripcion, int? IdListaPrecio);

public record TarjetaClienteDto(int IdCliente, int IdTipoTarjeta, string? TipoDescripcion, string NroTarjeta,
    bool Activa, DateTime? FechaBajaUtc);
public record TarjetaClienteInput(int IdTipoTarjeta, string NroTarjeta);

/// <summary>
/// Resultado del alta de tarjeta. Como el cliente tiene UNA sola tarjeta vigente, el alta puede
/// haber anulado la anterior: se informa cuál, para poder avisarlo en pantalla.
/// </summary>
public record AltaTarjetaResultado(bool Ok, int Anuladas, string? NroAnulada, string? TipoAnulada);

public interface ITarjetaAdminService
{
    Task<IReadOnlyList<TipoTarjetaDto>> GetTiposAsync(CancellationToken ct = default);
    Task<int> CreateTipoAsync(TipoTarjetaInput input, CancellationToken ct = default);
    Task<bool> UpdateTipoAsync(int id, TipoTarjetaInput input, CancellationToken ct = default);
    Task<bool> DeleteTipoAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<TarjetaClienteDto>> GetTarjetasAsync(int idCliente, CancellationToken ct = default);
    Task<AltaTarjetaResultado> AddTarjetaAsync(int idCliente, TarjetaClienteInput input, CancellationToken ct = default);
    Task<bool> RemoveTarjetaAsync(int idCliente, int idTipoTarjeta, string nroTarjeta, CancellationToken ct = default);
}

// ---- Cuenta corriente (límite de crédito por cliente/sucursal) ----
public record CuentaCorrienteLimiteDto(int IdSucursal, int IdCliente, string ClienteDescripcion,
    decimal LimiteCredito, decimal SaldoActual);
public record CuentaCorrienteLimiteInput(decimal LimiteCredito);

public interface IClienteEnCuentaService
{
    Task<IReadOnlyList<CuentaCorrienteLimiteDto>> GetAsync(int idSucursal, CancellationToken ct = default);
    Task UpsertAsync(int idSucursal, int idCliente, CuentaCorrienteLimiteInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int idSucursal, int idCliente, CancellationToken ct = default);
}

// ---- Padrones ----
public record PadronIibbDto(string Cuit, decimal Percepcion);
public record PadronIibbInput(string Cuit, decimal Percepcion);
public record PadronExIvaDto(string Cuit);

/// <summary>Resultado de reemplazar el padrón completo desde un archivo.</summary>
public record ImportacionPadronDto(
    int FilasLeidas, int Importadas, int SinPercepcion, int Invalidas, int BorradasPrevias,
    long MilisegundosTotales);

public interface IPadronService
{
    Task<IReadOnlyList<PadronIibbDto>> GetIibbAsync(string? filtro, CancellationToken ct = default);
    /// <summary>
    /// Reemplaza TODO el padrón de IIBB con el contenido del archivo (formato PadronRGSPer).
    /// Borrado + carga van en una sola transacción: si algo falla, el padrón anterior queda intacto.
    /// </summary>
    /// <param name="incluirSinPercepcion">
    /// Por defecto false: las filas con alícuota 0 no se guardan (un CUIT con 0% se comporta igual
    /// que uno ausente, y son ~87% del archivo).
    /// </param>
    Task<ImportacionPadronDto> ImportarIibbAsync(Stream archivo, bool incluirSinPercepcion = false,
        CancellationToken ct = default);
    /// <summary>
    /// Reemplaza TODO el padrón de excepción de percepción de IVA. Archivo de ancho fijo: el CUIT
    /// son los primeros 11 caracteres de cada línea (no hay alícuota: estar es la excepción).
    /// </summary>
    Task<ImportacionPadronDto> ImportarExcepcionIvaAsync(Stream archivo, CancellationToken ct = default);
    Task UpsertIibbAsync(PadronIibbInput input, CancellationToken ct = default);
    Task<bool> DeleteIibbAsync(string cuit, CancellationToken ct = default);

    Task<IReadOnlyList<PadronExIvaDto>> GetExIvaAsync(string? filtro, CancellationToken ct = default);
    Task AddExIvaAsync(string cuit, CancellationToken ct = default);
    Task<bool> DeleteExIvaAsync(string cuit, CancellationToken ct = default);
}
