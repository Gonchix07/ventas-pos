namespace Pos.Api.Common;

/// <summary>
/// Headers de seguridad HTTP aplicados a TODA respuesta (incluidas las de error, que ya salen
/// del ExceptionMiddleware). Se agrega lo más arriba posible del pipeline para que ningún camino
/// de respuesta se lo salte.
///
/// La API es principalmente JSON (el navegador no ejecuta nada de un `application/json`), pero
/// estos headers igual importan para: la página HTML de Swagger (solo Development u
/// Swagger:Enabled), cualquier error/redirect servido como HTML por el propio Kestrel/IIS, y
/// como defensa en profundidad si algún día se sirve contenido HTML desde acá.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var headers = ctx.Response.Headers;

        // Evita que el navegador "adivine" un content-type distinto al declarado (mitiga XSS por
        // MIME-sniffing si algún endpoint devolviera texto interpretable como HTML/JS).
        headers["X-Content-Type-Options"] = "nosniff";
        // Nada de esta API está pensado para embeberse en un <iframe> — bloquea clickjacking.
        headers["X-Frame-Options"] = "DENY";
        // No filtrar la URL completa (con querystrings/tokens en path) a terceros al navegar afuera.
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        // Sin uso de geolocalización/cámara/micrófono/etc. desde ninguna página servida acá.
        headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";

        // CSP: se omite para /swagger (la UI de Swashbuckle necesita estilos/scripts inline y
        // recursos propios; restringirla ahí es fácil de romper y da poco valor real en una
        // herramienta de desarrollo). Para el resto (JSON puro, no debería ejecutar nada en el
        // navegador) va lo más restrictivo posible.
        if (!ctx.Request.Path.StartsWithSegments("/swagger"))
        {
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        }

        await _next(ctx);
    }
}
