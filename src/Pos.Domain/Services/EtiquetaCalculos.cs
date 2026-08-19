namespace Pos.Domain.Services;

/// <summary>
/// Cálculos para la etiqueta de precios (fleje/A4/A5). Verificados contra plantillas reales:
/// "precio por Kg/Lt" = precio mostrado ÷ contenido neto de UNA unidad (no del bulto);
/// "precio sin impuestos nacionales" = neto de IVA (más el impuesto interno, si lo hubiera).
/// </summary>
public static class EtiquetaCalculos
{
    /// <summary>
    /// Precio normalizado por unidad de medida (Kg/Lt). Null si el artículo no tiene definida
    /// unidad de medida o contenido neto (no aplica esa línea en la etiqueta).
    /// </summary>
    public static decimal? PrecioPorUnidadMedida(decimal precioMostrado, decimal? contenidoNetoUnitario)
    {
        if (contenidoNetoUnitario is null || contenidoNetoUnitario <= 0) return null;
        return Math.Round(precioMostrado / contenidoNetoUnitario.Value, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Precio sin los impuestos nacionales (IVA + impuesto interno) que ya están incluidos en
    /// <paramref name="precioMostrado"/>. Reutiliza <see cref="DesglioIva"/> (misma fórmula que
    /// Facturación) sobre el importe neto del impuesto interno.
    /// </summary>
    public static decimal PrecioSinImpuestosNacionales(decimal precioMostrado, decimal impuestoInterno, decimal alicuotaIva)
    {
        var baseSinInterno = precioMostrado - impuestoInterno;
        var (neto, _) = DesglioIva.Calcular(baseSinInterno, alicuotaIva);
        return Math.Round(neto, 2, MidpointRounding.AwayFromZero);
    }
}
