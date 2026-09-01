using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions.Fidelizacion;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>Forma del body 201 de POST /api/cargar-puntos (ver cargar_puntos en schema.sql de
/// puntos-app) — solo los campos que necesita el popup de confirmación en caja.</summary>
file record CargaPuntosRespuestaJson(
    [property: JsonPropertyName("cliente")] string? Cliente,
    [property: JsonPropertyName("puntos_otorgados")] decimal? PuntosOtorgados,
    [property: JsonPropertyName("puntos_totales")] decimal? PuntosTotales);

/// <summary>Forma del body de error de puntos-app ({ "error": "mensaje" }) en 4xx/5xx.</summary>
file record ErrorRespuestaJson([property: JsonPropertyName("error")] string? Error);

/// <summary>Una campaña dentro del array "campanias" de GET /api/campanias (ver campanias.js de
/// puntos-app).</summary>
file record CampaniaJson(
    // uuid en puntos-app (campania_id, ver migration_campanias.sql), no un entero.
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("nombre")] string? Nombre,
    [property: JsonPropertyName("descripcion")] string? Descripcion,
    [property: JsonPropertyName("descuento_porcentaje")] decimal DescuentoPorcentaje,
    [property: JsonPropertyName("local")] string? Local,
    [property: JsonPropertyName("fecha_desde")] DateOnly? FechaDesde);

/// <summary>Forma del body 200 de GET /api/campanias.</summary>
file record CampaniasRespuestaJson([property: JsonPropertyName("campanias")] List<CampaniaJson>? Campanias);

/// <summary>
/// Llama a puntos-app (POST /api/cargar-puntos) para sumar puntos de fidelización al facturar.
/// Best-effort A PROPÓSITO — ver <see cref="IPuntosFidelizacionService"/>: cualquier error (config
/// deshabilitada/incompleta, cliente sin DNI, tarjeta inexistente en puntos-app, timeout, etc.) se
/// loguea y se traga acá, nunca se propaga a la venta que ya se facturó/imprimió/cobró.
/// </summary>
public class PuntosFidelizacionService : IPuntosFidelizacionService
{
    // Mismo purpose que ConexionPuntosAppAdminService (ver AbmServices.cs) — tiene que ser idéntico
    // o Unprotect falla siempre, aunque el token esté bien guardado.
    private const string DataProtectionPurpose = "Pos.ConexionPuntosApp";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly PosDbContext _db;
    private readonly IDataProtector _protector;
    private readonly HttpClient _http;
    private readonly ILogger<PuntosFidelizacionService> _log;

