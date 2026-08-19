namespace Pos.Application.Caja;

// Retiro del turno: el cajero saca plata de su caja para enviarla (a tesorería, a un depósito,
// etc.). Se registra como un movimiento negativo — igual mecanismo que usa una nota de crédito
// para descontar del esperado — y queda etiquetado con el concepto para que se pueda justificar al
// rendir. Ver RetiroCajaService.
// IdMedioPago null = Efectivo (comportamiento de siempre, así el frontend actual sigue andando sin
// tocar nada); explícito = cualquier otro medio.
public record RetiroEfectivoRequest(decimal Monto, string? Concepto, int? IdMedioPago = null);
public record RetiroEfectivoResponse(int IdSucursal, int IdMovCaja, int IdMedioPago, decimal Monto,
    string? Concepto, DateTime Fecha);

public interface IRetiroCajaService
{
    Task<RetiroEfectivoResponse> RegistrarAsync(int idSucursal, int idCaja, RetiroEfectivoRequest req, CancellationToken ct = default);
}
