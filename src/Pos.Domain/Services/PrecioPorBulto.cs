namespace Pos.Domain.Services;

/// <summary>
/// Derivación del precio de cada presentación a partir de UN solo precio unitario.
///
/// El operador carga el precio de la unidad suelta y cada presentación queda valorizada
/// multiplicando por sus unidades por bulto (un pack de 12 vale 12 veces la unidad). Antes había
/// que cargar el precio de cada presentación a mano, lo que además permitía que quedaran
/// incoherentes entre sí.
/// </summary>
public static class PrecioPorBulto
{
    /// <summary>
    /// Precio de una presentación = precio unitario × unidades por bulto, redondeado a 2 decimales.
    /// Se aplica igual al impuesto interno, que también es un importe por unidad de producto.
    /// </summary>
    public static decimal Calcular(decimal precioUnitario, decimal unidadXBulto)
    {
        if (precioUnitario < 0)
            throw new ArgumentOutOfRangeException(nameof(precioUnitario), "El precio no puede ser negativo.");
        if (unidadXBulto <= 0)
            throw new ArgumentOutOfRangeException(nameof(unidadXBulto), "Las unidades por bulto deben ser mayores a cero.");

        return Math.Round(precioUnitario * unidadXBulto, 2, MidpointRounding.AwayFromZero);
    }
}
