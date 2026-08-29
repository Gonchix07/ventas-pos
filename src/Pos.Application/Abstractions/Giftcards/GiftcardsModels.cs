namespace Pos.Application.Abstractions.Giftcards;

/// <summary>Datos de una gift card consultada (sin cobrar) — para que el cajero vea saldo/cliente
/// antes de aplicarla como medio de pago. Null en los campos de datos si <see cref="Error"/> viene
/// con algo (no encontrada, config incompleta, etc.).</summary>
public record GiftcardConsulta(bool Ok, string? Codigo, string? Cliente, string? Comercio,
    decimal? Saldo, decimal? MontoMax, bool? UsoParcial, string? Estado, DateOnly? FechaVencimiento,
    string? Error);

/// <summary>Resultado de cobrar (descontar saldo de) una gift card. A diferencia de
/// <see cref="Pos.Application.Abstractions.Fidelizacion.ResultadoCargaPuntos"/>, esto SÍ mueve plata
/// real de la venta: <see cref="Ok"/>=false debe abortar el pago (ver FacturacionService), nunca es
/// best-effort.</summary>
public record ResultadoUsoGiftcard(bool Ok, string? TransaccionId, decimal? SaldoResultante, string? Estado, string? Error);

/// <summary>
/// Puerto hacia el API de giftcards-app (proyecto externo) — <c>GET /api/validar-giftcard</c> y
/// <c>POST /api/usar-giftcard</c>. A diferencia de <see cref="Pos.Application.Abstractions.Fidelizacion.IPuntosFidelizacionService"/>,
/// NO es best-effort: una gift card es un medio de pago real, un fallo acá tiene que abortar el
/// cobro (ver <see cref="Pos.Infrastructure.Services.FacturacionService"/>). No hay reversión
/// automática desde acá todavía — si la venta se cae DESPUÉS de cobrar la gift card (ej. falla el
/// CAE), la reversión es manual, del lado de giftcards-app (decisión explícita, no una omisión).
/// </summary>
public interface IGiftcardsAppService
{
    Task<GiftcardConsulta> ValidarAsync(string codigo, CancellationToken ct = default);
    Task<ResultadoUsoGiftcard> UsarAsync(string codigo, decimal monto, string cajeroLabel,
        string idempotencyKey, CancellationToken ct = default);
}
