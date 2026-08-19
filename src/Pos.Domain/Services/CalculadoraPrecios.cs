using Pos.Domain.Enums;

namespace Pos.Domain.Services;

/// <summary>Precio candidato proveniente de una lista, para resolver por prioridad.</summary>
/// <param name="IdListaPrecio">Lista de origen: se propaga al resultado para dejar trazado de qué
/// lista salió el precio cobrado (la caja lo usa para marcar los precios de folder/promoción).</param>
public record CandidatoPrecio(
    TipoListaPrecio Tipo, int Prioridad, DateTime? Desde, DateTime? Hasta,
    decimal PrecioFinal, decimal ImpuestoInterno, int IdListaPrecio = 0);

/// <summary>Datos del convenio del cliente (opcional).</summary>
/// <param name="IdListaPrecio">Lista propia del convenio, si tiene y si hay precio para el artículo.</param>
public record ConvenioInfo(decimal DescuentoPorc, decimal? PrecioListaConvenio, int? IdListaPrecio = null);

/// <param name="IdListaPrecio">Lista de la que sale el precio a cobrar.</param>
/// <param name="AplicoConvenio">
/// false cuando el cliente tiene convenio pero NO se le aplicó (precio de folder, que no acumula).
/// </param>
public record ResultadoPrecio(
    bool Encontrado, decimal PrecioVigente, decimal ImpuestoInterno, decimal PrecioConvenio,
    int? IdListaPrecio = null, bool AplicoConvenio = false);

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

    public static ResultadoPrecio Resolver(
        IEnumerable<CandidatoPrecio> candidatos, DateTime fecha, ConvenioInfo? convenio = null)
    {
        var ganador = candidatos
            .Where(c => Vigente(c, fecha))
            .OrderByDescending(c => Rango(c.Tipo))
            .ThenByDescending(c => c.Prioridad)
            .ThenByDescending(c => c.Desde ?? DateTime.MinValue)
            .FirstOrDefault();

        if (ganador is null)
            return new ResultadoPrecio(false, 0m, 0m, 0m);

        var precioVigente = ganador.PrecioFinal;
        var idListaGanadora = ganador.IdListaPrecio == 0 ? (int?)null : ganador.IdListaPrecio;

        // Un precio de folder es una promoción y NO acumula con el convenio del cliente: se cobra tal
        // cual, sin su lista ni su descuento (regla del negocio). Vale también para el convenio que
        // solo tiene descuento %: antes ese caso lo aplicaba sobre el folder y el que tenía lista
        // propia no, así que el mismo cliente pagaba distinto según cómo estuviera armado su convenio.
        if (convenio is null || ganador.Tipo == TipoListaPrecio.Folder)
            return new ResultadoPrecio(true, precioVigente, ganador.ImpuestoInterno, precioVigente,
                idListaGanadora, AplicoConvenio: false);

        var baseConv = convenio.PrecioListaConvenio ?? precioVigente;
        var desc = Math.Clamp(convenio.DescuentoPorc, 0m, 100m);
        var precioConvenio = Redondear(baseConv * (1 - desc / 100m));
        // Si el precio base salió de la lista del convenio, esa es la lista que se cobró.
        var idLista = convenio.PrecioListaConvenio is not null ? convenio.IdListaPrecio : idListaGanadora;

        return new ResultadoPrecio(true, precioVigente, ganador.ImpuestoInterno, precioConvenio,
            idLista, AplicoConvenio: true);
    }

    private static decimal Redondear(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}
