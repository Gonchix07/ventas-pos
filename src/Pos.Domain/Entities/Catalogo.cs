using System.ComponentModel.DataAnnotations.Schema;
using Pos.Domain.Common;
using Pos.Domain.Enums;

namespace Pos.Domain.Entities;

public class Sector : AuditableEntity, IEntidadLookup
{
    public int IdSector { get; set; }
    public string Descripcion { get; set; } = "";
    [NotMapped] public int Id => IdSector;
}

public class Linea : AuditableEntity, IEntidadLookup
{
    public int IdLinea { get; set; }
    public string Descripcion { get; set; } = "";
    [NotMapped] public int Id => IdLinea;
}

public class Familia : AuditableEntity, IEntidadLookup
{
    public int IdFamilia { get; set; }
    public string Descripcion { get; set; } = "";
    /// <summary>
    /// Sector al que pertenece la familia. Nullable porque la fila "SIN FAMILIA" (los artículos sin
    /// clasificar) no cuelga de ningún sector. El nombre de familia NO es único: se repite entre
    /// sectores (DESODORANTES está en PERFUMERIA y en LIMPIEZA), por eso el par sector+familia es
    /// lo que identifica a la clasificación real.
    /// </summary>
    public int? IdSector { get; set; }
    public Sector? Sector { get; set; }
    [NotMapped] public int Id => IdFamilia;
}

public class ModoIva : AuditableEntity
{
    public int IdModoIva { get; set; }
    public string Descripcion { get; set; } = "";
    /// <summary>Alícuota de IVA (ej. 0.21).</summary>
    public decimal Alicuota { get; set; }
    /// <summary>Porcentaje de percepción de IVA para este modo (configurable).</summary>
    public decimal PorcentajePercepcion { get; set; }
}

public class Articulo : AuditableEntity
{
    public int IdArticulo { get; set; }
    public string CodigoInterno { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public int IdSector { get; set; }
    public Sector? Sector { get; set; }
    public int IdLinea { get; set; }
    public Linea? Linea { get; set; }
    public int IdFamilia { get; set; }
    public Familia? Familia { get; set; }
    public int IdModoIva { get; set; }
    public ModoIva? ModoIva { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>Unidad de medida del contenido neto de una unidad individual (etiquetas de precio).</summary>
    public UnidadMedida UnidadMedida { get; set; } = UnidadMedida.Ninguna;
    /// <summary>Contenido neto de UNA unidad individual en la unidad de medida indicada (ej. 1 = 1 Kg, 0.75 = 0,75 Lt).</summary>
    public decimal? ContenidoNetoUnitario { get; set; }

    public ICollection<Presentacion> Presentaciones { get; set; } = new List<Presentacion>();
}

public class Presentacion : AuditableEntity
{
    public int IdPresentacion { get; set; }
    public int IdArticulo { get; set; }
    public Articulo? Articulo { get; set; }
    /// <summary>Unidades por bulto (1 = unidad suelta).</summary>
    public decimal UnidadXBulto { get; set; } = 1m;
    public string? DescripcionTicket { get; set; }
    public ICollection<Barra> Barras { get; set; } = new List<Barra>();
}

public class Barra : AuditableEntity
{
    public int IdBarra { get; set; }
    public int IdPresentacion { get; set; }
    public Presentacion? Presentacion { get; set; }
    public string CodigoBarra { get; set; } = "";
    public TipoBarra Tipo { get; set; } = TipoBarra.Ean13;
}
