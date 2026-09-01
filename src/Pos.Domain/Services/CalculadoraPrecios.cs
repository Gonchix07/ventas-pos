using Pos.Domain.Enums;

namespace Pos.Domain.Services;

/// <summary>Precio candidato proveniente de una lista, para resolver por prioridad.</summary>
/// <param name="IdListaPrecio">Lista de origen: se propaga al resultado para dejar trazado de qué
/// lista salió el precio cobrado (la caja lo usa para marcar los precios de folder/promoción).</param>
public record CandidatoPrecio(
    TipoListaPrecio Tipo, int Prioridad, DateTime? Desde, DateTime? Hasta,
    decimal PrecioFinal, decimal ImpuestoInterno, int IdListaPrecio = 0);

/// <summary>
/// Precio/descuento propio del cliente (opcional). A pesar del nombre, <see cref="PrecioListaConvenio"/>
/// puede venir de la lista de un Convenio explícito O de la lista de su tipo de tarjeta (resguardo
/// cuando no tiene Convenio propio — ver PricingService.ResolverPrecioAsync); <see cref="DescuentoPorc"/>
/// solo existe si hay un Convenio real (0 si el origen fue solo la tarjeta).
/// </summary>
/// <param name="IdListaPrecio">Lista de origen del precio propio, si se encontró precio ahí para el artículo.</param>
public record ConvenioInfo(decimal DescuentoPorc, decimal? PrecioListaConvenio, int? IdListaPrecio = null);

/// <param name="IdListaPrecio">Lista de la que sale el precio a cobrar.</param>
/// <param name="AplicoConvenio">
/// false cuando el cliente tiene convenio pero NO se le aplicó (precio de folder, que no acumula).
/// </param>
/// <param name="PrecioBase">
/// El precio de lista que "le corresponde" a ESTE cliente, SIN el % de convenio/campaña todavía —
/// es la lista de su tarjeta/convenio si tiene una propia, o si no el mismo PrecioVigente. Es lo
/// que Caja tiene que mostrar en la columna "Precio" (no PrecioVigente a secas, que es la lista
/// genérica y puede no ser la que ese cliente realmente ve): el % de descuento del convenio y la
/// campaña se calcula y se muestra siempre sobre PrecioBase, nunca sobre PrecioVigente.
/// </param>
public record ResultadoPrecio(
    bool Encontrado, decimal PrecioVigente, decimal ImpuestoInterno, decimal PrecioConvenio,
    int? IdListaPrecio = null, bool AplicoConvenio = false, decimal PrecioBase = 0m);

/// <summary>
/// Resuelve el precio de una presentación aplicando la prioridad de listas del SRS:
/// Folder &gt; Temporal vigente &gt; Base; a igual tipo, mayor Prioridad gana.
/// Lógica pura: no accede a base de datos.
/// </summary>
public static class CalculadoraPrecios
{
    private static int Rango(TipoListaPrecio t) => t switch
    {
        TipoListaPrecio.Folder => 3,
        TipoListaPrecio.Temporal => 2,
        _ => 1 // Base
    };

    private static bool Vigente(CandidatoPrecio c, DateTime fecha)
    {
        // Sólo las listas temporales están sujetas a vigencia.
        if (c.Tipo != TipoListaPrecio.Temporal) return true;
        if (c.Desde.HasValue && fecha.Date < c.Desde.Value.Date) return false;
        if (c.Hasta.HasValue && fecha.Date > c.Hasta.Value.Date) return false;
        return true;
    }

    /// <param name="campaniaDescuentoPorc">% de descuento de una campaña de puntos-app vigente para
    /// el cliente/local (0 si no hay). Se SUMA al % del convenio (a pedido del negocio: convenio y
    /// campaña acumulan) antes de aplicarse — mismo tratamiento que el convenio en todo lo demás,
    /// incluida la regla de que un precio de folder no acumula con nada.</param>
    public static ResultadoPrecio Resolver(
        IEnumerable<CandidatoPrecio> candidatos, DateTime fecha, ConvenioInfo? convenio = null,
        decimal campaniaDescuentoPorc = 0m)
    {
        var ganador = candidatos
            .Where(c => Vigente(c, fecha))
            .OrderByDescending(c => Rango(c.Tipo))
            .ThenByDescending(c => c.Prioridad)
            .ThenByDescending(c => c.Desde ?? DateTime.MinValue)
            .FirstOrDefault();

        if (ganador is null)
            return new ResultadoPrecio(false, 0m, 0m, 0m, PrecioBase: 0m);

        var precioVigente = ganador.PrecioFinal;
        var idListaGanadora = ganador.IdListaPrecio == 0 ? (int?)null : ganador.IdListaPrecio;

        // Un precio de folder es una promoción y NO acumula con el convenio ni con la campaña de
        // puntos-app: se cobra tal cual, sin lista propia ni descuento adicional (regla del negocio).
        // Vale también para el convenio que solo tiene descuento %: antes ese caso lo aplicaba sobre
        // el folder y el que tenía lista propia no, así que el mismo cliente pagaba distinto según
        // cómo estuviera armado su convenio.
        var campaniaPorc = Math.Clamp(campaniaDescuentoPorc, 0m, 100m);
        var tienePrecioListaConvenio = convenio?.PrecioListaConvenio is not null;
        var descPorc = Math.Clamp((convenio?.DescuentoPorc ?? 0m) + campaniaPorc, 0m, 100m);

        if (ganador.Tipo == TipoListaPrecio.Folder || (!tienePrecioListaConvenio && descPorc <= 0m))
            return new ResultadoPrecio(true, precioVigente, ganador.ImpuestoInterno, precioVigente,
                idListaGanadora, AplicoConvenio: false, PrecioBase: precioVigente);

        var baseConv = convenio?.PrecioListaConvenio ?? precioVigente;
        var precioConvenio = Redondear(baseConv * (1 - descPorc / 100m));
        // Si el precio base salió de la lista del convenio, esa es la lista que se cobró.
        var idLista = tienePrecioListaConvenio ? convenio!.IdListaPrecio : idListaGanadora;

        return new ResultadoPrecio(true, precioVigente, ganador.ImpuestoInterno, precioConvenio,
            idLista, AplicoConvenio: true, PrecioBase: baseConv);
    }

    private static decimal Redondear(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}
