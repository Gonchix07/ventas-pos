using Pos.Domain.Common;
using Pos.Domain.Enums;

namespace Pos.Domain.Entities;

public class ListaPrecio : AuditableEntity
{
    public int IdListaPrecio { get; set; }
    public int IdSucursal { get; set; }
    public string CodigoInterno { get; set; } = "";
    public TipoListaPrecio Tipo { get; set; } = TipoListaPrecio.Base;
    /// <summary>Prioridad de resolución: mayor gana. Folder &gt; Temporal vigente &gt; Base.</summary>
    public int Prioridad { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public ICollection<Precio> Precios { get; set; } = new List<Precio>();
}

public class Precio : AuditableEntity
{
    public int IdListaPrecio { get; set; }
    public ListaPrecio? ListaPrecio { get; set; }
    /// <summary>El precio cuelga de la presentación (unidad vs bulto).</summary>
    public int IdPresentacion { get; set; }
    public Presentacion? Presentacion { get; set; }
    /// <summary>Columna denormalizada para consultas por artículo.</summary>
    public int IdArticulo { get; set; }
    public decimal PrecioFinal { get; set; }
    public decimal ImpuestoInterno { get; set; }
}

public class Convenio : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdConvenio { get; set; }
    public int IdCliente { get; set; }
    public Cliente? Cliente { get; set; }
    public decimal Descuento { get; set; }
    public int? IdListaPrecio { get; set; }
}
