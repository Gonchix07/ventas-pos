namespace Pos.Domain.Services;

/// <summary>
/// Los tres tipos de punto de venta que soporta el sistema. No son configurables: cada uno implica
/// un camino de emisión y un dispositivo de impresión distintos, y el código los trata de forma
/// diferente, así que inventar un cuarto tipo desde el ABM no tendría ningún efecto.
/// </summary>
public enum ModalidadPuntoVenta
{
    /// <summary>Va a ARCA (CAE/CAEA) e imprime en la comandera local.</summary>
    Electronica = 1,
    /// <summary>Emite por la DLL de Hasar contra una impresora fiscal en red (requiere su IP).</summary>
    Fiscal = 2,
    /// <summary>Documento local, sin valor fiscal; imprime en la comandera local.</summary>
    Presupuesto = 3,
}

/// <summary>
/// Catálogo fijo de tipos de punto de venta. El id de la modalidad ES el <c>IdTipoPuntoVenta</c>
/// de la tabla por sucursal (1/2/3), que es como ya venían cargados los datos reales.
/// </summary>
public static class TiposPuntoVentaFijos
{
    /// <param name="TipoArca">Marca de integración con ARCA; solo la electrónica emite contra ellos.</param>
    /// <param name="Detalle">Texto que explica el tipo en el ABM (una sola fuente de verdad).</param>
    public record Definicion(ModalidadPuntoVenta Modalidad, string Descripcion, string? TipoArca, string Detalle)
    {
        public int Id => (int)Modalidad;
    }

    public static readonly IReadOnlyList<Definicion> Todos = new List<Definicion>
    {
        new(ModalidadPuntoVenta.Electronica, "ELECTRONICA", "ARCA",
            "Va a ARCA (CAE/CAEA) y se imprime en la comandera local."),
        new(ModalidadPuntoVenta.Fiscal, "FISCAL", null,
            "Emite por la DLL de Hasar contra la impresora fiscal: necesita la IP del controlador."),
        new(ModalidadPuntoVenta.Presupuesto, "PRESUPUESTO", null,
            "Documento local sin valor fiscal; se imprime en la comandera local."),
    };

    public static Definicion? Buscar(int idTipoPuntoVenta) =>
        Todos.FirstOrDefault(t => t.Id == idTipoPuntoVenta);

    /// <summary>Solo el punto de venta FISCAL habla con un controlador por red, así que solo él lleva IP.</summary>
    public static bool RequiereIpControlador(int idTipoPuntoVenta) =>
        idTipoPuntoVenta == (int)ModalidadPuntoVenta.Fiscal;
}
