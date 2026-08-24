using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Pos.Application.Common;
using Pos.Infrastructure.Storage;

namespace Pos.Infrastructure.Adapters.Afip;

/// <summary>Token+Sign de un servicio (ej. "wsfe"), válidos ~12hs desde que WSAA los emite.</summary>
public record AfipCredencial(string Token, string Sign, DateTime ExpiraUtc);

/// <summary>
/// WSAA (autenticación): arma el TRA (Ticket de Requerimiento de Acceso), lo firma en CMS/PKCS#7
/// con el certificado de la empresa y lo cambia por un Token+Sign contra AFIP. El certificado NO
/// se manda al Web Service — solo firma localmente; AFIP valida la firma con la cadena de
/// certificación pública.
///
/// El Token/Sign se cachea en memoria por (empresa, servicio) hasta 10 minutos antes de su
/// vencimiento real: AFIP limita cuántos se pueden pedir por período, así que pedir uno nuevo en
/// cada factura agotaría la cuota y además es innecesario (dura ~12hs). TAMBIÉN se persiste a disco
/// (no solo en memoria): un simple reinicio del proceso (deploy, reinicio del server) no puede
/// perder el ticket vigente — si lo hiciera, WSAA rechazaría el siguiente pedido con
/// "El CEE ya posee un TA valido" (el ticket anterior sigue vivo del lado de ARCA hasta que vence,
/// aunque nuestro proceso lo haya olvidado) y la app quedaría sin poder facturar Electrónica hasta
/// que ese ticket expirara solo. Bug real encontrado probando en homologación (2026-08-24).
/// </summary>
public class AfipWsaaClient
{
    private const string NamespaceWsaa = "http://wsaa.view.sua.dvadac.desein.afip.gov";

