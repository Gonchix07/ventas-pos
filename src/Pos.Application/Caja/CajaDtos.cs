namespace Pos.Application.Caja;

// ---- Apertura / lote ----
/// <summary><c>AdmitePresupuesto</c>: si ESTA caja habilita el modo Presupuesto (además el cliente
/// necesita su propio <c>PermitePresupuesto</c> — ver ClienteResumen). Gatea el toggle en la pantalla
/// de cobro.</summary>
public record LoteDto(int IdSucursal, int IdLote, int IdCaja, string DescripcionCaja, int IdPuntoVenta,
    DateTime FechaApertura, string Estado, bool AdmitePresupuesto);
// CodigoSupervisor: null salvo que se esté abriendo en un puesto distinto al propio (ver
// CajaService.AbrirCajaAsync) y quien abre no sea ya Supervisor/Administrador.
public record AperturaRequest(int IdSucursal, int IdCaja, string? CodigoSupervisor = null);

/// <summary>
/// Turno abierto del cajero logueado, en cualquier caja de la sucursal. Permite retomar el turno
/// desde otra PC cuando la original se cae: la caja que resuelve la IP del puesto nuevo no es la del
/// lote, y sin esto el turno quedaba inaccesible con sus ventas adentro.
/// </summary>
public record TurnoAbiertoDto(int IdSucursal, int IdLote, int IdCaja, string DescripcionCaja,
    int IdPuntoVenta, DateTime FechaAperturaUtc, int VentasSinCobrar, bool EsLaCajaDeEstaPc);

/// <summary>Caja de la sucursal, para elegir dónde abrir turno desde un puesto sin configurar.</summary>
public record CajaDisponibleDto(int IdSucursal, int IdCaja, string Descripcion, int IdPuntoVenta);

/// <summary>
/// Medio de pago ofrecido en el cobro. <c>Fuente</c> = familia (5 = Cuenta corriente, 2 = Tarjetas:
/// estos últimos piden cupón y lote). <c>EsPredeterminado</c> marca cuál viene elegido de entrada.
/// <c>ImprimeComprobante</c>: si al cobrar con este medio hay que imprimir además un comprobante
/// propio del medio (ej. VALE) para que lo firme el empleado — ver PagoAdminService.
/// </summary>
public record MedioPagoResumen(int IdMedioPago, string Descripcion, int Fuente, bool EsPredeterminado,
    bool ImprimeComprobante);

/// <summary>Plan de cuotas de un medio de pago Tarjeta, para elegir junto con el medio al cobrar.</summary>
public record PlanCuotaResumen(int IdPlan, string Denominacion, int CantidadCuotas);

// ---- Identificación de cliente ----
// La pantalla de caja muestra estos datos en tabla para que el cajero pueda distinguir homónimos:
// además del padrón (domicilio/localidad), la tarjeta del cliente y la lista de precios con la que
// se lo va a valorizar. ListaPrecioOrigen dice de dónde sale esa lista ("Convenio" o "Tarjeta"):
// hoy el precio de caja solo respeta la del convenio (ver PricingService), la de la tarjeta es
// informativa (se usa en etiquetas).
/// <summary>Persona autorizada a comprar en nombre del cliente, para que el cajero la controle.</summary>
public record AutorizadoResumen(string Dni, string Descripcion);

public record ClienteResumen(int IdCliente, string CodigoInt, string Descripcion, string? NombreFantasia, string? Cuit,
    string? Documento, bool PermitePresupuesto, string? CondIvaDescripcion, int? IdConvenio, decimal? DescuentoConvenio,
    string? Domicilio, string? Localidad, string? NroTarjeta, string? TipoTarjeta, int CantidadTarjetas,
    string? ListaPrecioDescripcion, string? ListaPrecioOrigen,
    // Solo los activos: un autorizado dado de baja no habilita a nadie en el mostrador.
    List<AutorizadoResumen>? Autorizados = null);

// ---- Búsqueda de artículo ----
public record ArticuloEncontrado(int IdArticulo, int IdPresentacion, string CodigoInterno,
    string Descripcion, string? DescripcionTicket, decimal UnidadXBulto, string ImagenUrl,
    decimal PrecioVigente, decimal PrecioConvenio, bool TieneConvenio,
    // Cantidad que venía en el propio código de barra (etiqueta de balanza: son kilos).
    // Cuando llega, manda sobre la cantidad que haya tipeado el cajero.
    decimal? CantidadDetectada = null);

// ---- Operación ----
public record CrearOperacionRequest(int IdSucursal, int IdCaja, int? IdCliente);
/// <param name="ListaPrecio">Nombre de la lista de la que salió el precio (null en líneas viejas).</param>
/// <param name="EsPrecioFolder">La lista es de tipo Folder: la caja lo destaca en pantalla porque es
/// un precio de promoción, distinto del habitual del artículo.</param>
public record OperacionLineaDto(long IdDetalle, int IdPresentacion, string CodigoInterno,
    string Descripcion, decimal Cantidad, decimal PrecioUnit, decimal Bruto, decimal Descuento,
    decimal Neto, List<string> OfertasAplicadas, string? ListaPrecio = null, bool EsPrecioFolder = false);
/// <param name="Neto">Total de mercadería (bruto - descuento) — NO incluye percepciones.</param>
/// <param name="PercepcionIva21">Percepción de IVA sobre el neto gravado al 21% (0 si no corresponde).</param>
/// <param name="PercepcionIva105">Percepción de IVA sobre el neto gravado al 10,5% (0 si no corresponde).</param>
/// <param name="PercepcionIibb">Percepción de Ingresos Brutos según el padrón del cliente (0 si no corresponde).</param>
/// <param name="TotalACobrar">Neto + las 3 percepciones — este es el monto real que hay que cobrar.</param>
public record OperacionDto(int IdSucursal, int IdOperacion, int? IdCliente, string? ClienteDescripcion,
    string Estado, List<OperacionLineaDto> Lineas, decimal Bruto, decimal Descuento, decimal Neto,
    decimal PercepcionIva21 = 0, decimal PercepcionIva105 = 0, decimal PercepcionIibb = 0, decimal TotalACobrar = 0);

