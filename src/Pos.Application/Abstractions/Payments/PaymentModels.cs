using Pos.Domain.Enums;

namespace Pos.Application.Abstractions.Payments;

public record SolicitudPago(
    FuentePago Fuente,
    CanalCobro Canal,
    int IdMedioPago,
    decimal Monto,
    string IdempotencyKey,
    string? Referencia = null);

public record ResultadoPago(bool Aprobado, string? IdTransaccion, string? Autorizacion, string? Error);

public record EstadoPago(string Estado, string? Detalle);

/// <summary>
/// Puerto de proveedor de pago. Hay una implementación por CANAL, no por fuente: lo que cambia
/// entre un pago con tarjeta y uno en efectivo no es la semántica del medio sino por dónde se
/// efectúa (el cajero lo registra a mano, o sale por el posnet vía iCARD).
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Canal que atiende este proveedor (usado para resolver el adaptador correcto).</summary>
    CanalCobro Canal { get; }
    Task<ResultadoPago> CobrarAsync(SolicitudPago req, CancellationToken ct);
    Task<ResultadoPago> AnularAsync(string idTransaccion, CancellationToken ct);
    Task<EstadoPago> ConsultarAsync(string idTransaccion, CancellationToken ct);
}

/// <summary>Resuelve el proveedor de pago según el canal configurado en el tipo de pago.</summary>
public interface IPaymentProviderFactory
{
    IPaymentProvider Resolve(CanalCobro canal);
}