    public PuntosFidelizacionService(PosDbContext db, IDataProtectionProvider dataProtection,
        HttpClient http, ILogger<PuntosFidelizacionService> log)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(DataProtectionPurpose);
        _http = http;
        _log = log;
    }

    public async Task<ResultadoCargaPuntos> CargarPuntosAsync(CargaPuntosFidelizacion carga, CancellationToken ct = default)
    {
        try
        {
            var config = await _db.ConexionesPuntosApp.AsNoTracking().SingleOrDefaultAsync(ct);
            // Integración no activada: no es un error, simplemente no se intenta (no hay nada que
            // mostrarle al cajero — el frontend no muestra popup cuando Error es null).
            if (config is null || !config.Habilitada) return new ResultadoCargaPuntos(false, null, null, null, null);

            if (string.IsNullOrWhiteSpace(config.UrlBase) || string.IsNullOrWhiteSpace(config.Comercio)
                || string.IsNullOrEmpty(config.TokenProtegido))
            {
                const string msg = "Integración con puntos-app incompleta (falta URL/comercio/API key).";
                _log.LogWarning("{Msg} Factura {Factura}.", msg, carga.FacturaNumero);
                return new ResultadoCargaPuntos(false, null, null, null, msg);
            }

            var apiKey = _protector.Unprotect(config.TokenProtegido);

            using var req = new HttpRequestMessage(HttpMethod.Post,
                config.UrlBase.TrimEnd('/') + "/api/cargar-puntos");
            // X-Api-Key (secreto fijo, API_INTEGRATION_KEY en puntos-app) — no un access_token de
            // sesión: ese expira a la hora y no sirve para una integración recurrente. Ver
            // cargar-puntos.js/getAdmin.
            req.Headers.Add("X-Api-Key", apiKey);
            req.Content = JsonContent.Create(new
            {
                dni = carga.Dni,
                factura_pesos = carga.FacturaPesos,
                factura_numero = carga.FacturaNumero,
                comercio = config.Comercio,
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var resp = await _http.SendAsync(req, cts.Token);
            var bodyText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // No es un error de la venta: el motivo más común es "tarjeta no encontrada" (el
                // cliente todavía no tiene tarjeta de fidelización) o "comercio no encontrado" —
                // casos de negocio esperables, no fallas de la integración en sí. Se loguea para
                // poder revisarlos y se le devuelve el motivo al frontend (best-effort igual: nunca
                // bloquea la venta, esto solo decide si se muestra el popup o no).
                _log.LogWarning(
                    "puntos-app no sumó puntos para la factura {Factura} (DNI {Dni}): {Status} {Body}",
                    carga.FacturaNumero, carga.Dni, (int)resp.StatusCode, bodyText);
                string? motivo = null;
                try { motivo = System.Text.Json.JsonSerializer.Deserialize<ErrorRespuestaJson>(bodyText)?.Error; }
                catch (System.Text.Json.JsonException) { /* body no era el JSON esperado; se ignora el motivo */ }
                return new ResultadoCargaPuntos(false, null, null, null, motivo ?? $"HTTP {(int)resp.StatusCode}");
            }

            var data = System.Text.Json.JsonSerializer.Deserialize<CargaPuntosRespuestaJson>(bodyText,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            return new ResultadoCargaPuntos(true, data?.Cliente, data?.PuntosOtorgados, data?.PuntosTotales, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "No se pudo sumar puntos en puntos-app para la factura {Factura} (DNI {Dni}).",
                carga.FacturaNumero, carga.Dni);
            return new ResultadoCargaPuntos(false, null, null, null, ex.Message);
        }
    }

    public async Task<ResultadoCampanias> ConsultarCampaniasAsync(string dni, CancellationToken ct = default)
    {
        var vacio = Array.Empty<CampaniaVigente>();
        try
        {
            if (string.IsNullOrWhiteSpace(dni)) return new ResultadoCampanias(false, vacio, null);

            var config = await _db.ConexionesPuntosApp.AsNoTracking().SingleOrDefaultAsync(ct);
            // Integración no activada: no es un error, simplemente no se consulta (no hay nada que
            // mostrarle al cajero — mismo criterio que CargarPuntosAsync).
            if (config is null || !config.Habilitada) return new ResultadoCampanias(false, vacio, null);

            if (string.IsNullOrWhiteSpace(config.UrlBase) || string.IsNullOrWhiteSpace(config.Comercio)
                || string.IsNullOrEmpty(config.TokenProtegido))
                return new ResultadoCampanias(false, vacio, "Integración con puntos-app incompleta (falta URL/comercio/API key).");

            var apiKey = _protector.Unprotect(config.TokenProtegido);

            // "local" = el Comercio configurado acá (este mismo POS): devuelve las campañas
            // generales + las restringidas a este local únicamente, nunca las de otro local.
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{config.UrlBase.TrimEnd('/')}/api/campanias?dni={Uri.EscapeDataString(dni)}&local={Uri.EscapeDataString(config.Comercio)}");
            req.Headers.Add("X-Api-Key", apiKey);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var resp = await _http.SendAsync(req, cts.Token);
            var bodyText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // 404 = DNI sin cliente en puntos-app, el caso más común (la mayoría de los clientes
                // de pos-mayorista no tienen tarjeta de fidelización) — no es una falla real, así que
                // se loguea más bajo que el resto para no ensuciar el log en cada venta.
                string? motivo = null;
                try { motivo = System.Text.Json.JsonSerializer.Deserialize<ErrorRespuestaJson>(bodyText)?.Error; }
                catch (System.Text.Json.JsonException) { /* body no era el JSON esperado; se ignora el motivo */ }
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    _log.LogDebug("puntos-app no tiene campañas para DNI {Dni}: {Body}", dni, bodyText);
                else
                    _log.LogWarning("No se pudieron consultar campañas en puntos-app para DNI {Dni}: {Status} {Body}",
                        dni, (int)resp.StatusCode, bodyText);
                return new ResultadoCampanias(false, vacio, motivo ?? $"HTTP {(int)resp.StatusCode}");
            }

            var data = System.Text.Json.JsonSerializer.Deserialize<CampaniasRespuestaJson>(bodyText,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            // Trunca a entero: puntos-app puede guardar el % con decimales de más (ej. 10.7 en vez de
            // 10 por cómo se cargó la campaña ahí), y el negocio pidió redondear siempre hacia abajo
            // al entero — nunca cobrarle de más al cliente por un resto de decimal que ni se ve en
            // el globo de Caja.
            var campanias = (data?.Campanias ?? new List<CampaniaJson>())
                .Select(c => new CampaniaVigente(c.Id ?? "", c.Nombre ?? "", c.Descripcion,
                    Math.Truncate(c.DescuentoPorcentaje), c.Local ?? "General", c.FechaDesde))
                .ToList();
            return new ResultadoCampanias(true, campanias, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudieron consultar campañas en puntos-app para DNI {Dni}.", dni);
            return new ResultadoCampanias(false, vacio, ex.Message);
        }
    }
}
