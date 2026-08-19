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
    public decimal Total { get; set; }
    public string? Cae { get; set; }
    public DateTime? CaeVencimiento { get; set; }
    public bool EsCaea { get; set; }
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
    public decimal Precio { get; set; }
    public decimal Descuento { get; set; }
    /// <summary>Trazabilidad: ofertas aplicadas a la línea (JSON).</summary>
    public string? OfertasAplicadas { get; set; }
    /// <summary>
    /// Lista de la que salió el precio cobrado (la del convenio si tiene lista propia, si no la que
    /// ganó por prioridad). Queda registrado para poder explicar el precio de una venta y para que la
    /// caja distinga en pantalla los precios de folder/promoción. Null en líneas anteriores al campo.
    /// </summary>
    public int? IdListaPrecio { get; set; }
}

/// <summary>Numeradores por punto de venta. Consumo serializado (bloqueo pesimista).</summary>
public class Numero : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdNumero { get; set; }
    public int IdPuntoVenta { get; set; }
    public long Valor { get; set; }
}
