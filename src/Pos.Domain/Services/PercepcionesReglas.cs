namespace Pos.Domain.Services;

/// <summary>
/// Percepciones que se agregan al total a cobrar en el momento de facturar:
/// - <b>IVA</b>: 3% sobre el neto de los artículos gravados al 21% + 1% sobre el neto de los
///   gravados al 10,5%, cada una solo si supera su propio piso mínimo (configurable). No aplica si
///   el cliente está en el padrón de excepción de percepción de IVA (eso se resuelve en
///   Infrastructure, acá solo se calculan los montos).
/// - <b>IIBB</b>: según la alícuota que traiga el padrón de Ingresos Brutos para el CUIT del
///   cliente (viene en porcentaje, ej. "2,50" = 2,5%, no como fracción), sobre el neto total de la
///   operación, también con su propio piso mínimo.
/// Cálculo puro — resolver alícuotas de artículo, padrón y mínimos de configuración es
/// responsabilidad de Infrastructure (necesita la BD); acá solo se aplican las reglas.
/// </summary>
public static class PercepcionesReglas
{
    /// <summary>Tasa de percepción de IVA sobre el neto gravado al 21%.</summary>
    public const decimal TasaPercepcionIva21 = 0.03m;
    /// <summary>Tasa de percepción de IVA sobre el neto gravado al 10,5%.</summary>
    public const decimal TasaPercepcionIva105 = 0.01m;

    private static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static decimal AplicarSiSuperaMinimo(decimal importe, decimal minimo)
    {
        var redondeado = Redondear(importe);
        return redondeado > minimo ? redondeado : 0m;
    }

    /// <summary>Percepción de IVA sobre el neto gravado al 21%; 0 si no supera el mínimo.</summary>
    public static decimal CalcularPercepcionIva21(decimal netoGravado21, decimal minimo) =>
        AplicarSiSuperaMinimo(netoGravado21 * TasaPercepcionIva21, minimo);

    /// <summary>Percepción de IVA sobre el neto gravado al 10,5%; 0 si no supera el mínimo.</summary>
    public static decimal CalcularPercepcionIva105(decimal netoGravado105, decimal minimo) =>
        AplicarSiSuperaMinimo(netoGravado105 * TasaPercepcionIva105, minimo);

    /// <summary>
    /// Percepción de IIBB. <paramref name="alicuotaPadronPorcentual"/> es la alícuota tal como la
    /// trae el padrón (en porcentaje: 2,50 = 2,5%, NO 0,025) — 0 o negativa significa que el
    /// cliente no tiene alícuota cargada, y no corresponde percepción.
    /// </summary>
    public static decimal CalcularPercepcionIibb(decimal netoTotalOperacion, decimal alicuotaPadronPorcentual, decimal minimo)
    {
        if (alicuotaPadronPorcentual <= 0) return 0m;
        return AplicarSiSuperaMinimo(netoTotalOperacion * alicuotaPadronPorcentual / 100m, minimo);
    }
}
