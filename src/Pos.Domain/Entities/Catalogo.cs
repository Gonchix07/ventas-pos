using System.ComponentModel.DataAnnotations.Schema;
using Pos.Domain.Common;
using Pos.Domain.Enums;

namespace Pos.Domain.Entities;

public class Sector : AuditableEntity, IEntidadLookup
{
    public int IdSector { get; set; }
    public string Descripcion { get; set; } = "";
    /// <summary>Código de T_Sectores en el ERP Central, usado por el sync para matchear sin depender
    /// de que la descripción no cambie. Null en sectores creados a mano desde el ABM.</summary>
    public string? CodigoErp { get; set; }
    [NotMapped] public int Id => IdSector;
}

public class Linea : AuditableEntity, IEntidadLookup
{
    public int IdLinea { get; set; }
    public string Descripcion { get; set; } = "";
    /// <summary>Código de T_Lineas en el ERP Central. Ver <see cref="Sector.CodigoErp"/>.</summary>
    public string? CodigoErp { get; set; }
    [NotMapped] public int Id => IdLinea;
}

/// <summary>
/// Banco emisor de un cheque (ver <see cref="MovimientoPago.IdBanco"/>). Lookup simple, igual patrón
/// que <see cref="Sector"/>/<see cref="Linea"/> — se administra desde el ABM genérico de tablas.
/// </summary>
public class Banco : AuditableEntity, IEntidadLookup
{
    public int IdBanco { get; set; }
    public string Descripcion { get; set; } = "";
    [NotMapped] public int Id => IdBanco;
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
    /// <summary>Código de T_Familias en el ERP Central. Ver <see cref="Sector.CodigoErp"/>.</summary>
    public string? CodigoErp { get; set; }
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
    /// <summary>Código (idModoIVA) de T_ModoIVA en el ERP Central. Ver <see cref="Sector.CodigoErp"/>.</summary>
    public string? CodigoErp { get; set; }
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
    /// <summary>
    /// Unidades por bulto del artículo (1 = no viene en bulto). Distinto de
    /// <see cref="Presentacion.UnidadXBulto"/>: ese es por presentación/código de barras concreto
    /// (una presentación "Caja x12" puede o no estar cargada); este es un dato propio de la ficha
    /// del artículo, siempre disponible aunque no haya ninguna presentación de bulto configurada —
    /// lo necesita la codificación de cantidades de la interfase contable (movstock.salida:
    /// entero=bultos, decimales=unidades sueltas, con 2 o 3 decimales según si esto supera 99).
    /// </summary>
    public decimal UnidadXBulto { get; set; } = 1m;
    /// <summary>
    /// Se vende "suelto" por peso, leído de una etiqueta de balanza (código Kretz, ver
    /// BarraBalanza) — no un paquete de peso fijo. Lo necesita la codificación de cantidades de la
    /// interfase contable: en este caso <c>movstock.salida</c> va siempre con 3 decimales (kilos
    /// entero + gramos), sin aplicar la regla de bultos/unidades sueltas.
    /// </summary>
    public bool VentaPorPeso { get; set; }

    /// <summary>
    /// Cantidad mínima que se carga al carrito al leer este artículo por EAN o código interno
    /// individual en Caja (1 = comportamiento de siempre, una unidad por lectura). Si el cajero
    /// escanea con una cantidad tipeada, esta se multiplica por esa cantidad (ver
    /// CajaPage.procesarCola) — no la reemplaza. Pensado para productos que técnicamente se leen de
    /// a uno pero SIEMPRE se venden en un múltiplo fijo (ej. un pack que no tiene presentación de
    /// bulto propia cargada). Default 1 para todo el catálogo existente.
    /// </summary>
    public decimal MinimaUnidadVenta { get; set; } = 1m;

    /// <summary>idArticulo de T_Articulos en el ERP Central. Clave de matcheo del sync — a
    /// diferencia de <see cref="CodigoInterno"/>, no se espera que cambie nunca del lado del ERP.
    /// Null en artículos cargados a mano desde el ABM (nunca vinieron del ERP).</summary>
    public long? IdErp { get; set; }
    /// <summary>Estado crudo (T_Articulos.estado: 0/1/2/3) tal como viene del ERP, guardado sin
    /// perder información aunque hoy <see cref="Activo"/> solo distinga activo/inactivo (0,1,2 →
    /// activo — el 2 es "suspendido" pero igual se puede vender —, 3 → inactivo).</summary>
    public int? EstadoErp { get; set; }

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
    /// <summary>idPresentacion de T_Presentaciones en el ERP Central. Ver <see cref="Articulo.IdErp"/>.</summary>
    public long? IdErp { get; set; }
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
