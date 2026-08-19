namespace Pos.Domain.Services;

/// <summary>
/// Ids de numeradores en la tabla <c>Numeros</c>. La clave es <c>IdNumero</c> (con
/// <c>IdPuntoVenta</c> como columna informativa), así que un mismo punto de venta puede tener
/// varias series. Las notas de crédito llevan la suya: ante ARCA la numeración de NC es
/// independiente de la de facturas, y mezclarlas dejaría huecos en ambas.
/// </summary>
public static class NumeradorIds
{
    /// <summary>Separación entre familias de numeradores. Un punto de venta nunca llega a 100.000.</summary>
    private const int OffsetNotaCredito = 100_000;

    public static int Factura(int idPuntoVenta) => idPuntoVenta;

    public static int NotaCredito(int idPuntoVenta) => OffsetNotaCredito + idPuntoVenta;
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
    /// Líneas que todavía se pueden anular: las que no fueron acreditadas en una NC previa. Como
    /// la anulación por artículos es siempre de la línea completa, alcanza con el flag por línea.
    /// </summary>
    public static IReadOnlyList<LineaOriginal> LineasAnulables(IEnumerable<LineaOriginal> lineas) =>
        lineas.Where(l => !l.YaAnulada).ToList();

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
}
