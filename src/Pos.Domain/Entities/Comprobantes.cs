using Pos.Domain.Common;
using Pos.Domain.Enums;

namespace Pos.Domain.Entities;

public class TipoComprobante : AuditableEntity
{
    public int IdTipoComprobante { get; set; }
    public string Descripcion { get; set; } = "";
    public string? Letra { get; set; }
    public string? CodigoArca { get; set; }
    /// <summary>+1 factura / débito, -1 nota de crédito.</summary>
    public int Signo { get; set; } = 1;
}

public class CabeceraComprobante : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdComprobante { get; set; }
    public int IdTipoComprobante { get; set; }
    public TipoComprobante? TipoComprobante { get; set; }
    public int? IdCliente { get; set; }
    public int? IdPuntoVenta { get; set; }
    public int? IdOperacion { get; set; }
    public string? Letra { get; set; }
    public string? NumeroCompleto { get; set; }
    public DateTime Fecha { get; set; }
    public decimal Neto { get; set; }
    public decimal Iva { get; set; }
    /// <summary>Suma de las 3 percepciones de abajo — se mantiene por compatibilidad con reportes
    /// existentes que ya sumaban este campo al Total.</summary>
    public decimal Percepciones { get; set; }
    /// <summary>Percepción de IVA sobre el neto gravado al 21% (0 si no correspondió).</summary>
    public decimal PercepcionIva21 { get; set; }
    /// <summary>Percepción de IVA sobre el neto gravado al 10,5% (0 si no correspondió).</summary>
    public decimal PercepcionIva105 { get; set; }
    /// <summary>Percepción de Ingresos Brutos según el padrón del cliente (0 si no correspondió).</summary>
    public decimal PercepcionIibb { get; set; }
    /// <summary>Alícuota (%) con la que se calculó <see cref="PercepcionIibb"/> — la del padrón, o la
    /// alícuota general por defecto si el cliente tenía CUIT pero no estaba en el padrón. Se guarda
    /// (no se recalcula al reimprimir) porque el padrón puede cambiar después de emitido.</summary>
    public decimal AlicuotaIibb { get; set; }
    public decimal Total { get; set; }
    public string? Cae { get; set; }
    public DateTime? CaeVencimiento { get; set; }
    public bool EsCaea { get; set; }
    /// <summary>Cuándo se informó este comprobante a ARCA como parte de un lote CAEA
    /// (FECAEARegInformativo) — null mientras está pendiente. Solo aplica si <see cref="EsCaea"/>;
    /// un comprobante con CAE normal nunca lo necesita (ya quedó autorizado al pedir el CAE). Ver
    /// módulo "Facturación CAEA".</summary>
    public DateTime? FechaInformadoCaeaUtc { get; set; }
    public EstadoComprobante Estado { get; set; } = EstadoComprobante.Iniciado;

    /// <summary>
    /// Comprobante que esta nota de crédito acredita (misma sucursal). Null en facturas.
    /// La tabla <see cref="ComprobanteAsociado"/> modela el vínculo genérico; esto lo duplica de
    /// forma directa porque la NC necesita resolver su origen en cada consulta de saldo, y un
    /// join extra por cada factura listada no se paga.
    /// </summary>
    public int? IdComprobanteOrigen { get; set; }

    /// <summary>Motivo de la anulación, que el cajero escribe al emitir la NC.</summary>
    public string? MotivoAnulacion { get; set; }

    public ICollection<DetalleComprobante> Detalles { get; set; } = new List<DetalleComprobante>();
}

public class DetalleComprobante : AuditableEntity
{
    public long IdDetalleComprobante { get; set; }
    public int IdSucursal { get; set; }
    public int IdComprobante { get; set; }
    public CabeceraComprobante? Comprobante { get; set; }
    public int IdPresentacion { get; set; }
    public string DescripcionTicket { get; set; } = "";
    public decimal Cantidad { get; set; }
    public decimal PrecioUnit { get; set; }
    public decimal Descuento { get; set; }
    public decimal AlicuotaIva { get; set; }
    public decimal Importe { get; set; }

    /// <summary>
    /// En una nota de crédito por artículos: la línea de la factura original que esta línea
    /// acredita. Es lo que permite saber qué queda por anular sin recalcular importes (la
    /// anulación por artículos es siempre de la línea completa). Null en facturas y en las NC
    /// por monto, que no acreditan una línea concreta.
    /// </summary>
    public long? IdDetalleOrigen { get; set; }
}

