namespace Pos.Domain.Services;

/// <summary>
/// Resiliencia mínima para llamadas a servicios externos (fiscal ARCA, pagos MODO/MP, impresora
/// fiscal): timeout por intento + reintentos con backoff simple para fallas transitorias.
///
/// Hoy los adaptadores (mocks en Pos.Infrastructure.Adapters) son instantáneos y nunca tardan ni
/// fallan por red — pero el día que se conecten los servicios reales, una llamada colgada (o una
/// falla transitoria de red) no debe poder bloquear indefinidamente una transacción de BD que
/// sostiene un lock pesimista (ver <c>Numeros</c> en FacturacionService). Esto es esa red de
/// seguridad, sin agregar una dependencia externa (Polly) para un problema acotado. Vive en
/// Domain (no en Infrastructure) porque es lógica pura de coordinación (TPL/BCL), sin ninguna
/// dependencia de EF/HTTP — eso permite testearla con las mismas herramientas que el resto de
/// las reglas de dominio.
///
/// No usar para operaciones NO idempotentes salvo que el llamador ya maneje idempotencia (ej.
/// <c>SolicitudPago.IdempotencyKey</c> en Pos.Application.Abstractions.Payments): reintentar un
/// cobro que en realidad sí se procesó del lado del proveedor, pero cuya respuesta se perdió por
/// timeout, solo es seguro si el proveedor deduplica por esa clave.
/// </summary>
public static class ResilientCall
{
    /// <summary>Un solo intento con timeout — para operaciones que YA tienen su propia lógica de
    /// reintento de negocio (ej. el loop de CAE/CAEA en FacturacionService), donde agregar reintentos
    /// acá duplicaría esa lógica.</summary>
    public static Task<T> ConTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operacion, TimeSpan timeout, CancellationToken ct) =>
        ConTimeoutYReintentosAsync(operacion, timeout, maxIntentos: 1, esperaEntreIntentos: TimeSpan.Zero, ct);

    /// <summary>Reintentos con backoff fijo para operaciones sin lógica de reintento propia
    /// (ej. cobrar/anular un pago).</summary>
    public static async Task<T> ConTimeoutYReintentosAsync<T>(
        Func<CancellationToken, Task<T>> operacion, TimeSpan timeout, int maxIntentos,
        TimeSpan esperaEntreIntentos, CancellationToken ct)
    {
        Exception ultimaFalla = new TimeoutException("No se realizó ningún intento.");
        for (var intento = 1; intento <= maxIntentos; intento++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await operacion(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Se cortó por ESTE timeout, no porque el caller haya cancelado el request original.
                ultimaFalla = new TimeoutException(
                    $"La operación externa no respondió dentro de {timeout.TotalSeconds:0} segundos.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ultimaFalla = ex;
            }

            if (intento < maxIntentos)
                await Task.Delay(esperaEntreIntentos, ct);
        }
        throw ultimaFalla;
    }
}
