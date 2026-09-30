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

    /// <summary>Solo con valor cuando Tipo=Enlazada: la lista de la que toma los precios base antes
    /// de aplicarles el recargo de Diferenciales. Nunca apunta a otra lista Enlazada (se valida al
    /// guardar) — un solo nivel de indirección.</summary>
    public int? IdListaBase { get; set; }
    public ListaPrecio? ListaBase { get; set; }
    public ICollection<DiferencialListaPrecio> Diferenciales { get; set; } = new List<DiferencialListaPrecio>();
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

/// <summary>
/// Recargo porcentual que una lista Tipo=Enlazada aplica sobre el precio de su ListaBase, por línea
/// completa (IdLinea) o por artículo puntual (IdArticulo) — nunca ambos a la vez en la misma fila.
/// Si un artículo matchea tanto un diferencial propio como el de su línea, el del artículo puntual
/// tiene prioridad (ver PricingService/EtiquetaService). Se puede cargar a mano (CRUD en Precios y
/// Ofertas) o importar desde descxtipocli_art.dbf (ver DiferencialListaPrecioImportService),
/// filtrando TIPO_TARJE='03' y vigencia (DESDE/HASTA) al momento de importar.
/// </summary>
public class DiferencialListaPrecio : AuditableEntity
{
    public int IdDiferencial { get; set; }
    public int IdListaPrecio { get; set; }
    public ListaPrecio? ListaPrecio { get; set; }
    public int? IdLinea { get; set; }
    public Linea? Linea { get; set; }
    public int? IdArticulo { get; set; }
    public Articulo? Articulo { get; set; }
    public decimal Porcentaje { get; set; }
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