/// <summary>
/// Venta sin terminar de un cliente, para retomarla después de una caída del sistema (o de un F5:
/// la pantalla de caja no recuerda la operación, pero la operación y sus líneas ya están en la BD).
/// Se limita al lote abierto del cajero: una operación de un lote ya cerrado no se puede seguir
/// vendiendo ni facturar contra ese turno.
/// </summary>
public record OperacionPendienteDto(int IdOperacion, DateTime FechaUtc, string Estado,
    int CantidadLineas, decimal Total);

public record AgregarLineaRequest(int IdPresentacion, decimal Cantidad);
public record CambiarCantidadRequest(decimal Cantidad, string? CodigoSupervisor = null);
public record RedondeoDto(decimal Ajuste, decimal TotalConRedondeo);

/// <summary>
/// Oferta por medio de pago vigente, para que la pantalla de cobro calcule en vivo cuánto se le
/// informa al cliente que tiene que abonar por ese medio (mismo cálculo que hace el servidor al
/// emitir — ver OfertaMedioPagoReglas.CalcularDescuento). IdPlanCuota null = aplica en cualquier
/// cantidad de cuotas del medio.
/// </summary>
public record OfertaMedioPagoVigenteDto(int IdMedioPago, int? IdPlanCuota, decimal Porcentaje, decimal TopeMaximo);

public interface ICajaService
{
    Task<LoteDto> AbrirCajaAsync(AperturaRequest req, CancellationToken ct = default);
    Task<LoteDto?> ObtenerLoteActualAsync(int idSucursal, int idCaja, CancellationToken ct = default);
    Task<IReadOnlyList<TurnoAbiertoDto>> GetMisTurnosAbiertosAsync(int idSucursal, CancellationToken ct = default);
    Task<IReadOnlyList<CajaDisponibleDto>> GetCajasAsync(int idSucursal, CancellationToken ct = default);

    Task<IReadOnlyList<ClienteResumen>> BuscarClienteAsync(int idSucursal, string query, CancellationToken ct = default);
    Task<ArticuloEncontrado?> BuscarArticuloAsync(int idSucursal, string codigo, int? idCliente, CancellationToken ct = default);
    /// <summary>Búsqueda manual (lupa): por código interno, descripción o barra, para elegir de una lista.</summary>
    Task<IReadOnlyList<ArticuloEncontrado>> BuscarArticulosAsync(int idSucursal, string texto, int? idCliente, CancellationToken ct = default);

    Task<IReadOnlyList<OperacionPendienteDto>> GetOperacionesPendientesAsync(int idSucursal, int idCaja, int idCliente, CancellationToken ct = default);
    Task<OperacionDto> CrearOperacionAsync(CrearOperacionRequest req, CancellationToken ct = default);
    Task<OperacionDto?> ObtenerOperacionAsync(int idSucursal, int idOperacion, CancellationToken ct = default);
    Task<OperacionDto?> AgregarLineaAsync(int idSucursal, int idOperacion, AgregarLineaRequest req, CancellationToken ct = default);
    Task<OperacionDto?> AnularLineaAsync(int idSucursal, int idOperacion, long idDetalle, string? codigoSupervisor = null, CancellationToken ct = default);
    /// <summary>Fija la cantidad de una línea (los +/- de la tabla). Cantidad 0 = anular la línea.</summary>
    // codigoSupervisor solo hace falta si cantidad&lt;=0 (equivale a anular, ver implementación).
    Task<OperacionDto?> CambiarCantidadLineaAsync(int idSucursal, int idOperacion, long idDetalle, decimal cantidad,
        string? codigoSupervisor = null, CancellationToken ct = default);
    Task<OperacionDto?> FinalizarOperacionAsync(int idSucursal, int idOperacion, CancellationToken ct = default);
    /// <summary>Vuelve una operación Finalizada a EnCurso (botón "Volver" desde la pantalla de cobro, para seguir cargando artículos). No aplica si ya se facturó o anuló.</summary>
    Task<OperacionDto?> ReabrirOperacionAsync(int idSucursal, int idOperacion, CancellationToken ct = default);

    Task<RedondeoDto> CalcularRedondeoAsync(decimal total, CancellationToken ct = default);

    /// <summary>Medios de pago activos, para la pantalla de cobro (accesible a Cajero/Supervisor).</summary>
    /// <param name="idCliente">
    /// Cliente de la venta: se excluyen los medios restringidos a un cluster al que no pertenece.
    /// Sin cliente solo se ofrecen los medios sin restricción.
    /// </param>
    Task<IReadOnlyList<MedioPagoResumen>> GetMediosPagoAsync(int? idCliente = null, CancellationToken ct = default);
    Task<IReadOnlyList<PlanCuotaResumen>> GetPlanesMedioAsync(int idMedioPago, CancellationToken ct = default);
    /// <summary>Ofertas por medio de pago activas de la sucursal, para calcular en vivo el importe con descuento.</summary>
    Task<IReadOnlyList<OfertaMedioPagoVigenteDto>> GetOfertasMedioPagoVigentesAsync(int idSucursal, CancellationToken ct = default);

    /// <summary>Descripción de la caja resuelta por login (para mostrar antes de que exista un lote).</summary>
    Task<string?> ObtenerDescripcionCajaAsync(int idSucursal, int idCaja, CancellationToken ct = default);
}