    private readonly AfipOptions _opciones;
    private readonly AfipCertificadoStore _certificados;
    private readonly string _tokensPath;
    private readonly ILogger<AfipWsaaClient> _log;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, AfipCredencial> _cache = new();
    // Serializa los pedidos de un mismo (empresa, servicio): dos facturas concurrentes con el
    // token vencido no deben disparar dos altas de TRA a la vez (AFIP puede rechazar el segundo
    // "a con Ticket vigente" — el TRA usa hora, no es idempotente como el TRA de Hasar).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public AfipWsaaClient(AfipOptions opciones, AfipCertificadoStore certificados, StorageOptions storage, ILogger<AfipWsaaClient> log)
    {
        _opciones = opciones;
        _certificados = certificados;
        // Carpeta hermana de la de certificados (App_Data/afip-tokens), separada por ambiente para
        // no confundir un ticket de homologación con uno de producción si se cambia de config.
        _tokensPath = Path.Combine(Path.GetDirectoryName(storage.CertificadosPath.TrimEnd('/', '\\'))
            ?? storage.CertificadosPath, "afip-tokens", _opciones.Ambiente.ToString());
        _log = log;
        _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(30) })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<AfipCredencial> ObtenerCredencialAsync(int idEmpresa, string servicio, CancellationToken ct)
    {
        var clave = $"{idEmpresa}:{servicio}";
        if (_cache.TryGetValue(clave, out var actual) && actual.ExpiraUtc > DateTime.UtcNow.AddMinutes(10))
            return actual;

        var candado = _locks.GetOrAdd(clave, _ => new SemaphoreSlim(1, 1));
        await candado.WaitAsync(ct);
        try
        {
            // Re-chequeo tras el candado: otra request pudo haber renovado mientras esperábamos.
            if (_cache.TryGetValue(clave, out actual) && actual.ExpiraUtc > DateTime.UtcNow.AddMinutes(10))
                return actual;

            // Antes de pedir uno nuevo: ¿quedó un ticket vigente en disco de un proceso anterior?
            var deDisco = LeerDeDisco(clave);
            if (deDisco is not null && deDisco.ExpiraUtc > DateTime.UtcNow.AddMinutes(10))
            {
                _cache[clave] = deDisco;
                _log.LogInformation("WSAA: reusando Token/Sign persistido para empresa {IdEmpresa}, servicio {Servicio} (vence {Vencimiento})",
                    idEmpresa, servicio, deDisco.ExpiraUtc);
                return deDisco;
            }

            _log.LogInformation("WSAA: solicitando nuevo Token/Sign para empresa {IdEmpresa}, servicio {Servicio}", idEmpresa, servicio);
            var cred = await SolicitarNuevaAsync(idEmpresa, servicio, ct);
            _cache[clave] = cred;
            GuardarEnDisco(clave, cred);
            return cred;
        }
        finally
        {
            candado.Release();
        }
    }

    private async Task<AfipCredencial> SolicitarNuevaAsync(int idEmpresa, string servicio, CancellationToken ct)
    {
        var tra = ArmarTra(servicio);
        string cms;
        using (var cert = await _certificados.ObtenerCertificadoAsync(idEmpresa, ct))
        {
            cms = FirmarTra(tra, cert);
        }
        var respuestaXml = await LlamarLoginCmsAsync(cms, ct);
        return ParsearCredencial(respuestaXml);
    }

    /// <summary>
    /// TRA con vigencia de 10 minutos (5 antes y 5 después de "ahora", en UTC): AFIP exige que
    /// generationTime/expirationTime encierren el momento real del pedido con margen de reloj.
    /// </summary>
    private static string ArmarTra(string servicio)
    {
        var ahora = DateTime.UtcNow;
        var uniqueId = ((long)ahora.Subtract(DateTime.UnixEpoch).TotalSeconds).ToString(CultureInfo.InvariantCulture);
        string F(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<loginTicketRequest version=\"1.0\">" +
            "<header>" +
            $"<uniqueId>{uniqueId}</uniqueId>" +
            $"<generationTime>{F(ahora.AddMinutes(-5))}</generationTime>" +
            $"<expirationTime>{F(ahora.AddMinutes(5))}</expirationTime>" +
            "</header>" +
            $"<service>{servicio}</service>" +
            "</loginTicketRequest>";
    }

    /// <summary>Firma el TRA como CMS/PKCS#7 (SignedData, no detached) y lo devuelve en Base64 —
    /// tal como lo pide <c>loginCms</c>. Solo va el certificado propio en la firma (EndCertOnly):
    /// AFIP ya tiene la cadena de la CA que lo emitió.</summary>
    private static string FirmarTra(string traXml, X509Certificate2 cert)
    {
        var contentInfo = new ContentInfo(Encoding.UTF8.GetBytes(traXml));
        var signedCms = new SignedCms(contentInfo, detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") // SHA-256
        };
        signedCms.ComputeSignature(signer);
        return Convert.ToBase64String(signedCms.Encode());
    }

    private async Task<string> LlamarLoginCmsAsync(string cmsBase64, CancellationToken ct)
    {
        var soap = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
            "<soap:Body>" +
            $"<loginCms xmlns=\"{NamespaceWsaa}\"><in0>{cmsBase64}</in0></loginCms>" +
            "</soap:Body></soap:Envelope>";

        using var req = new HttpRequestMessage(HttpMethod.Post, AfipUrls.Wsaa(_opciones.Ambiente))
        {
            Content = new StringContent(soap, Encoding.UTF8, "text/xml")
        };
        req.Headers.TryAddWithoutValidation("SOAPAction", $"{NamespaceWsaa}/loginCms");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new DomainException("WSAA_ERROR", $"WSAA respondió {(int)resp.StatusCode}: {ExtraerFaultString(body) ?? body}");
        var fault = ExtraerFaultString(body);
        if (fault is not null)
            throw new DomainException("WSAA_ERROR", fault);
        return body;
    }

    private static string? ExtraerFaultString(string soapXml)
    {
        try
        {
            var doc = XDocument.Parse(soapXml);
            return doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "faultstring")?.Value;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>El SOAP body trae <c>loginCmsReturn</c> con el XML del loginTicketResponse
    /// escapado como texto — hay que parsearlo dos veces (el envelope, y adentro el XML real).</summary>
    private static AfipCredencial ParsearCredencial(string soapXml)
    {
        var envelope = XDocument.Parse(soapXml);
        var loginCmsReturn = envelope.Descendants().FirstOrDefault(x => x.Name.LocalName == "loginCmsReturn")?.Value
            ?? throw new DomainException("WSAA_ERROR", "La respuesta de WSAA no contiene loginCmsReturn.");

        var ticket = XDocument.Parse(loginCmsReturn);
        var token = ticket.Descendants("token").FirstOrDefault()?.Value
            ?? throw new DomainException("WSAA_ERROR", "El loginTicketResponse no contiene token.");
        var sign = ticket.Descendants("sign").FirstOrDefault()?.Value
            ?? throw new DomainException("WSAA_ERROR", "El loginTicketResponse no contiene sign.");
        var expirationTime = ticket.Descendants("expirationTime").FirstOrDefault()?.Value
            ?? throw new DomainException("WSAA_ERROR", "El loginTicketResponse no contiene expirationTime.");

        return new AfipCredencial(token, sign, DateTimeOffset.Parse(expirationTime, CultureInfo.InvariantCulture).UtcDateTime);
    }

    // ---------- Persistencia a disco (sobrevive reinicios del proceso) ----------

    private string RutaToken(string clave) => Path.Combine(_tokensPath, $"{clave.Replace(':', '-')}.json");

    private AfipCredencial? LeerDeDisco(string clave)
    {
        var ruta = RutaToken(clave);
        if (!File.Exists(ruta)) return null;
        try
        {
            return JsonSerializer.Deserialize<AfipCredencial>(File.ReadAllText(ruta));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Un archivo corrupto o ilegible no debe romper la emisión — simplemente se pide un
            // token nuevo, como si no hubiera nada persistido.
            _log.LogWarning(ex, "WSAA: no se pudo leer el token persistido en {Ruta}, se pedirá uno nuevo.", ruta);
            return null;
        }
    }

    private void GuardarEnDisco(string clave, AfipCredencial cred)
    {
        try
        {
            Directory.CreateDirectory(_tokensPath);
            File.WriteAllText(RutaToken(clave), JsonSerializer.Serialize(cred));
        }
        catch (IOException ex)
        {
            // No persistir no es fatal (el pedido de CAE ya tiene el token en memoria para esta
            // corrida) — solo significa que un reinicio futuro va a tener que pedir uno nuevo.
            _log.LogWarning(ex, "WSAA: no se pudo persistir el token a disco (clave {Clave}).", clave);
        }
    }
}
