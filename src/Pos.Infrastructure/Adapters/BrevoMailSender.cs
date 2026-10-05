using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions;
using Pos.Application.Common;

namespace Pos.Infrastructure.Adapters;

/// <summary>
/// Configuración del envío de mails (sección "Mail" de appsettings / variables de entorno
/// Mail__Brevo__ApiKey, Mail__FromEmail, Mail__FromName). La API key es un secreto: va en
/// user-secrets (Development) o en variable de entorno del servidor, nunca en appsettings.json.
/// </summary>
public class BrevoMailOptions
{
    public string ApiKey { get; set; } = "";
    /// <summary>Remitente verificado en Brevo (el mismo que usa la app de Gift Cards).</summary>
    public string FromEmail { get; set; } = "info@hergo.com.ar";
    public string FromName { get; set; } = "HERGO";
}

/// <summary>
/// Envío transaccional por la API REST de Brevo (POST /v3/smtp/email), igual que la función
/// send-giftcard de la app de Gift Cards. Si falta la API key falla con un error claro en vez de
/// "enviar" en silencio: el cajero tiene que saber que el cliente NO recibió la factura.
/// </summary>
public class BrevoMailSender : IMailSender
{
    public const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly HttpClient _http;
    private readonly BrevoMailOptions _options;
    private readonly ILogger<BrevoMailSender> _log;

    public BrevoMailSender(HttpClient http, BrevoMailOptions options, ILogger<BrevoMailSender> log)
    {
        _http = http;
        _options = options;
        _log = log;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.FromEmail))
            throw new DomainException("MAIL_NO_CONFIGURADO",
                "El envío de mails no está configurado en el servidor (falta la API key de Brevo). Avisá a Sistemas.");

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = _options.FromName, email = _options.FromEmail },
                to = new[] { new { email = to } },
                subject,
                htmlContent = htmlBody
            })
        };
        req.Headers.Add("api-key", _options.ApiKey);
        req.Headers.Add("accept", "application/json");

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            _log.LogError(ex, "Brevo: no se pudo contactar a la API al enviar a {To}.", to);
            throw new DomainException("MAIL_ERROR", "No se pudo conectar con el servicio de mail. Probá de nuevo en un momento.");
        }

        using (resp)
        {
            if (resp.IsSuccessStatusCode) return;

            // El detalle de Brevo (puede nombrar la API key mal configurada, el remitente no verificado,
            // etc.) va al log del servidor, no a la pantalla del cajero.
            var detalle = await resp.Content.ReadAsStringAsync(ct);
            _log.LogError("Brevo respondió {Status} al enviar a {To}: {Detalle}", (int)resp.StatusCode, to, detalle);
            throw new DomainException("MAIL_ERROR",
                $"El servicio de mail rechazó el envío (código {(int)resp.StatusCode}). Avisá a Sistemas si se repite.");
        }
    }
}
