using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Pos.Infrastructure.Adapters.Hasar;

/// <summary>Error devuelto por el controlador fiscal, ya traducido a algo accionable.</summary>
public class HasarException : Exception
{
    /// <summary>Código simbólico del equipo (p.ej. <c>POS_DOCUMENT_BEYOND_FISCAL_DAY</c>).</summary>
    public string Codigo { get; }
    public string? Contexto { get; }
    public string? Parametro { get; }

    public HasarException(string codigo, string mensaje, string? contexto = null, string? parametro = null)
        : base(mensaje)
    {
        Codigo = codigo; Contexto = contexto; Parametro = parametro;
    }
}

/// <summary>Respuesta de un comando fiscal.</summary>
public class HasarRespuesta
{
    public XElement Raiz { get; init; } = new("vacio");
    public IReadOnlyList<string> EstadosFiscales { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EstadosImpresora { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EstadosAuxiliares { get; init; } = Array.Empty<string>();

    public string? Campo(string nombre) => Raiz.Element(nombre)?.Value.Trim();

    public bool Tiene(string estado) =>
        EstadosFiscales.Contains(estado) || EstadosImpresora.Contains(estado)
        || EstadosAuxiliares.Contains(estado);

    public bool HayError =>
        EstadosFiscales.Any(e => e.StartsWith("Error")) || EstadosImpresora.Any(e => e.StartsWith("Error"));
}

/// <summary>
/// Cliente del protocolo fiscal 2G de HASAR. El equipo expone un servidor HTTP y recibe comandos
/// como XML plano contra <c>POST /fiscal.xml</c>: el elemento raíz es el nombre del comando y los
/// hijos son sus parámetros. No hace falta el OCX de 32 bits ni ningún agente local — esto habla
/// directo con la impresora por la LAN.
///
/// Cada instancia atiende UNA impresora y serializa el acceso: el equipo tiene un solo estado
/// fiscal (un comprobante abierto por vez), así que dos ventas en paralelo sobre la misma caja se
/// pisarían los ítems entre sí.
/// </summary>
public class HasarProtocolo : IDisposable
{
    private const string XmlDecl = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>";

    private readonly HasarImpresoraOptions _impresora;
    private readonly HasarOptions _opciones;
    private readonly SecuenciaStore _secuencias;
    private readonly ILogger _log;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly string _clave;
    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    public HasarProtocolo(HasarImpresoraOptions impresora, HasarOptions opciones,
        SecuenciaStore secuencias, ILogger log)
    {
        _impresora = impresora; _opciones = opciones; _secuencias = secuencias; _log = log;
        _clave = $"{impresora.Host}_{impresora.Puerto}";
        _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = new Uri($"http://{impresora.Host}:{impresora.Puerto}/"),
            Timeout = TimeSpan.FromMilliseconds(opciones.TimeoutMs)
        };
    }

    public string Destino => $"{_impresora.Host}:{_impresora.Puerto}";

    /// <summary>Toma el equipo en exclusiva mientras dure el comprobante.</summary>
    public async Task<IDisposable> TomarAsync(CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        return new Liberador(_mutex);
    }

    /// <summary>
    /// Envía un comando fiscal. Ante <c>ControladorOcupado</c> reintenta con la MISMA secuencia,
    /// que es lo que hace la respuesta idempotente: si el comando ya se ejecutó, el equipo
    /// devuelve la respuesta guardada en vez de imprimir de nuevo.
    /// </summary>
    public async Task<HasarRespuesta> EjecutarAsync(string comando,
        IEnumerable<KeyValuePair<string, string>>? parametros, CancellationToken ct)
    {
        var secuencia = _secuencias.Siguiente(_clave);
        var cuerpo = Construir(comando, secuencia, parametros);

        for (var intento = 0; intento <= _opciones.MaxReintentosOcupado; intento++)
        {
            var xml = await PostAsync(cuerpo, ct);
            var raiz = Parsear(xml, comando);

            if (raiz.Name.LocalName == "ControladorOcupado")
            {
                await Task.Delay(_opciones.EsperaOcupadoMs, ct);
                continue;
            }

            // <Error> es un rechazo de la capa XML/interfaz (comando desconocido, XML mal formado),
            // distinto de un error fiscal, que viaja dentro de <Estado>.
            if (raiz.Name.LocalName == "Error")
            {
                throw new HasarException(
                    raiz.Element("Identificador")?.Value.Trim() ?? "ERROR_DESCONOCIDO",
                    raiz.Element("Descripcion")?.Value.Trim() ?? "Error del controlador fiscal.",
                    raiz.Element("Contexto")?.Value.Trim());
            }

            var resp = Interpretar(raiz);
            if (resp.HayError) throw await DetallarErrorAsync(comando, ct);
            return resp;
        }

        throw new HasarException("CONTROLADOR_OCUPADO",
            $"La impresora fiscal {Destino} sigue ocupada tras varios reintentos.");
    }

    /// <summary>
    /// Traduce el último error del equipo a una excepción con detalle.
    ///
    /// Tiene que llamarse INMEDIATAMENTE después del comando que falló: el equipo guarda un solo
    /// error y cualquier comando posterior lo pisa con <c>NO_CURRENT_ERROR</c>. Por eso se invoca
    /// acá adentro, bajo el mismo lock, y no desde el llamador.
    /// </summary>
    private async Task<HasarException> DetallarErrorAsync(string comando, CancellationToken ct)
    {
        try
        {
            var secuencia = _secuencias.Siguiente(_clave);
            var xml = await PostAsync(Construir("ConsultarUltimoError", secuencia, null), ct);
            var raiz = Parsear(xml, "ConsultarUltimoError");
            var codigo = raiz.Element("UltimoError")?.Value.Trim() ?? "ERROR_FISCAL";
            var desc = raiz.Element("Descripcion")?.Value.Trim();
            var contexto = raiz.Element("Contexto")?.Value.Trim();
            var parametro = raiz.Element("NombreParametro")?.Value.Trim();

            var mensaje = string.IsNullOrWhiteSpace(desc) ? $"Falló el comando {comando}." : desc!;
            if (!string.IsNullOrWhiteSpace(parametro)) mensaje += $" (campo: {parametro})";
            if (!string.IsNullOrWhiteSpace(contexto)) mensaje += $" {contexto}";
            return new HasarException(codigo, mensaje, contexto, parametro);
        }
        catch (HasarException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo obtener el detalle del último error de {Destino}", Destino);
            return new HasarException("ERROR_FISCAL", $"Falló el comando {comando} en la impresora fiscal.");
        }
    }

    private static string Construir(string comando, int secuencia,
        IEnumerable<KeyValuePair<string, string>>? parametros)
    {
        var sb = new StringBuilder(XmlDecl).Append('<').Append(comando).Append('>');
        sb.Append("<Secuencia>").Append(secuencia).Append("</Secuencia>");
        if (parametros != null)
            foreach (var p in parametros)
            {
                if (p.Value is null) continue;
                sb.Append('<').Append(p.Key).Append('>')
                  .Append(Escapar(p.Value))
                  .Append("</").Append(p.Key).Append('>');
            }
        return sb.Append("</").Append(comando).Append('>').ToString();
    }

    private static string Escapar(string v) =>
        v.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private async Task<string> PostAsync(string cuerpo, CancellationToken ct)
    {
        // El equipo trabaja en ISO-8859-1 y rotula sus respuestas de error como text/html aunque
        // el contenido sea XML, así que se leen los bytes y se decodifican a mano en vez de
        // confiar en el charset que declare.
        var contenido = new ByteArrayContent(Latin1.GetBytes(cuerpo));
        contenido.Headers.TryAddWithoutValidation("Content-Type", "text/xml; charset=ISO-8859-1");
        using var resp = await _http.PostAsync("fiscal.xml", contenido, ct);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return Latin1.GetString(bytes);
    }

    private static XElement Parsear(string xml, string comando)
    {
        try { return XDocument.Parse(xml).Root!; }
        catch (Exception ex)
        {
            throw new HasarException("RESPUESTA_INVALIDA",
                $"La impresora fiscal devolvió una respuesta ilegible al comando {comando}: {ex.Message}");
        }
    }

    private static HasarRespuesta Interpretar(XElement raiz)
    {
        var estado = raiz.Element("Estado");
        return new HasarRespuesta
        {
            Raiz = raiz,
            EstadosFiscales = Lista(estado?.Element("Fiscal")),
            EstadosImpresora = Lista(estado?.Element("Impresora")),
            EstadosAuxiliares = Lista(raiz.Element("EstadoAuxiliar"))
        };
    }

    private static string[] Lista(XElement? contenedor) =>
        contenedor is null ? Array.Empty<string>()
            : contenedor.Elements().Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToArray();

    public void Dispose() { _http.Dispose(); _mutex.Dispose(); }

    private sealed class Liberador : IDisposable
    {
        private readonly SemaphoreSlim _s;
        private int _liberado;
        public Liberador(SemaphoreSlim s) => _s = s;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _liberado, 1) == 0) _s.Release();
        }
    }
}
