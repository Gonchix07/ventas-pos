namespace Pos.Domain.Services;

/// <summary>
/// Ids de numeradores en la tabla <c>Numeros</c>. La clave es <c>IdNumero</c> (con
/// <c>IdPuntoVenta</c> como columna informativa), así que un mismo punto de venta puede tener
/// varias series. Cada combinación de punto de venta + TIPO de comprobante (CbteTipo de ARCA)
/// lleva su propia serie independiente — ante ARCA, Factura A y Factura B (o NC A y NC B) del
/// mismo punto de venta nunca comparten numeración; mezclarlas dejaría huecos en ambas y
/// desincronizaría contra <c>FECompUltimoAutorizado</c>, que también pide el CbteTipo por separado.
/// Bug real encontrado probando Electrónica en homologación (2026-08-24): al principio
/// <c>Factura</c> solo tomaba el punto de venta, así que Factura A y B terminaban compartiendo la
/// serie de la que se usara primero.
/// </summary>
public static class NumeradorIds
{
    /// <summary>Separación entre familias de numeradores. Un punto de venta nunca llega a 100.000.</summary>
    private const int OffsetNotaCredito = 100_000;
    /// <summary>Lugar reservado por CbteTipo dentro de cada familia — un punto de venta interno
    /// nunca llega a 1.000, y ningún CbteTipo de ARCA llega a 100 (los NC más altos rondan el 13).</summary>
    private const int OffsetPorTipo = 1_000;

    public static int Factura(int idPuntoVenta, int cbteTipo) => cbteTipo * OffsetPorTipo + idPuntoVenta;

    public static int NotaCredito(int idPuntoVenta, int cbteTipo) => OffsetNotaCredito + cbteTipo * OffsetPorTipo + idPuntoVenta;
}

/// <summary>Qué se está anulando.</summary>
public enum TipoAnulacion
{
    /// <summary>El comprobante entero: todas las líneas que todavía tengan saldo.</summary>
    Total = 1,
    /// <summary>Sólo las líneas elegidas, completas (no se devuelven cantidades parciales).</summary>
    PorArticulos = 2,
    /// <summary>Un importe suelto (ajuste de precio), sin devolución de mercadería.</summary>
    PorMonto = 3
}

public record LineaOriginal(long IdDetalle, decimal Importe, decimal Alicuota, bool YaAnulada);

public record ProrrateoAlicuota(decimal Alicuota, decimal Importe);

/// <summary>
/// Reglas de las notas de crédito sobre un comprobante ya emitido.
/// </summary>
public static class NotaCreditoReglas
{
    private const decimal Tolerancia = 0.01m;

