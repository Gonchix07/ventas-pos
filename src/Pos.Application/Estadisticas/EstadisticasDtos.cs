namespace Pos.Application.Estadisticas;

/// <summary>Períodos fijos que ofrece el dashboard de Ventas. El rango real (Desde/Hasta) lo resuelve
/// el servicio a partir de este valor — el front no calcula fechas, solo elige la etiqueta.</summary>
public enum PeriodoEstadisticas
{
    Hoy = 0,
    Ultimos7Dias = 1,
    Ultimos30Dias = 2,
    UltimoAnio = 3,
}

public record ResumenVentasDto(decimal TotalVentas, int CantidadTickets, int CantidadClientes,
    decimal TicketPromedio, decimal TotalDescuentos, int CantidadNotasCredito, decimal TotalNotasCredito);

public record FamiliaVendidaDto(int IdFamilia, string Descripcion, decimal Total, decimal Cantidad, decimal Participacion);

/// <summary>Un punto de la serie temporal. <see cref="Etiqueta"/> ya viene formateada (hora del día
/// para "Hoy", fecha para el resto) porque el criterio de agrupación varía según el período.</summary>
public record VentaPorPeriodoDto(string Etiqueta, decimal Total);

public record SectorConsumidoDto(int IdSector, string Descripcion, decimal Total, decimal Cantidad, decimal Participacion);

public record ProductoVendidoDto(int IdArticulo, string CodigoInterno, string Descripcion, decimal Cantidad, decimal Total);

public record TopClienteDto(int? IdCliente, string Descripcion, decimal Total, int CantidadTickets);

/// <summary>
/// Efectividad de una oferta: cuántas líneas de venta la tuvieron aplicada (según el trace de texto
/// que guarda cada línea, ver DetalleOperacion.OfertasAplicadas) y cuánto descuento otorgó. Cuando
/// una línea acumula más de una oferta, el descuento de esa línea se reparte por igual entre todas
/// las que aparecen listadas — el sistema no guarda el monto que aportó cada una por separado.
/// </summary>
public record OfertaEfectividadDto(string Descripcion, int VecesAplicada, decimal DescuentoOtorgado, decimal ImporteAfectado);

public record EstadisticasVentasResponse(
    PeriodoEstadisticas Periodo, DateTime Desde, DateTime Hasta,
    ResumenVentasDto Resumen,
    List<FamiliaVendidaDto> FamiliasMasVendidas,
    List<VentaPorPeriodoDto> Evolucion,
    List<SectorConsumidoDto> SectoresMasConsumidos,
    List<ProductoVendidoDto> ProductosMasVendidos,
    List<TopClienteDto> TopClientes,
    List<OfertaEfectividadDto> Ofertas);

public interface IEstadisticasService
{
    Task<EstadisticasVentasResponse> GetVentasAsync(PeriodoEstadisticas periodo, int? idSucursal, CancellationToken ct = default);
}
