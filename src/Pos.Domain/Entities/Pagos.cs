using Pos.Domain.Common;
using Pos.Domain.Enums;

namespace Pos.Domain.Entities;

/// <summary>
/// Tipo de pago: el genérico (Efectivo, Transferencia, Billetera virtual, Tarjetas). Agrupa a
/// varios medios de pago concretos y define POR DÓNDE se cobra (<see cref="Canal"/>).
/// </summary>
public class TipoPago : AuditableEntity
{
    public int IdTipoPago { get; set; }
    public string Descripcion { get; set; } = "";
    /// <summary>Familia genérica a la que pertenece (clasificación, no ruteo).</summary>
    public FuentePago Fuente { get; set; }
    /// <summary>Por dónde se efectúa el cobro de todos los medios de este tipo.</summary>
    public CanalCobro Canal { get; set; } = CanalCobro.Manual;
    public ICollection<MedioPago> Medios { get; set; } = new List<MedioPago>();
}

/// <summary>
/// Medio de pago concreto (Visa, Mastercard, MODO, Mercado Pago, una cuenta bancaria puntual…).
/// Siempre pertenece a un <see cref="TipoPago"/>; un mismo tipo puede tener muchos medios.
/// </summary>
public class MedioPago : AuditableEntity
{
    public int IdMedioPago { get; set; }
    public string Descripcion { get; set; } = "";
    public int IdTipoPago { get; set; }
    public TipoPago? TipoPago { get; set; }
    /// <summary>
    /// Medio que la caja propone por defecto al abrir el cobro (normalmente Efectivo). Hay uno
    /// solo: al marcar otro, el anterior se destilda (ver PagoAdminService).
    /// </summary>
    public bool EsPredeterminado { get; set; }
    /// <summary>
    /// Si está seteado, el medio SOLO se habilita para los clientes que pertenecen a ese cluster
    /// (ej. una cuenta bancaria propia de un grupo de clientes). Null = disponible para todos.
    /// </summary>
    public int? IdCluster { get; set; }
    public Cluster? Cluster { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>
    /// Si al cobrar con este medio corresponde imprimir un comprobante adicional (ej. cupón/voucher
    /// propio del medio, aparte del comprobante fiscal). Todavía no se usa en Caja — el campo se
    /// agrega ahora para poder cargarlo desde el ABM; la función que lo consuma se define más
    /// adelante.
    /// </summary>
    public bool ImprimeComprobante { get; set; } = true;
    public ICollection<PlanCuota> Planes { get; set; } = new List<PlanCuota>();
}

/// <summary>
/// Plan de cuotas de un medio de pago de tarjeta (ej. "3 cuotas sin interés"). Solo tiene sentido
/// para medios cuyo TipoPago.Fuente sea Tarjeta — se valida al crearlo, no con una restricción de
/// esquema. El cajero lo elige junto con el medio al cobrar (ver MovimientoPago.IdPlanCuota).
/// </summary>
public class PlanCuota : AuditableEntity
{
    public int IdPlan { get; set; }
    public int IdMedioPago { get; set; }
    public MedioPago? MedioPago { get; set; }
    public string Denominacion { get; set; } = "";
    public int CantidadCuotas { get; set; }
}

public class CuentaCorriente : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdCliente { get; set; }
    public int IdComprobante { get; set; }
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
}
