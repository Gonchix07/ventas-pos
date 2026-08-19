namespace Pos.Domain.Enums;

/// <summary>Tipo de código de barra segun SRS: EAN13 (unidad) o DUN14 (bulto).</summary>
public enum TipoBarra
{
    Ean13 = 1,
    Dun14 = 2
}

/// <summary>
/// Unidad de medida del contenido neto de UNA unidad individual del artículo (no del bulto).
/// Habilita el cálculo de "precio por Kg/Lt" en las etiquetas de precio.
/// </summary>
public enum UnidadMedida
{
    Ninguna = 0,
    Kilogramo = 1,
    Litro = 2
}

/// <summary>Prioridad de resolución de listas de precios (SRS).</summary>
public enum TipoListaPrecio
{
    Base = 1,
    Temporal = 2,
    Folder = 3
}

/// <summary>
/// Familia genérica a la que pertenece un tipo de pago (efectivo, tarjetas, billetera virtual,
/// transferencia). Clasifica el tipo y habilita reglas de negocio propias — hoy solo la de
/// <see cref="CuentaCorriente"/>, que no pasa por ningún proveedor externo sino por el control
/// de crédito del cliente. NO decide por dónde se cobra: eso es <see cref="CanalCobro"/>.
/// </summary>
public enum FuentePago
{
    Efectivo = 1,
    Tarjeta = 2,
    BilleteraVirtual = 3,
    Transferencia = 4,
    CuentaCorriente = 5
}

/// <summary>
/// Por dónde se efectúa el cobro. Se configura en el tipo de pago y determina qué adaptador
/// interviene: <see cref="Manual"/> lo registra el cajero (no hay dispositivo ni API que
/// aprobar) y <see cref="ICard"/> sale por el wrapper local iCARD (posnet/billeteras).
/// </summary>
public enum CanalCobro
{
    Manual = 1,
    ICard = 2
}

/// <summary>
/// Tipos de oferta soportados por el motor de ofertas. Es el CÓDIGO de la fila de TiposOferta
/// (columna Codigo), no su IdTipoOferta: la tabla es editable en datos, el comportamiento no.
/// </summary>
public enum TipoOfertaEnum
{
    /// <summary>% sobre cada línea alcanzada (artículos sueltos, sector o familia completa).</summary>
    Descuento = 1,
    /// <summary>Canasta: si el carrito cumple la canasta que activa, se bonifica al 100% la canasta premiada.</summary>
    MixCanasta = 2,
    /// <summary>Legacy "lleva N + M, paga N" (CantidadMin/CantidadBonif). Ya no se ofrece en el ABM.</summary>
    Bonificacion = 3,
    /// <summary>2x1: por cada 2 unidades iguales, la 2ª se bonifica al 100%.</summary>
    DosPorUno = 4,
    /// <summary>2ª unidad bonificada al % indicado (por defecto 70%).</summary>
    SegundaUnidad = 5
}

/// <summary>
/// De qué lado de una Mix Canasta está un artículo: la canasta que ACTIVA la oferta (condición)
/// o la que se BONIFICA cuando la primera se cumple. Pueden ser artículos distintos.
/// </summary>
public enum RolItemCanasta
{
    Condicion = 1,
    Bonificado = 2
}

/// <summary>Estado del ciclo de vida de un comprobante fiscal (saga de emisión).</summary>
public enum EstadoComprobante
{
    Iniciado = 0,
    PagoOk = 1,
    CaeOk = 2,
    Persistido = 3,
    Impreso = 4,
    Contingencia = 5,
    Anulado = 9
}

/// <summary>Modo de emisión solicitado.</summary>
public enum ModoFacturacion
{
    Presupuesto = 0,
    Electronica = 1,
    Fiscal = 2
}

/// <summary>Estado de un lote de caja (turno).</summary>
public enum EstadoLote
{
    Abierto = 1,
    Cerrado = 2
}

/// <summary>Estado de una operación de caja (pre-ticket).</summary>
public enum EstadoOperacion
{
    EnCurso = 1,
    Finalizada = 2,
    Facturada = 3,
    Anulada = 9
}

/// <summary>Acciones auditables por módulo.</summary>
public enum AccionPermiso
{
    Ver = 1,
    Editar = 2,
    Especial = 3
}

/// <summary>Proveedor/modelo de la terminal física de tarjeta dada de alta en una sucursal.</summary>
public enum TipoTerminalTarjeta
{
    FiServ = 1,
    PayWay = 2,
    PinPad = 3
}
