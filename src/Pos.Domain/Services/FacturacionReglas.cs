namespace Pos.Domain.Services;

/// <summary>
/// Desglose de IVA a partir de un importe QUE YA INCLUYE el impuesto (como se factura en
/// mostrador). Lógica pura: no accede a datos ni a servicios fiscales.
/// </summary>
public static class DesglioIva
{
    public static (decimal Neto, decimal Iva) Calcular(decimal importeConIva, decimal alicuota)
    {
        if (importeConIva == 0) return (0m, 0m);
        if (alicuota <= 0) return (Redondear(importeConIva), 0m);

        var neto = Redondear(importeConIva / (1 + alicuota));
        var iva = Redondear(importeConIva - neto);
        return (neto, iva);
    }

    private static decimal Redondear(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Decide cuándo la emisión debe pasar a contingencia (CAEA) por CAE inaccesible.
/// Regla SRS: "Límite de reintentos por CAE inaccesible" (configurable).
/// </summary>
public static class ReintentosCaeReglas
{
    public static bool DebePasarAContingencia(int intentosRealizados, int limite) =>
        intentosRealizados >= Math.Max(1, limite);
}

/// <summary>
/// Letra del comprobante según la condición del COMPRADOR frente al IVA:
/// Responsable Inscripto y Monotributista → A (IVA discriminado), el resto (Consumidor Final,
/// Exento) → B (precio final, sin discriminar). La condición ya trae la letra cargada en
/// CondicionIva.Letra; esta regla es el fallback y el punto único donde vive la decisión.
/// </summary>
public static class LetraComprobante
{
    public const string A = "A";
    public const string B = "B";

    /// <param name="letraCondicionIva">CondicionIva.Letra del cliente (null = sin cliente).</param>
    public static string Resolver(string? letraCondicionIva) =>
        string.Equals(letraCondicionIva?.Trim(), A, StringComparison.OrdinalIgnoreCase) ? A : B;

    /// <summary>La A identifica obligatoriamente al comprador (CUIT y domicilio).</summary>
    public static bool ExigeIdentificacion(string letra) =>
        string.Equals(letra?.Trim(), A, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Discriminación de IVA por alícuota para el pie de la factura A ("IVA 10.5%: ...", "IVA 21.0%: ...").
/// Se arma desde las líneas del comprobante, que guardan la alícuota con la que se vendió cada una.
/// </summary>
public static class DiscriminacionIva
{
    public record Renglon(decimal Alicuota, decimal Base, decimal Importe);

    public static IReadOnlyList<Renglon> Agrupar(IEnumerable<(decimal Alicuota, decimal Neto, decimal Iva)> lineas) =>
        lineas.GroupBy(l => l.Alicuota)
            .Select(g => new Renglon(g.Key, Math.Round(g.Sum(x => x.Neto), 2, MidpointRounding.AwayFromZero),
                                            Math.Round(g.Sum(x => x.Iva), 2, MidpointRounding.AwayFromZero)))
            .OrderBy(r => r.Alicuota)
            .ToList();
}

/// <summary>Formato de número de comprobante fiscal: PPPP-NNNNNNNN.</summary>
public static class NumeroComprobanteFormatter
{
    public static string Formatear(int puntoVenta, long numero) => $"{puntoVenta:D4}-{numero:D8}";
}

/// <summary>
/// Valida que la suma de los pagos cubra el total a cobrar, con una tolerancia de
/// redondeo (centavos). El sobrante por sobre el total se admite como VUELTO — pero solo si
/// viene de Efectivo: no se puede "dar vuelto" en tarjeta, transferencia u otro medio no efectivo.
/// </summary>
public static class ValidacionPagos
{
    private const decimal ToleranciaCentavos = 0.01m;

    /// <summary>Ya no exige igualdad exacta: alcanza con cubrir el total (el sobrante es vuelto).</summary>
    public static bool CubreElTotal(decimal sumaPagos, decimal total) =>
        sumaPagos - total >= -ToleranciaCentavos;

    /// <summary>La parte no-Efectivo de los pagos nunca puede superar el total (no da vuelto).</summary>
    public static bool NoEfectivoNoSuperaElTotal(decimal sumaNoEfectivo, decimal total) =>
        sumaNoEfectivo - total <= ToleranciaCentavos;

    /// <summary>Vuelto = lo que sobra por sobre el total, nunca negativo.</summary>
    public static decimal CalcularVuelto(decimal sumaPagos, decimal total) =>
        Math.Max(0m, Math.Round(sumaPagos - total, 2, MidpointRounding.AwayFromZero));
}
