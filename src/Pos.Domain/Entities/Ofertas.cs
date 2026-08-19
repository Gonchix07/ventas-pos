using System.ComponentModel.DataAnnotations.Schema;
using Pos.Domain.Common;
using Pos.Domain.Enums;

namespace Pos.Domain.Entities;

public class TipoOferta : AuditableEntity, IEntidadLookup
{
    public int IdTipoOferta { get; set; }
    public string Descripcion { get; set; } = "";
    /// <summary>Qué hace el tipo (<see cref="TipoOfertaEnum"/>). El motor mira esto, no el Id ni la descripción.</summary>
    public int Codigo { get; set; }
    /// <summary>Si se ofrece en el ABM de ofertas. Los tipos legacy quedan en la tabla pero no se eligen.</summary>
    public bool Seleccionable { get; set; } = true;
    [NotMapped] public int Id => IdTipoOferta;
}

public class CabeceraOferta : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdOferta { get; set; }
    public int IdAccion { get; set; }
    public string Descripcion { get; set; } = "";
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public bool Acumula { get; set; }
    public bool PermiteConvenio { get; set; }
    public ICollection<AlcanceOferta> Alcances { get; set; } = new List<AlcanceOferta>();
    public ICollection<AccionOferta> Acciones { get; set; } = new List<AccionOferta>();
}

/// <summary>A qué aplica la oferta. Campos nullable = comodín (aplica a todos).</summary>
public class AlcanceOferta : AuditableEntity
{
    public int IdAlcance { get; set; }
    public int IdSucursal { get; set; }
    public int IdOferta { get; set; }
    public int? IdCluster { get; set; }
    public int? IdLinea { get; set; }
    public int? IdSector { get; set; }
    public int? IdFamilia { get; set; }
    public int? IdArticulo { get; set; }
    /// <summary>Marca de excepción (excluye en vez de incluir).</summary>
    public bool EsExcepcion { get; set; }
}

/// <summary>Qué hace la oferta (según TipoOferta): campos propios por tipo.</summary>
public class AccionOferta : AuditableEntity
{
    public int IdAccion { get; set; }
    public int IdSucursal { get; set; }
    public int IdOferta { get; set; }
    public int IdTipoOferta { get; set; }
    public TipoOferta? TipoOferta { get; set; }
    public int? IdPresentacion { get; set; }
    public decimal? Porcentaje { get; set; }
    public decimal? MontoFijo { get; set; }
    public decimal? CantidadMin { get; set; }
    public decimal? CantidadBonif { get; set; }
    /// <summary>Solo para Mix Canasta: los artículos y cantidades que arman la canasta.</summary>
    public ICollection<ItemOferta> Items { get; set; } = new List<ItemOferta>();
}

/// <summary>
/// Un renglón de una acción Mix Canasta. La acción tiene DOS canastas: la que activa la oferta
/// (<see cref="RolItemCanasta.Condicion"/>) y la que se bonifica al 100% cuando la primera se
/// cumple (<see cref="RolItemCanasta.Bonificado"/>). Los artículos de una y otra pueden ser distintos.
/// </summary>
public class ItemOferta : AuditableEntity
{
    public int IdItem { get; set; }
    public int IdAccion { get; set; }
    public int IdSucursal { get; set; }
    public int IdOferta { get; set; }
    public int IdArticulo { get; set; }
    /// <summary>Unidades: requeridas en el carrito si es condición, bonificadas si es premio.</summary>
    public decimal Cantidad { get; set; }
    /// <summary>Ver <see cref="RolItemCanasta"/>.</summary>
    public int Rol { get; set; }
}

/// <summary>
/// Descuento por medio de pago (y, si es tarjeta, por una cantidad de cuotas puntual). Vive aparte
/// del motor de ofertas por línea/artículo (<see cref="CabeceraOferta"/>): no se aplica al carrito,
/// se calcula recién en la pantalla de cobro sobre el medio que elige el cajero. Ver
/// <c>OfertaMedioPagoReglas</c> (Pos.Domain.Services) y <c>FacturacionService.EmitirAsync</c>.
/// </summary>
public class OfertaMedioPago : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdOfertaMedioPago { get; set; }
    public string Descripcion { get; set; } = "";
    public int IdMedioPago { get; set; }
    /// <summary>
    /// Null = aplica en cualquier cantidad de cuotas de ese medio (o el medio no es tarjeta, donde
    /// no hay cuotas). Si se especifica, la oferta solo vale para ESE plan puntual — una oferta
    /// específica de 3 cuotas gana por sobre una general del mismo medio (ver Resolver).
    /// </summary>
    public int? IdPlanCuota { get; set; }
    /// <summary>0 a 100, mismo criterio que AccionOferta.Porcentaje (no es la Alicuota de IVA, que es 0 a 1).</summary>
    public decimal Porcentaje { get; set; }
    /// <summary>Tope máximo en $ del descuento: nunca se aplica más que esto, sea cual sea el monto del pago.</summary>
    public decimal TopeMaximo { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Vigencia: mismo criterio que CabeceraOferta (FechaInicio/FechaFin, inclusive). Fuera
    /// de rango no se aplica aunque Activo sea true — ver GetOfertasMedioPagoVigentesAsync.</summary>
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
}
