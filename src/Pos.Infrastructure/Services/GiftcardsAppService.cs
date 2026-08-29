using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions.Giftcards;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>Forma del body 200 de GET /api/validar-giftcard (ver validar-giftcard.js de giftcards-app).</summary>
file record ValidarGiftcardJson(
    [property: JsonPropertyName("codigo")] string? Codigo,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("saldo")] decimal? Saldo,
    [property: JsonPropertyName("monto_max")] decimal? MontoMax,
    [property: JsonPropertyName("uso_parcial")] bool? UsoParcial,
    [property: JsonPropertyName("fecha_vencimiento")] DateOnly? FechaVencimiento,
    [property: JsonPropertyName("cliente")] string? Cliente,
    [property: JsonPropertyName("dni")] string? Dni,
    [property: JsonPropertyName("campana")] string? Campana,
    [property: JsonPropertyName("comercio")] string? Comercio);

/// <summary>Forma del body 201 de POST /api/usar-giftcard.</summary>
file record UsarGiftcardJson(
    [property: JsonPropertyName("transaccion_id")] string? TransaccionId,
    [property: JsonPropertyName("saldo_resultante")] decimal? SaldoResultante,
    [property: JsonPropertyName("estado")] string? Estado);

file record ErrorRespuestaJson([property: JsonPropertyName("error")] string? Error);

/// <summary>
/// Llama a giftcards-app (GET /api/validar-giftcard, POST /api/usar-giftcard). A diferencia de
/// <see cref="PuntosFidelizacionService"/>, NO es best-effort: <see cref="UsarAsync"/> devuelve
/// Ok=false ante cualquier fallo (config, red, rechazo de negocio) y el llamador debe abortar el
/// cobro — ver <see cref="IGiftcardsAppService"/>.
/// </summary>
public class GiftcardsAppService : IGiftcardsAppService
{
    // Mismo purpose que ConexionGiftcardsAppAdminService (ver AbmServices.cs).
    private const string DataProtectionPurpose = "Pos.ConexionGiftcardsApp";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    // UsarAsync se llama tanto desde el popup de "Confirmar uso" (canje inmediato en Caja, ver
    // CajaController.UsarGiftcard) como en el fallback dentro de la saga de facturación — mismo
    // criterio de reintentos que un proveedor de pago real (ResilientCall), protegido por la
    // idempotencia de usar_giftcard_api del lado de giftcards-app.
    private const int MaxIntentos = 3;
    private static readonly TimeSpan EsperaEntreIntentos = TimeSpan.FromMilliseconds(500);

    private readonly PosDbContext _db;
    private readonly IDataProtector _protector;
    private readonly HttpClient _http;
    private readonly ILogger<GiftcardsAppService> _log;

    public GiftcardsAppService(PosDbContext db, IDataProtectionProvider dataProtection,
        HttpClient http, ILogger<GiftcardsAppService> log)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(DataProtectionPurpose);
        _http = http;
        _log = log;
    }

    private async Task<(Domain.Entities.ConexionGiftcardsApp Config, string ApiKey)?> ConfigValidaAsync(CancellationToken ct)
    {
        var config = await _db.ConexionesGiftcardsApp.AsNoTracking().FirstOrDefaultAsync(ct);
        if (config is null || !config.Habilitada) return null;
        if (string.IsNullOrWhiteSpace(config.UrlBase) || string.IsNullOrEmpty(config.TokenProtegido)) return null;
        return (config, _protector.Unprotect(config.TokenProtegido));
    }

    public async Task<GiftcardConsulta> ValidarAsync(string codigo, CancellationToken ct = default)
    {
        try
        {
            var cfg = await ConfigValidaAsync(ct);
            if (cfg is null)
                return new GiftcardConsulta(false, null, null, null, null, null, null, null, null, null, null,
                    "La integración con giftcards-app no está configurada/habilitada.");
            var (config, apiKey) = cfg.Value;

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{config.UrlBase.TrimEnd('/')}/api/validar-giftcard?codigo={Uri.EscapeDataString(codigo)}");
            req.Headers.Add("X-Api-Key", apiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var resp = await _http.SendAsync(req, cts.Token);
            var bodyText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                var motivo = ExtraerError(bodyText) ?? $"HTTP {(int)resp.StatusCode}";
                return new GiftcardConsulta(false, null, null, null, null, null, null, null, null, null, null, motivo);
            }

            var data = System.Text.Json.JsonSerializer.Deserialize<ValidarGiftcardJson>(bodyText,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            return new GiftcardConsulta(true, data?.Codigo, data?.Cliente, data?.Dni, data?.Campana, data?.Comercio,
                data?.Saldo, data?.MontoMax, data?.UsoParcial, data?.Estado, data?.FechaVencimiento, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo validar la gift card {Codigo} en giftcards-app.", codigo);
            return new GiftcardConsulta(false, null, null, null, null, null, null, null, null, null, null, ex.Message);
        }
    }

    public async Task<ResultadoUsoGiftcard> UsarAsync(string codigo, decimal monto, string cajeroLabel,
        string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var cfg = await ConfigValidaAsync(ct);
            if (cfg is null)
                return new ResultadoUsoGiftcard(false, null, null, null,
                    "La integración con giftcards-app no está configurada/habilitada.");
            var (config, apiKey) = cfg.Value;

            // Reintentable: misma idempotencyKey en cada intento — un timeout que en realidad sí
            // llegó a descontar el saldo del lado de giftcards-app no lo duplica (usar_giftcard_api
            // es idempotente por esa clave).
            HttpResponseMessage resp;
            string bodyText;
            try
            {
                (resp, bodyText) = await ResilientCall.ConTimeoutYReintentosAsync(async ct2 =>
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{config.UrlBase.TrimEnd('/')}/api/usar-giftcard");
                    req.Headers.Add("X-Api-Key", apiKey);
                    req.Content = JsonContent.Create(new
                    {
                        codigo,
                        monto,
                        comercio = config.Comercio,
                        cajero = cajeroLabel,
                        idempotency_key = idempotencyKey,
                    });
                    var r = await _http.SendAsync(req, ct2);
                    var b = await r.Content.ReadAsStringAsync(ct2);
                    return (r, b);
                }, Timeout, MaxIntentos, EsperaEntreIntentos, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "No se pudo cobrar la gift card {Codigo} por ${Monto} en giftcards-app.", codigo, monto);
                return new ResultadoUsoGiftcard(false, null, null, null, ex.Message);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var motivo = ExtraerError(bodyText) ?? $"HTTP {(int)resp.StatusCode}";
                _log.LogWarning(
                    "giftcards-app rechazó el cobro de la gift card {Codigo} por ${Monto}: {Status} {Body}",
                    codigo, monto, (int)resp.StatusCode, bodyText);
                return new ResultadoUsoGiftcard(false, null, null, null, motivo);
            }

            var data = System.Text.Json.JsonSerializer.Deserialize<UsarGiftcardJson>(bodyText,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            return new ResultadoUsoGiftcard(true, data?.TransaccionId, data?.SaldoResultante, data?.Estado, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo cobrar la gift card {Codigo} por ${Monto} en giftcards-app.", codigo, monto);
            return new ResultadoUsoGiftcard(false, null, null, null, ex.Message);
        }
    }

    private static string? ExtraerError(string bodyText)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<ErrorRespuestaJson>(bodyText)?.Error; }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
