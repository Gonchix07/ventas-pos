namespace Pos.Application.Caja;

// Retiro de efectivo del turno: el cajero saca plata de su caja para enviarla (a tesorería, a un
// depósito, etc.). Se registra como un movimiento negativo en Efectivo — igual mecanismo que usa
// una nota de crédito para descontar del esperado — y queda etiquetado con el concepto para que se
// pueda justificar al rendir. Ver RetiroCajaService.
public record RetiroEfectivoRequest(decimal Monto, string? Concepto);
public record RetiroEfectivoResponse(int IdSucursal, int IdMovCaja, decimal Monto, string? Concepto, DateTime Fecha);

public interface IRetiroCajaService
{
    Task<RetiroEfectivoResponse> RegistrarAsync(int idSucursal, int idCaja, RetiroEfectivoRequest req, CancellationToken ct = default);
}
