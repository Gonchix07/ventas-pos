using Pos.Application.Abstractions.Payments;
using Pos.Domain.Enums;

namespace Pos.Infrastructure.Adapters;

/// <summary>
/// Cobro MANUAL: no hay dispositivo ni API que aprobar, el cajero registra lo que recibió
/// (efectivo, una transferencia que ya vio acreditada, etc.). Siempre aprueba — es el único
/// adaptador que en producción tampoco va a llamar a nada externo.
/// </summary>
public class ManualPaymentProvider : IPaymentProvider
{
    public CanalCobro Canal => CanalCobro.Manual;

    public Task<ResultadoPago> CobrarAsync(SolicitudPago req, CancellationToken ct)
        => Task.FromResult(new ResultadoPago(true, $"MANUAL-{req.IdempotencyKey}", "REGISTRADO", null));

    public Task<ResultadoPago> AnularAsync(string idTransaccion, CancellationToken ct)
        => Task.FromResult(new ResultadoPago(true, idTransaccion, "ANULADO", null));

    public Task<EstadoPago> ConsultarAsync(string idTransaccion, CancellationToken ct)
        => Task.FromResult(new EstadoPago("REGISTRADO", null));
}

/// <summary>
/// Cobro por el wrapper local iCARD (posnet / billeteras virtuales). SIMULADO por ahora, igual
/// que el resto de los adaptadores externos.
///
/// Regla de test: los montos terminados en .99 se rechazan, para poder ejercitar la compensación
/// de la saga de facturación. Vive acá y no en el manual porque un cobro manual no puede ser
/// rechazado por un tercero.
/// </summary>
public class MockICardPaymentProvider : IPaymentProvider
{
    public CanalCobro Canal => CanalCobro.ICard;

    public Task<ResultadoPago> CobrarAsync(SolicitudPago req, CancellationToken ct)
    {
        var centavos = Math.Round(req.Monto - Math.Floor(req.Monto), 2);
        if (centavos == 0.99m)
            return Task.FromResult(new ResultadoPago(false, null, null, "Pago rechazado (iCARD mock)"));

        var id = $"ICARD-{req.Fuente}-{req.IdempotencyKey}";
        return Task.FromResult(new ResultadoPago(true, id, "APROBADO", null));
    }

    public Task<ResultadoPago> AnularAsync(string idTransaccion, CancellationToken ct)
        => Task.FromResult(new ResultadoPago(true, idTransaccion, "ANULADO", null));

    public Task<EstadoPago> ConsultarAsync(string idTransaccion, CancellationToken ct)
        => Task.FromResult(new EstadoPago("APROBADO", null));
}

/// <summary>
/// Resuelve el adaptador por el canal configurado en el tipo de pago. Cuando iCARD sea real,
/// solo hay que cambiar el proveedor de ese canal.
/// </summary>
public class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly ManualPaymentProvider _manual = new();
    private readonly MockICardPaymentProvider _icard = new();

    public IPaymentProvider Resolve(CanalCobro canal) => canal switch
    {
        CanalCobro.ICard => _icard,
        _ => _manual,
    };
}
