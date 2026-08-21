using System.ComponentModel.DataAnnotations.Schema;
using Pos.Domain.Common;
using Pos.Domain.Enums;

namespace Pos.Domain.Entities;

public class MotivoDiferencia : AuditableEntity, IEntidadLookup
{
    public int IdMotivoDiferencia { get; set; }
    public string Descripcion { get; set; } = "";
    [NotMapped] public int Id => IdMotivoDiferencia;
}

public class MotivoCierre : AuditableEntity, IEntidadLookup
{
    public int IdMotivoCierre { get; set; }
    public string Descripcion { get; set; } = "";
    [NotMapped] public int Id => IdMotivoCierre;
}

/// <summary>Lote de caja = turno (apertura → cierre Z, uno por día por caja+cajero). Varios cajeros
/// pueden operar la misma caja física a la vez, cada uno con su propio lote.</summary>
public class LoteCaja : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdLote { get; set; }
    public int IdCaja { get; set; }
    public int IdUsuarioApertura { get; set; }
    public DateTime FechaApertura { get; set; }
    public DateTime? FechaCierre { get; set; }
    public EstadoLote Estado { get; set; } = EstadoLote.Abierto;

    /// <summary>
    /// Quién cerró el lote y, si fue un cierre administrativo de Tesorería sobre un lote pendiente de
    /// un día anterior, con qué motivo. Va en el lote y no en CierresLotesCaja porque esa tabla tiene
    /// una fila por medio de pago: un lote sin movimientos no genera ninguna, y el cierre quedaba sin
    /// rastro de quién lo hizo ni por qué. En el cierre Z normal del cajero el motivo queda nulo — el
    /// cajero cierra su propio lote del día, no hace falta justificar el acto en sí.
    /// </summary>
    public int? IdUsuarioCierre { get; set; }
    public int? IdMotivoCierre { get; set; }
    public string? ObservacionCierre { get; set; }
}

public class MovimientoCaja : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdMovCaja { get; set; }
    public int IdUsuario { get; set; }
    public int IdCaja { get; set; }
    public int? IdComprobante { get; set; }
    public int IdLote { get; set; }
    public long? IdMovPagos { get; set; }
    public string? Estado { get; set; }
    public DateTime Fecha { get; set; }
    /// <summary>Descripción libre para mostrar al usuario en un movimiento manual (ej. el texto que
    /// tipeó el cajero al retirar). Null en ventas/NC: esas ya se identifican por
    /// <see cref="IdComprobante"/>. Ya NO se usa para clasificar el movimiento — ver
    /// <see cref="TipoManual"/>.</summary>
    public string? Concepto { get; set; }
    /// <summary>Tipo de movimiento manual (Ingreso/Retiro/Vuelto/CorreccionTesoreria). Null en
    /// ventas y notas de crédito (esas se identifican por <see cref="IdComprobante"/> != null).</summary>
    public TipoMovimientoManual? TipoManual { get; set; }
}

public class MovimientoPago : AuditableEntity
{
    public long IdMovPagos { get; set; }
    public int IdMedioPago { get; set; }
    public decimal Total { get; set; }
    public decimal Redondeo { get; set; }
    /// <summary>
    /// Cupón y lote del pago con tarjeta, para la rendición de cupones contra el resumen del
    /// operador. Solo se cargan cuando el tipo de pago es Tarjeta; en el resto quedan en null.
    /// Van como texto: vienen impresos con ceros a la izquierda.
    /// </summary>
    public string? NumeroCupon { get; set; }
    public string? NumeroLote { get; set; }
    /// <summary>
    /// Plan de cuotas elegido junto con el medio de pago (solo Tarjeta). <see cref="CantidadCuotas"/>
    /// queda copiado acá al momento del pago (no se resuelve por join contra PlanCuota) para que el
    /// historial no cambie si el plan se edita o se borra después.
    /// </summary>
    public int? IdPlanCuota { get; set; }
    public int? CantidadCuotas { get; set; }
    /// <summary>
    /// Banco emisor y número del pago con Cheque — análogo a cupón/lote de Tarjeta, pero para poder
    /// identificar el cheque físico al presentarlo en Tesorería/banco. Solo se cargan cuando el tipo
    /// de pago es Cheque; en el resto quedan en null. Observaciones es libre (no se exige).
    /// </summary>
    public int? IdBanco { get; set; }
    public Banco? Banco { get; set; }
    public string? NumeroCheque { get; set; }
    public string? ObservacionesCheque { get; set; }
    /// <summary>
    /// Si este pago (cupón de tarjeta, vale, o cualquier medio) quedó anulado por una nota de
    /// crédito de reversión completa — ver NotaCreditoService.EmitirAsync. El registro original NO
    /// se borra ni se toca su monto: queda como constancia de lo que se cobró, con este flag
    /// marcando que ya no es válido (ej. para no rendirlo de nuevo contra el operador de tarjeta).
    /// </summary>
    public bool Anulado { get; set; }
    public DateTime? FechaAnulacion { get; set; }
}

/// <summary>Cierre de lote acumulado por medio de pago (cierre Z).</summary>
public class CierreLoteCaja : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdLote { get; set; }
    public int IdMedioPago { get; set; }
    public decimal Total { get; set; }
    public int NumeroCierre { get; set; }
    public decimal RedondeoAcumulado { get; set; }
    public decimal DiferenciaTotal { get; set; }
    public int? IdMotivoDiferencia { get; set; }
    public string? ObservacionesCajero { get; set; }
    public bool VerificaTesoreria { get; set; }
    public int? IdMotivoCierre { get; set; }
    public string? ObservacionTesoreria { get; set; }
}

/// <summary>
/// Auditoría de un Cierre Z del controlador fiscal (reporte de jornada de la CAJA FÍSICA, comando
/// Hasar "CerrarJornadaFiscal"). Deliberadamente NO tiene FK a LoteCaja: es una operación de
/// máquina que no depende de que haya (ni de cuántos) turnos de cajero abiertos en esa caja — ver
/// ICierreZFiscalService. IdUsuario es quien lo disparó desde la pantalla, no necesariamente el
/// dueño del código de supervisor que lo autorizó (ese no se persiste, ver ISupervisorAuthService).
/// </summary>
public class CierreZFiscal : AuditableEntity
{
    public int IdCierreZFiscal { get; set; }
    public int IdSucursal { get; set; }
    public int IdCaja { get; set; }
    public int IdUsuario { get; set; }
    public DateTime FechaHoraUtc { get; set; }
    public bool Ok { get; set; }
    public string? NumeroFiscal { get; set; }
    public string? Referencia { get; set; }
    public string? Error { get; set; }
}
