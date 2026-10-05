using System.Text.RegularExpressions;

namespace Pos.Domain.Services;

/// <summary>
/// Validación de formato de mail para el envío de comprobantes. Lógica pura.
/// Es deliberadamente práctica y no la RFC completa: un solo "@", sin espacios ni caracteres de
/// control (esto también corta cualquier intento de inyectar saltos de línea en cabeceras), dominio
/// con al menos un punto, sin puntos consecutivos ni al borde, y un TLD de 2+ letras. El frontend
/// aplica la misma regla (EnviarMailModal.tsx) pero la verdad la decide el backend.
/// </summary>
public static class EmailReglas
{
    /// <summary>Largo máximo = Cliente.Email (HasMaxLength 120), para poder guardarlo en la ficha.</summary>
    public const int LargoMaximo = 120;

    // Parte local: los caracteres "atext" de la RFC 5322 (sin comillas ni comentarios). Dominio:
    // etiquetas alfanuméricas con guiones internos, separadas por punto, y TLD de 2+ letras. No admite
    // dominios internacionalizados con acentos/ñ: para facturas es preferible rechazar a mandar a una
    // dirección que el servicio de mail no va a poder entregar.
    private static readonly Regex Formato = new(
        @"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@([A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Mail normalizado (sin espacios al borde) o null si está vacío.</summary>
    public static string? Normalizar(string? email)
    {
        var e = email?.Trim();
        return string.IsNullOrEmpty(e) ? null : e;
    }

    public static bool EsValido(string? email)
    {
        var e = Normalizar(email);
        if (e is null || e.Length > LargoMaximo) return false;
        if (e.Any(char.IsControl)) return false;
        if (!Formato.IsMatch(e)) return false;

        // Lo que la regex no cubre: puntos consecutivos o al borde de la parte local.
        var local = e[..e.IndexOf('@')];
        if (e.Contains("..", StringComparison.Ordinal)) return false;
        if (local.StartsWith('.') || local.EndsWith('.')) return false;
        return true;
    }
}
