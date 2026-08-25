namespace Pos.Domain.Services;

/// <summary>Redondeo por efectivo según un rango configurable (SRS: "rango de redondeo").</summary>
public static class RedondeoService
{
    /// <summary>
    /// Redondea <paramref name="monto"/> al múltiplo de <paramref name="rango"/> más cercano.
    /// Devuelve el AJUSTE (positivo o negativo) a aplicar, nunca el monto final.
    /// Rango &lt;= 0 desactiva el redondeo (ajuste 0).
    /// </summary>
    public static decimal CalcularAjuste(decimal monto, decimal rango)
    {
        if (rango <= 0) return 0m;
        var redondeado = Math.Round(monto / rango, 0, MidpointRounding.AwayFromZero) * rango;
        return Math.Round(redondeado - monto, 4, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Totales de una operación de caja a partir de las líneas ya resueltas (precio + oferta aplicados).
/// Lógica pura: solo suma, no accede a datos.
/// </summary>
public record TotalOperacion(decimal Bruto, decimal Descuento, decimal Neto);

public static class OperacionTotales
{
    public static TotalOperacion Calcular(IEnumerable<(decimal bruto, decimal descuento)> lineas)
    {
        decimal bruto = 0, desc = 0;
        foreach (var (b, d) in lineas) { bruto += b; desc += d; }
        var neto = Math.Round(bruto - desc, 4, MidpointRounding.AwayFromZero);
        return new TotalOperacion(Math.Round(bruto, 4), Math.Round(desc, 4), neto);
    }
}