    private static decimal Redondear(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Cuánto queda por anular: el total del comprobante menos lo ya acreditado por notas de
    /// crédito anteriores. Nunca negativo.
    /// </summary>
    public static decimal SaldoAnulable(decimal totalComprobante, decimal totalYaAcreditado) =>
        Math.Max(0m, Redondear(totalComprobante - totalYaAcreditado));

    /// <summary>
    /// Un importe es acreditable si es positivo y no excede el saldo. La tolerancia absorbe el
    /// centavo de redondeo cuando se acredita exactamente el saldo restante.
    /// </summary>
    public static bool ImporteAcreditable(decimal monto, decimal saldo) =>
        monto > 0m && monto - saldo <= Tolerancia;

    /// <summary>
    /// Cuándo una NC revierte TODOS los medios de pago originales (cupones incluidos) en vez de
    /// devolver un monto genérico en efectivo: tiene que ser el 100% de la venta en una sola
    /// operación (no una 2ª NC parcial sobre lo que quedó), el mismo día, y el lote de la venta
    /// original todavía tiene que estar abierto (si ya cerró, la rendición de ese turno ya quedó
    /// fija y no se puede tocar retroactivamente).
    /// </summary>
    public static bool EsReversionCompleta(TipoAnulacion tipo, decimal totalNc, decimal totalOrigen,
        decimal yaAcreditadoAntes, DateTime fechaOrigen, DateTime ahora, bool loteOrigenAbierto) =>
        tipo == TipoAnulacion.Total
        && yaAcreditadoAntes == 0m
        && Math.Abs(totalNc - totalOrigen) <= Tolerancia
        && fechaOrigen.Date == ahora.Date
        && loteOrigenAbierto;

    /// <summary>
    /// Líneas que todavía se pueden anular: las que no fueron acreditadas en una NC previa. Como
    /// la anulación por artículos es siempre de la línea completa, alcanza con el flag por línea.
    /// </summary>
    public static IReadOnlyList<LineaOriginal> LineasAnulables(IEnumerable<LineaOriginal> lineas) =>
        lineas.Where(l => !l.YaAnulada).ToList();

    /// <summary>
    /// Una cantidad a anular es válida si es positiva y no supera lo que todavía queda disponible
    /// de esa línea (la línea completa la primera vez, o el resto si ya se acreditó una parte en
    /// una NC anterior).
    /// </summary>
    public static bool CantidadAcreditable(decimal cantidadPedida, decimal cantidadDisponible) =>
        cantidadPedida > 0m && cantidadPedida <= cantidadDisponible;

    /// <summary>
    /// Importe que corresponde a una cantidad parcial de una línea, en la misma proporción que el
    /// importe total de la línea (precio unitario × cantidad − descuento, ya prorrateado si hubo
    /// descuento). Si <paramref name="cantidad"/> es la cantidad original completa, el resultado es
    /// el importe original sin cambios (salvo el redondeo).
    /// </summary>
    public static decimal ImporteProporcional(decimal importe, decimal cantidad, decimal cantidadPedida) =>
        cantidad == 0m ? 0m : Redondear(importe * cantidadPedida / cantidad);

    /// <summary>
    /// Reparte un importe suelto entre las alícuotas de IVA presentes en el comprobante original,
    /// en la misma proporción en que aparecen ahí.
    ///
    /// Es lo fiscalmente correcto cuando la factura mezcla 21% y 10,5%: acreditar todo a una sola
    /// alícuota devolvería un IVA distinto del que se liquidó. El último tramo absorbe el resto
    /// del redondeo para que la suma dé exactamente el monto pedido.
    /// </summary>
    public static IReadOnlyList<ProrrateoAlicuota> Prorratear(decimal monto, IEnumerable<LineaOriginal> lineas)
    {
        var porAlicuota = lineas
            .GroupBy(l => l.Alicuota)
            .Select(g => new { Alicuota = g.Key, Importe = g.Sum(x => x.Importe) })
            .Where(g => g.Importe > 0)
            .OrderByDescending(g => g.Importe)
            .ToList();

        if (porAlicuota.Count == 0) return Array.Empty<ProrrateoAlicuota>();

        var baseTotal = porAlicuota.Sum(g => g.Importe);
        if (baseTotal <= 0) return Array.Empty<ProrrateoAlicuota>();

        var resultado = new List<ProrrateoAlicuota>();
        var asignado = 0m;
        for (var i = 0; i < porAlicuota.Count; i++)
        {
            var g = porAlicuota[i];
            // El último tramo se calcula por diferencia, no por proporción: así la suma cierra
            // exacta aunque los anteriores se hayan redondeado hacia arriba.
            var importe = i == porAlicuota.Count - 1
                ? Redondear(monto - asignado)
                : Redondear(monto * g.Importe / baseTotal);
            asignado += importe;
            if (importe > 0) resultado.Add(new ProrrateoAlicuota(g.Alicuota, importe));
        }
        return resultado;
    }

    /// <summary>
    /// Reparte un importe de percepción entre IVA 21%/10,5%/IIBB en la misma proporción en que
    /// aparecen en la factura original (<paramref name="baseIva21"/>/<paramref name="baseIva105"/>/
    /// <paramref name="baseIibb"/> = lo que tenía la factura de cada una). El último tramo (IIBB)
    /// se calcula por diferencia, mismo criterio que <see cref="Prorratear"/>, para que la suma
    /// cierre exacta contra <paramref name="monto"/>.
    ///
    /// Lo usa una "Anulación total" para acreditar también la percepción que le queda pendiente al
    /// comprobante original, algo que las líneas de detalle no pueden reflejar porque la percepción
    /// vive en la cabecera, no en ninguna línea (ver NotaCreditoService.EmitirAsync).
    /// </summary>
    public static (decimal Iva21, decimal Iva105, decimal Iibb) RepartirPercepcion(
        decimal monto, decimal baseIva21, decimal baseIva105, decimal baseIibb)
    {
        var baseTotal = baseIva21 + baseIva105 + baseIibb;
        if (monto <= 0m || baseTotal <= 0m) return (0m, 0m, 0m);

        var iva21 = Redondear2(monto * baseIva21 / baseTotal);
        var iva105 = Redondear2(monto * baseIva105 / baseTotal);
        var iibb = Redondear2(monto - iva21 - iva105);
        return (iva21, iva105, iibb);
    }

    private static decimal Redondear2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
