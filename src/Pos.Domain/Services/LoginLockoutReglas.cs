namespace Pos.Domain.Services;

/// <summary>
/// Reglas de bloqueo de cuenta por intentos fallidos de login (mitigación de fuerza bruta).
/// Puro dominio: no toca BD ni reloj real, recibe todo por parámetro.
/// </summary>
public static class LoginLockoutReglas
{
    public const int MaxIntentosFallidos = 5;
    public static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(15);

    /// <summary>True si, dado el bloqueo vigente, el usuario todavía no puede intentar loguearse.</summary>
    public static bool EstaBloqueado(DateTime? bloqueadoHasta, DateTime ahoraUtc) =>
        bloqueadoHasta is DateTime hasta && hasta > ahoraUtc;

    /// <summary>Próximo contador de intentos fallidos tras uno nuevo.</summary>
    public static int SiguienteIntento(int intentosFallidosActuales) => intentosFallidosActuales + 1;

    /// <summary>True si con este número de intentos fallidos corresponde bloquear la cuenta.</summary>
    public static bool DebeBloquear(int intentosFallidos) => intentosFallidos >= MaxIntentosFallidos;

    /// <summary>Momento hasta el cual queda bloqueada la cuenta a partir de ahora.</summary>
    public static DateTime CalcularBloqueoHasta(DateTime ahoraUtc) => ahoraUtc.Add(DuracionBloqueo);
}
