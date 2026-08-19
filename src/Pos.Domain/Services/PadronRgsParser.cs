using System.Globalization;

namespace Pos.Domain.Services;

/// <summary>
/// Parser del padrón de percepción de Ingresos Brutos (archivo "PadronRGSPer" del régimen general).
/// Es texto plano, una línea por CUIT, campos separados por punto y coma:
/// <code>P;24072026;01082026;31082026;20000000028;D;N;N;0,00;00;</code>
/// De las 11 columnas solo interesan la 5 (CUIT) y la 9 (alícuota de percepción, con coma decimal).
/// </summary>
public static class PadronRgsParser
{
    public const int ColumnaCuit = 4;        // 0-based
    public const int ColumnaPercepcion = 8;  // 0-based
    private const int ColumnasMinimas = 9;

    public readonly record struct Fila(string Cuit, decimal Percepcion);

    /// <summary>
    /// Devuelve false (sin lanzar) si la línea está vacía, tiene menos columnas de las esperadas,
    /// el CUIT no son 11 dígitos o la alícuota no es un número. El importador cuenta esas líneas
    /// como inválidas en vez de abortar: un padrón de millones de filas no puede caerse por una.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> linea, out Fila fila)
    {
        fila = default;
        var texto = linea.Trim();
        if (texto.IsEmpty) return false;

        var campos = texto.ToString().Split(';');
        if (campos.Length < ColumnasMinimas) return false;

        if (!EsCuit(campos[ColumnaCuit], out var cuit)) return false;

        // La alícuota viene con coma decimal ("2,50"); se fuerza es-AR para no depender de la
        // cultura del servidor, donde "2,50" se leería como 250.
        var percepcionTexto = campos[ColumnaPercepcion].Trim().Replace('.', ',');
        if (!decimal.TryParse(percepcionTexto, NumberStyles.Number,
                CultureInfo.GetCultureInfo("es-AR"), out var percepcion))
            return false;
        if (percepcion < 0) return false;

        fila = new Fila(cuit, percepcion);
        return true;
    }

    /// <summary>Largo fijo del CUIT al principio de cada línea del padrón de excepción de IVA.</summary>
    public const int LargoCuit = 11;

    /// <summary>
    /// Padrón de excepción de percepción de IVA: NO viene separado por delimitadores, es ancho fijo.
    /// El CUIT son los primeros 11 caracteres de la línea y el resto (lo que haya) se ignora.
    /// </summary>
    public static bool TryParseCuitExcepcionIva(ReadOnlySpan<char> linea, out string cuit)
    {
        cuit = "";
        var texto = linea.TrimStart();
        if (texto.Length < LargoCuit) return false;

        return EsCuit(texto[..LargoCuit].ToString(), out cuit);
    }

    private static bool EsCuit(string campo, out string cuit)
    {
        cuit = campo.Trim();
        if (cuit.Length == 11 && cuit.All(char.IsDigit)) return true;
        cuit = "";
        return false;
    }
}
