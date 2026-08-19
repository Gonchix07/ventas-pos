namespace Pos.Domain.Services;

/// <summary>
/// Reglas puras de validez de un refresh token (rotación de un solo uso + detección de reuso).
/// La orquestación real (buscar por hash, revocar, persistir el nuevo) vive en la capa de
/// infraestructura; acá solo la decisión.
/// </summary>
public static class RefreshTokenReglas
{
    public static bool EstaVencido(DateTime expiraUtc, DateTime ahoraUtc) => expiraUtc <= ahoraUtc;

    /// <summary>True si este token ya fue usado/rotado antes — presentarlo de nuevo es indicio de
    /// robo (alguien más ya lo canjeó), no un simple error de sincronismo del cliente.</summary>
    public static bool YaFueUsado(DateTime? revocadoUtc) => revocadoUtc is not null;

    public static bool EsValido(DateTime? revocadoUtc, DateTime expiraUtc, DateTime ahoraUtc) =>
        !YaFueUsado(revocadoUtc) && !EstaVencido(expiraUtc, ahoraUtc);
}
