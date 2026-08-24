using Pos.Domain.Enums;

namespace Pos.Application.Abstractions.Fiscal;

/// <summary>Responsabilidad del receptor frente a IVA, en términos neutros al proveedor.</summary>
public enum ResponsabilidadIvaFiscal
{
    ConsumidorFinal, ResponsableInscripto, Monotributo, MonotributoSocial,
    Exento, NoResponsable, NoCategorizado
}

public enum TipoDocumentoFiscal { Ninguno, Dni, Cuit, Cuil, Pasaporte, Ci, Le, Lc }

public record ClienteFiscal(
    string RazonSocial,
    string? NumeroDocumento,
    TipoDocumentoFiscal TipoDocumento,
    ResponsabilidadIvaFiscal Responsabilidad,
    string? Domicilio);

/// <summary>
/// Una línea del comprobante. <paramref name="PrecioUnitario"/> es el precio final por unidad
/// (con IVA incluido) — es como se cargan los precios en el POS, y la impresora fiscal lo acepta
/// tal cual indicándole modo "precio total"; que ella haga el desglose evita discrepancias de
/// redondeo entre el neto que calcula el sistema y el que imprime el controlador.
/// </summary>
public record ItemFiscal(
    string Descripcion,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal AlicuotaIva,
    decimal Descuento,
    string? CodigoInterno);

public record PagoFiscal(
    string Descripcion,
    decimal Monto,
    FuentePago Fuente,
    string? DescripcionAdicional,
    int Cuotas);

/// <summary>Tipos de tributo que reconoce el comando fiscal "ImprimirOtrosTributos" (protocolo 2G).
/// Solo se listan los que este POS usa hoy — el enum completo del manual tiene más.</summary>
public enum TipoTributoFiscal { PercepcionIva, PercepcionIibb }

/// <summary>
/// Tributo distinto del IVA/impuesto interno de cada ítem (percepciones de IVA/IIBB). Se imprime
/// DESPUÉS de todos los ítems/descuentos y ANTES de los pagos — el protocolo lo exige así, y una vez
/// enviado el primero ya no se pueden imprimir más ítems.
/// </summary>
public record TributoFiscal(TipoTributoFiscal Tipo, string Descripcion, decimal BaseImponible, decimal Importe);

/// <summary>
/// Datos del comprobante para los puertos fiscales. Los campos a partir de <c>IdSucursal</c> son
/// opcionales porque sólo los necesita la impresora fiscal (para rutear al equipo de la caja y
/// para imprimir el detalle); el servicio de CAE se conforma con la cabecera.
/// </summary>
public record ComprobanteFiscal(
    int IdEmpresa,
    int PuntoVenta,
    string TipoComprobante,
    string Letra,
    long Numero,
    string? CuitCliente,
    decimal Neto,
    decimal Iva,
    decimal Total,
    DateTime Fecha,
    int IdSucursal = 0,
    int IdCaja = 0,
    ClienteFiscal? Cliente = null,
    IReadOnlyList<ItemFiscal>? Items = null,
    IReadOnlyList<PagoFiscal>? Pagos = null,
    IReadOnlyList<TributoFiscal>? Tributos = null,
    /// <summary>Código de comprobante ARCA (TipoComprobante.CodigoArca: "001" Fact.A, "006"
    /// Fact.B, "003"/"008" NC A/B, etc.) — solo lo necesita el puerto de CAE/CAEA (WSFEv1 pide el
    /// código numérico, no la descripción); el controlador Hasar no lo usa.</summary>
    string? CodigoArca = null);

public record ResultadoCae(bool Ok, string? Cae, DateTime? Vencimiento, bool EsCaea, string? Error);

public record PeriodoFiscal(int Anio, int Mes);

public record ResultadoCaea(bool Ok, string? Caea, DateTime? Desde, DateTime? Hasta, string? Error);

public record EstadoServicioFiscal(bool Disponible, string? Detalle);

/// <summary>Puerto de servicios fiscales (ARCA/AFIP). Adaptador Mock en fase 1.</summary>
public interface IFiscalService
{
    Task<ResultadoCae> SolicitarCaeAsync(ComprobanteFiscal cmp, CancellationToken ct);
    Task<ResultadoCaea> ObtenerCaeaAsync(int idEmpresa, PeriodoFiscal periodo, CancellationToken ct);
    Task<ResultadoCaea> InformarComprobantesCaeaAsync(int idEmpresa, IEnumerable<ComprobanteFiscal> lote, CancellationToken ct);
    Task<EstadoServicioFiscal> PingAsync(CancellationToken ct);
}

/// <summary>
/// <paramref name="NumeroFiscal"/> es el número que asignó el controlador fiscal. Con impresora
/// real NO coincide con el numerador interno del POS: cada tipo de comprobante lleva su propia
/// serie dentro del equipo, así que hay que guardarlo aparte para poder rastrear el comprobante
/// en la memoria de auditoría.
/// </summary>
public record ResultadoImpresion(bool Ok, string? Referencia, string? Error, string? NumeroFiscal = null);

/// <summary>Puerto de impresora fiscal (Hasar / wrapper iCARD local). Adaptador Mock en fase 1.</summary>
public interface IFiscalPrinter
{
    Task<ResultadoImpresion> ImprimirFiscalAsync(ComprobanteFiscal cmp, CancellationToken ct);
    Task<ResultadoImpresion> ImprimirNotaCreditoAsync(ComprobanteFiscal cmp, CancellationToken ct);
    Task<ResultadoImpresion> CierreZAsync(int idSucursal, int idCaja, CancellationToken ct);
    Task<ResultadoImpresion> ArqueoXAsync(int idSucursal, int idCaja, CancellationToken ct);
}
