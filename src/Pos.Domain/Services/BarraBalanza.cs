namespace Pos.Domain.Services;

/// <summary>Lo que trae una etiqueta de balanza: qué artículo es y cuánto pesa.</summary>
public record BarraPesada(string CodigoArticulo, decimal Peso);

/// <summary>
/// Códigos de barra de balanza (Kretz y compatibles). Formato EAN-13 de artículo pesable:
/// <c>2</c> + 6 dígitos de código de producto (PLU) + peso + dígito verificador.
///
/// Sobre el peso hay dos lecturas posibles y se resuelven mirando el verificador:
/// si los 12 primeros dígitos cierran el check digit EAN-13, entonces el 13º es el verificador y
/// el peso son los 5 dígitos anteriores expresados en GRAMOS (03920 = 3,920 kg); si no cierra,
/// se toman los 6 dígitos como 2 enteros + 4 decimales (039206 = 3,9206 kg).
/// La etiqueta real de referencia (2070522039206, PLU 70522) cierra el verificador y su importe
/// impreso ($35.279,6 = 3,920 × $8.999,9) confirma que el peso es 3,920 kg y no 3,9206.
/// </summary>
public static class BarraBalanza
{
    /// <summary>Con qué dígito arranca una barra de balanza.</summary>
    public const char Prefijo = '2';
    public const int Largo = 13;

    public static bool TryParse(string? barra, out BarraPesada resultado)
    {
        resultado = default!;
        var b = (barra ?? "").Trim();
        if (b.Length != Largo || b[0] != Prefijo) return false;
        foreach (var c in b) if (c is < '0' or > '9') return false;

        var codigo = b.Substring(1, 6);

        decimal peso;
        if (VerificadorEan13(b[..12]) == b[12] - '0')
        {
            // 5 dígitos en gramos (el 13º es el verificador).
            peso = int.Parse(b.Substring(7, 5)) / 1000m;
        }
        else
        {
            // 2 dígitos de kilos + 4 decimales.
            peso = int.Parse(b.Substring(7, 2)) + int.Parse(b.Substring(9, 4)) / 10000m;
        }

        if (peso <= 0m) return false;
        resultado = new BarraPesada(codigo, peso);
        return true;
    }

    /// <summary>Dígito verificador EAN-13 de los primeros 12 dígitos.</summary>
    public static int VerificadorEan13(string doce)
    {
        var suma = 0;
        for (int i = 0; i < doce.Length; i++)
            suma += (doce[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - suma % 10) % 10;
    }

    /// <summary>
    /// El código del artículo puede estar cargado con o sin los ceros a la izquierda que completan
    /// los 6 dígitos de la barra (la etiqueta imprime "PLU:70522" y la barra lleva "070522").
    /// </summary>
    public static IReadOnlyList<string> CodigosPosibles(string codigoBarra)
    {
        var sinCeros = codigoBarra.TrimStart('0');
        if (sinCeros.Length == 0) sinCeros = "0";
        return sinCeros == codigoBarra ? new[] { codigoBarra } : new[] { codigoBarra, sinCeros };
    }
}
