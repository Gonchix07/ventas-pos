namespace Pos.Application.Abm;

/// <summary>
/// Descuento por medio de pago (y, si es tarjeta, por una cantidad de cuotas puntual). Se aplica en
/// la pantalla de cobro, no en el carrito — ver Pos.Domain.Entities.OfertaMedioPago.
/// IdPlanCuota null = aplica en cualquier cantidad de cuotas de ese medio.
/// </summary>
public record OfertaMedioPagoDto(int IdSucursal, int IdOfertaMedioPago, string Descripcion,
    int IdMedioPago, string? MedioPagoDescripcion, int? IdPlanCuota, string? PlanCuotaDescripcion,
    decimal Porcentaje, decimal TopeMaximo, bool Activo, DateTime FechaInicio, DateTime FechaFin);

public record OfertaMedioPagoInput(string Descripcion, int IdMedioPago, int? IdPlanCuota,
    decimal Porcentaje, decimal TopeMaximo, bool Activo, DateTime FechaInicio, DateTime FechaFin);

public interface IOfertaMedioPagoAdminService
{
    Task<IReadOnlyList<OfertaMedioPagoDto>> GetAllAsync(int idSucursal, CancellationToken ct = default);
    Task<int> CreateAsync(int idSucursal, OfertaMedioPagoInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int idSucursal, int id, OfertaMedioPagoInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int idSucursal, int id, CancellationToken ct = default);
}