public class ComprobanteAsociado : AuditableEntity
{
    public int IdComprobanteOrigen { get; set; }
    public int IdComprobanteAsociado { get; set; }
}

// -------- Operaciones (pre-ticket / carrito) --------

public class Operacion : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdOperacion { get; set; }
    public int? IdCliente { get; set; }
    public Cliente? Cliente { get; set; }
    /// <summary>Caja física y lote (turno) que procesó la venta — necesario para arqueo/cierre (Fase 5).</summary>
    public int IdCaja { get; set; }
    public int IdLote { get; set; }
    public EstadoOperacion Estado { get; set; } = EstadoOperacion.EnCurso;
    public decimal Total { get; set; }
    public decimal DescuentoTotal { get; set; }
    /// <summary>% de descuento de la campaña de puntos-app vigente para este cliente/local, resuelto
    /// UNA sola vez al crear la operación (ver CajaService.CrearOperacionAsync) y reutilizado en cada
    /// línea que se agregue — evita golpear la API externa en cada escaneo. 0 si no hay campaña o la
    /// integración está apagada/no responde (best-effort, nunca bloquea la venta).</summary>
    public decimal PorcentajeCampaniaPuntos { get; set; }
    public ICollection<DetalleOperacion> Detalles { get; set; } = new List<DetalleOperacion>();
}

public class DetalleOperacion : AuditableEntity
{
    public long IdDetalleOperacion { get; set; }
    public int IdSucursal { get; set; }
    public int IdOperacion { get; set; }
    public Operacion? Operacion { get; set; }
    public int IdPresentacion { get; set; }
    public decimal Cantidad { get; set; }
    /// <summary>Precio realmente cobrado por unidad, YA con convenio/campaña de puntos-app aplicados
    /// (no con la oferta — esa se descuenta aparte en <see cref="Descuento"/>). Es la base sobre la
    /// que corre el motor de ofertas. Para mostrarle al cajero el precio de lista SIN ningún
    /// descuento, ver <see cref="PrecioLista"/>.</summary>
    public decimal Precio { get; set; }
    /// <summary>Precio de la lista que le corresponde a ESTE cliente (la de su tarjeta/convenio
    /// propio si tiene una — ver ResultadoPrecio.PrecioBase —, si no la lista general vigente), SIN
    /// el % de convenio ni de campaña ni la oferta — lo que se muestra en la columna "Precio" de
    /// Caja. La diferencia entre este y el total realmente facturado
    /// (PrecioLista×Cantidad − (Precio×Cantidad − Descuento)) es todo lo que se le descontó al
    /// cliente por cualquier motivo (convenio, campaña, oferta), y es lo que Caja suma en la
    /// columna "Descuento".</summary>
    public decimal PrecioLista { get; set; }
    /// <summary>Descuento de OFERTA únicamente (MotorOfertas) — convenio y campaña ya están
    /// descontados dentro de <see cref="Precio"/>, no acá.</summary>
    public decimal Descuento { get; set; }
    /// <summary>Trazabilidad: ofertas aplicadas a la línea (JSON, array de descripciones).</summary>
    public string? OfertasAplicadas { get; set; }
    /// <summary>
    /// Lista de la que salió el precio cobrado (la del convenio si tiene lista propia, si no la que
    /// ganó por prioridad). Queda registrado para poder explicar el precio de una venta y para que la
    /// caja distinga en pantalla los precios de folder/promoción. Null en líneas anteriores al campo.
    /// </summary>
    public int? IdListaPrecio { get; set; }
    /// <summary>IdOferta de la primera/principal oferta aplicada a la línea (null si no tiene
    /// ninguna) — separado de <see cref="OfertasAplicadas"/> porque ese campo solo guarda
    /// descripciones (EstadisticasService lo deserializa como texto) y agregarle el id ahí rompería
    /// esa lectura. Se usa para la interfase contable (movstock.codconv).</summary>
    public int? IdOfertaPrincipal { get; set; }
}

/// <summary>Numeradores por punto de venta. Consumo serializado (bloqueo pesimista).</summary>
public class Numero : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdNumero { get; set; }
    public int IdPuntoVenta { get; set; }
    public long Valor { get; set; }
}
