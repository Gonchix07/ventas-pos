namespace Pos.Domain.Services;

/// <summary>
/// Reglas puras para la contingencia CAEA: a qué quincena/período corresponde una fecha, y si un
/// CAEA cargado sigue vigente ese día. El CAEA en sí (el valor que da ARCA) no se calcula acá —
/// se pide con conexión (FECAEASolicitar) y se carga a mano; esto solo resuelve CUÁL corresponde
/// usar y cuándo.
/// </summary>
public static class CaeaReglas
{
    public record Periodo(int Anio, int Mes, int Orden);

    /// <summary>Año+mes+quincena (1 = del 1 al 15, 2 = del 16 a fin de mes) a los que corresponde
    /// facturar contra el CAEA vigente ese día.</summary>
    public static Periodo PeriodoDe(DateTime fecha) => new(fecha.Year, fecha.Month, fecha.Day <= 15 ? 1 : 2);

    /// <summary>Un CAEA cargado es válido para una fecha si esa fecha cae dentro de su vigencia
    /// (inclusive en ambos extremos — así lo informa ARCA en FchVigDesde/FchVigHasta).</summary>
    public static bool Vigente(DateTime fecha, DateTime vigenciaDesde, DateTime vigenciaHasta) =>
        fecha.Date >= vigenciaDesde.Date && fecha.Date <= vigenciaHasta.Date;
}
