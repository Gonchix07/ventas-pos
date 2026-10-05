using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Abm;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Infrastructure.Adapters;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// ABM de la cuenta de Brevo para enviar mails — ver <see cref="ConexionMail"/>. Singleton, mismo
/// criterio que <see cref="ConexionGiftcardsAppAdminService"/>: la API key se guarda cifrada y nunca
/// vuelve al frontend; guardar con la key vacía conserva la que ya estaba.
/// </summary>
public class ConexionMailAdminService : IConexionMailAdminService
{
    private const string UrlCuenta = "https://api.brevo.com/v3/account";
    private const string UrlRemitentes = "https://api.brevo.com/v3/senders";

    private readonly PosDbContext _db;
    private readonly IDataProtector _protector;
    private readonly HttpClient _http;
    private readonly BrevoMailOptions _servidor;

    public ConexionMailAdminService(PosDbContext db, IDataProtectionProvider dataProtection, HttpClient http,
        BrevoMailOptions servidor)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(BrevoConfigProvider.DataProtectionPurpose);
        _http = http;
        _servidor = servidor;
    }

    public async Task<ConexionMailDto> GetAsync(CancellationToken ct = default)
    {
        var c = await _db.ConexionesMail.AsNoTracking().SingleOrDefaultAsync(ct);
        var tieneServidor = !string.IsNullOrWhiteSpace(_servidor.ApiKey);
        if (c is null) return new ConexionMailDto(_servidor.FromEmail, _servidor.FromName, false, tieneServidor);
        return new ConexionMailDto(
            string.IsNullOrWhiteSpace(c.FromEmail) ? _servidor.FromEmail : c.FromEmail,
            string.IsNullOrWhiteSpace(c.FromName) ? _servidor.FromName : c.FromName,
            !string.IsNullOrEmpty(c.ApiKeyProtegida), tieneServidor);
    }

    public async Task UpdateAsync(ConexionMailInput input, CancellationToken ct = default)
    {
        var from = EmailReglas.Normalizar(input.FromEmail);
        if (!EmailReglas.EsValido(from))
            throw new DomainException("EMAIL_INVALIDO", "El remitente no tiene un formato de mail válido.");

        var c = await _db.ConexionesMail.SingleOrDefaultAsync(ct);
        if (c is null)
        {
            c = new ConexionMail();
            _db.ConexionesMail.Add(c);
        }
        c.FromEmail = from!;
        var nombre = input.FromName?.Trim() ?? "";
        c.FromName = nombre.Length > 80 ? nombre[..80] : nombre;
        var key = input.ApiKey?.Trim();
        if (!string.IsNullOrEmpty(key))
            c.ApiKeyProtegida = _protector.Protect(key);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ProbarConexionResultado> ProbarAsync(ConexionMailInput input, CancellationToken ct = default)
    {
        var apiKey = input.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            var guardada = await _db.ConexionesMail.AsNoTracking().SingleOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(guardada?.ApiKeyProtegida))
            {
                try { apiKey = _protector.Unprotect(guardada.ApiKeyProtegida); }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    return new ProbarConexionResultado(false, "No se pudo leer la API key guardada: volvé a cargarla.");
                }
            }
            else apiKey = _servidor.ApiKey;
        }
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ProbarConexionResultado(false, "No hay API key guardada ni se ingresó una nueva.");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using (var cuenta = new HttpRequestMessage(HttpMethod.Get, UrlCuenta))
            {
                cuenta.Headers.Add("api-key", apiKey);
                using var r = await _http.SendAsync(cuenta, cts.Token);
                if (r.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    return new ProbarConexionResultado(false, "Brevo rechazó la API key (inválida o sin permisos).");
                if (!r.IsSuccessStatusCode)
                    return new ProbarConexionResultado(false, $"Brevo respondió {(int)r.StatusCode}.");
            }

            // La key es válida: ahora que el remitente esté verificado en la cuenta (si no, Brevo
            // rechaza cada envío). Es un chequeo extra — si esta consulta falla no se invalida la key.
            var from = EmailReglas.Normalizar(input.FromEmail);
            if (from is not null)
            {
                using var rem = new HttpRequestMessage(HttpMethod.Get, UrlRemitentes);
                rem.Headers.Add("api-key", apiKey);
                using var r = await _http.SendAsync(rem, cts.Token);
                if (r.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(cts.Token));
                    var verificado = doc.RootElement.TryGetProperty("senders", out var lista)
                        && lista.EnumerateArray().Any(s => s.TryGetProperty("email", out var e)
                            && string.Equals(e.GetString(), from, StringComparison.OrdinalIgnoreCase));
                    if (!verificado)
                        return new ProbarConexionResultado(false,
                            $"La API key es válida, pero el remitente {from} no figura entre los remitentes de esa cuenta de Brevo.");
                }
            }
            return new ProbarConexionResultado(true, null);
        }
        catch (Exception ex)
        {
            return new ProbarConexionResultado(false, ex.Message);
        }
    }
}
