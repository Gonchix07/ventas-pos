using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;

namespace Pos.Api.Common;

/// <summary>Traduce excepciones a la envoltura uniforme ApiResult.Fail.</summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (ValidationException ex)
        {
            var msg = string.Join("; ", ex.Errors.Select(e => e.ErrorMessage));
            await WriteAsync(ctx, StatusCodes.Status400BadRequest, "VALIDACION", msg);
        }
        catch (DomainException ex)
        {
            await WriteAsync(ctx, StatusCodes.Status409Conflict, ex.Code, ex.Message);
        }
        catch (AccesoDenegadoException ex)
        {
            await WriteAsync(ctx, StatusCodes.Status403Forbidden, ex.Code, ex.Message);
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex)
        {
            // Cubre, entre otros casos, el límite de tamaño de request (Kestrel.MaxRequestBodySize,
            // ver Program.cs): sin este catch, caía en el 500 genérico de abajo con un mensaje que
            // no explica nada al cliente.
            var code = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? "SOLICITUD_DEMASIADO_GRANDE"
                : "SOLICITUD_INVALIDA";
            var message = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? "La solicitud supera el tamaño máximo permitido."
                : "La solicitud es inválida.";
            await WriteAsync(ctx, ex.StatusCode, code, message);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Red de seguridad: los puntos conocidos de esta carrera (agregar/anular/cambiar
            // cantidad de línea sobre la misma operación) ya se serializan con un lock explícito
            // (ver RecursoLockHelper en CajaService), pero se deja este catch para no exponer un 500
            // crudo si aparece en algún otro camino de escritura concurrente no cubierto todavía.
            _logger.LogWarning(ex, "Concurrencia: la fila ya no estaba en el estado esperado");
            await WriteAsync(ctx, StatusCodes.Status409Conflict, "MODIFICADO_CONCURRENTEMENTE",
                "Estos datos fueron modificados por otra acción justo antes. Volvé a intentar.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no controlado");
            await WriteAsync(ctx, StatusCodes.Status500InternalServerError, "ERROR_INTERNO",
                "Ocurrió un error inesperado.");
        }
    }

    private static Task WriteAsync(HttpContext ctx, int status, string code, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var payload = new ApiResult<object>
        {
            Ok = false,
            Error = new ApiError(code, message)
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return ctx.Response.WriteAsync(json);
    }
}
