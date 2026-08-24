using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Pos.Application.Common;

namespace Pos.Infrastructure.Adapters.Afip;

/// <summary>Un tramo de IVA para el array <c>Iva</c> de WSFEv1 (Id de alícuota AFIP, base, importe).</summary>
public record AfipTramoIva(int Id, decimal BaseImp, decimal Importe);

/// <summary>Un tributo (percepción) para el array <c>Tributos</c> de WSFEv1 — Id de tributo AFIP
/// (tabla FEParamGetTiposTributos: 1=Nacional, 2=Provincial, 3=Municipal, 4=Interno, 99=Otro),
/// base imponible, alícuota (%) y el importe efectivamente percibido.</summary>
public record AfipTributo(int Id, string Descripcion, decimal BaseImponible, decimal Alicuota, decimal Importe);

/// <summary>Lo mínimo que WSFEv1 necesita para autorizar UN comprobante (esta app emite de uno en
/// uno, nunca en lote — <c>CantReg</c> siempre viaja en 1).</summary>
public record AfipComprobanteReq(
    int PtoVta, int CbteTipo, int Concepto, int DocTipo, string DocNro,
    long CbteNro, DateTime Fecha, decimal ImpTotal, decimal ImpNeto, decimal ImpIva,
    decimal ImpTotConc, decimal ImpOpEx, decimal ImpTrib, int CondicionIvaReceptorId,
    IReadOnlyList<AfipTramoIva> Ivas, IReadOnlyList<AfipTributo>? Tributos = null);

public record AfipObservacion(string Codigo, string Mensaje);

public record AfipResultadoCae(bool Aprobado, string? Cae, DateTime? Vencimiento, IReadOnlyList<AfipObservacion> Observaciones);

public record AfipResultadoCaea(bool Ok, string? Caea, DateTime? Desde, DateTime? Hasta, string? Error);

/// <summary>
/// WSFEv1 (facturación electrónica): CAE por comprobante, CAEA por período quincenal, y consulta
/// del último autorizado. Todo por SOAP 1.1 crudo (HttpClient + XML armado a mano) — mismo criterio
/// que el adaptador Hasar: nada de svcutil/WCF, que en .NET moderno da más dolores de cabeza que el
/// XML en sí, y así queda todo en un solo lugar auditable.
/// </summary>
public class AfipWsfeClient
{
    private const string Ns = "http://ar.gov.afip.dif.FEV1/";
    private readonly AfipOptions _opciones;
    private readonly HttpClient _http;

    public AfipWsfeClient(AfipOptions opciones)
    {
        _opciones = opciones;
        _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(30) })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>Ping sin autenticar — para el health check, no consume cuota de WSAA.</summary>
    public async Task<bool> DummyAsync(CancellationToken ct)
    {
        var body = "<FEDummy xmlns=\"" + Ns + "\" />";
        var doc = await LlamarAsync("FEDummy", body, ct);
        var r = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "FEDummyResult");
        var appServer = Texto(r, "AppServer");
        var dbServer = Texto(r, "DbServer");
        var authServer = Texto(r, "AuthServer");
        return appServer == "OK" && dbServer == "OK" && authServer == "OK";
    }

    public async Task<long> UltimoAutorizadoAsync(AfipCredencial cred, string cuit, int ptoVta, int cbteTipo, CancellationToken ct)
    {
        var body = "<FECompUltimoAutorizado xmlns=\"" + Ns + "\">" +
            Auth(cred, cuit) +
            $"<PtoVta>{ptoVta}</PtoVta><CbteTipo>{cbteTipo}</CbteTipo>" +
            "</FECompUltimoAutorizado>";
        var doc = await LlamarAsync("FECompUltimoAutorizado", body, ct);
        var r = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECompUltimoAutorizadoResult");
        LanzarSiHayErrores(r);
        return long.Parse(Texto(r, "CbteNro") ?? "0", CultureInfo.InvariantCulture);
    }

    public async Task<AfipResultadoCae> SolicitarCaeAsync(AfipCredencial cred, string cuit, AfipComprobanteReq c, CancellationToken ct)
    {
        var ivas = string.Concat(c.Ivas.Select(i =>
            $"<AlicIva><Id>{i.Id}</Id><BaseImp>{Dec(i.BaseImp)}</BaseImp><Importe>{Dec(i.Importe)}</Importe></AlicIva>"));
        // Percepciones (IVA/IIBB) — sin esto, ImpTrib>0 (heredado del Total que ya las incluye)
        // pero el detalle nunca viaja: ARCA rechaza con "ImpTotal debe ser igual a la suma de
        // ImpTotConc+ImpNeto+ImpOpEx+ImpTrib+ImpIVA" (error 10048, visto en producción real).
        var tributosXml = string.Concat((c.Tributos ?? Array.Empty<AfipTributo>()).Select(t =>
            $"<Tributo><Id>{t.Id}</Id><Desc>{System.Security.SecurityElement.Escape(t.Descripcion)}</Desc>" +
            $"<BaseImp>{Dec(t.BaseImponible)}</BaseImp><Alic>{Dec(t.Alicuota)}</Alic><Importe>{Dec(t.Importe)}</Importe></Tributo>"));

        var det =
            $"<Concepto>{c.Concepto}</Concepto>" +
            $"<DocTipo>{c.DocTipo}</DocTipo><DocNro>{c.DocNro}</DocNro>" +
            $"<CbteDesde>{c.CbteNro}</CbteDesde><CbteHasta>{c.CbteNro}</CbteHasta>" +
            $"<CbteFch>{Fecha(c.Fecha)}</CbteFch>" +
            $"<ImpTotal>{Dec(c.ImpTotal)}</ImpTotal><ImpTotConc>{Dec(c.ImpTotConc)}</ImpTotConc>" +
            $"<ImpNeto>{Dec(c.ImpNeto)}</ImpNeto><ImpOpEx>{Dec(c.ImpOpEx)}</ImpOpEx>" +
            $"<ImpIVA>{Dec(c.ImpIva)}</ImpIVA><ImpTrib>{Dec(c.ImpTrib)}</ImpTrib>" +
            "<MonId>PES</MonId><MonCotiz>1</MonCotiz>" +
            $"<CondicionIVAReceptorId>{c.CondicionIvaReceptorId}</CondicionIVAReceptorId>" +
            (string.IsNullOrEmpty(tributosXml) ? "" : $"<Tributos>{tributosXml}</Tributos>") +
            (c.Ivas.Count > 0 ? $"<Iva>{ivas}</Iva>" : "");

        var body = "<FECAESolicitar xmlns=\"" + Ns + "\">" +
            Auth(cred, cuit) +
            "<FeCAEReq>" +
            $"<FeCabReq><CantReg>1</CantReg><PtoVta>{c.PtoVta}</PtoVta><CbteTipo>{c.CbteTipo}</CbteTipo></FeCabReq>" +
            $"<FeDetReq><FECAEDetRequest>{det}</FECAEDetRequest></FeDetReq>" +
            "</FeCAEReq>" +
            "</FECAESolicitar>";

        var doc = await LlamarAsync("FECAESolicitar", body, ct);
        var result = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECAESolicitarResult");
        LanzarSiHayErrores(result);

        var cabResultado = Texto(result?.Descendants().FirstOrDefault(x => x.Name.LocalName == "FeCabResp"), "Resultado");
        var detResp = result?.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECAEDetResponse");
        var observaciones = (detResp?.Descendants().Where(x => x.Name.LocalName == "Obs") ?? Enumerable.Empty<XElement>())
            .Select(o => new AfipObservacion(Texto(o, "Code") ?? "", Texto(o, "Msg") ?? "")).ToList();

        var aprobado = cabResultado == "A" && Texto(detResp, "Resultado") == "A";
        var cae = Texto(detResp, "CAE");
        var vtoTexto = Texto(detResp, "CAEFchVto");
        DateTime? vto = vtoTexto is { Length: 8 }
            ? DateTime.ParseExact(vtoTexto, "yyyyMMdd", CultureInfo.InvariantCulture)
            : null;

        return new AfipResultadoCae(aprobado, aprobado ? cae : null, aprobado ? vto : null, observaciones);
    }

    /// <summary>Pide el CAEA de un período+quincena. <paramref name="orden"/>: 1 = del 1 al 15,
    /// 2 = del 16 a fin de mes.</summary>
    public async Task<AfipResultadoCaea> SolicitarCaeaAsync(AfipCredencial cred, string cuit, int periodo, int orden, CancellationToken ct)
    {
        var body = "<FECAEASolicitar xmlns=\"" + Ns + "\">" + Auth(cred, cuit) +
            $"<Periodo>{periodo}</Periodo><Orden>{orden}</Orden>" +
            "</FECAEASolicitar>";
        var doc = await LlamarAsync("FECAEASolicitar", body, ct);
        var result = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECAEASolicitarResult");
        var errores = ObtenerErrores(result);
        if (errores.Count > 0)
            return new AfipResultadoCaea(false, null, null, null, string.Join(" | ", errores));

        var get = result?.Descendants().FirstOrDefault(x => x.Name.LocalName == "ResultGet");
        var caea = Texto(get, "CAEA");
        var desde = Texto(get, "FchVigDesde");
        var hasta = Texto(get, "FchVigHasta");
        return new AfipResultadoCaea(true, caea,
            ParseFechaAaaammdd(desde), ParseFechaAaaammdd(hasta), null);
    }

    /// <summary>Informa (dentro de las 48hs, según normativa) los comprobantes ya emitidos bajo un
    /// CAEA — mismo body que CAE pero con el CAEA en <c>CAEA</c> en vez de pedir uno nuevo.</summary>
    public async Task<AfipResultadoCaea> InformarLoteCaeaAsync(AfipCredencial cred, string cuit, string caea,
        IReadOnlyList<AfipComprobanteReq> lote, CancellationToken ct)
    {
        if (lote.Count == 0)
            throw new DomainException("CAEA_LOTE_VACIO",
                "No hay comprobantes para informar bajo ese CAEA — si no se usó ninguno en el período, corresponde FECAEASinMovimientoInformar, no este método.");
        if (lote.Count > 250)
            throw new DomainException("CAEA_LOTE_DEMASIADO_GRANDE", "WSFEv1 acepta hasta 250 comprobantes por lote.");

        var ptoVta = lote[0].PtoVta;
        var cbteTipo = lote[0].CbteTipo;
        var detalles = string.Concat(lote.Select(c =>
        {
            var ivas = string.Concat(c.Ivas.Select(i =>
                $"<AlicIva><Id>{i.Id}</Id><BaseImp>{Dec(i.BaseImp)}</BaseImp><Importe>{Dec(i.Importe)}</Importe></AlicIva>"));
            return "<FECAEADetRequest>" +
                $"<Concepto>{c.Concepto}</Concepto><DocTipo>{c.DocTipo}</DocTipo><DocNro>{c.DocNro}</DocNro>" +
                $"<CbteDesde>{c.CbteNro}</CbteDesde><CbteHasta>{c.CbteNro}</CbteHasta><CbteFch>{Fecha(c.Fecha)}</CbteFch>" +
                $"<ImpTotal>{Dec(c.ImpTotal)}</ImpTotal><ImpTotConc>{Dec(c.ImpTotConc)}</ImpTotConc>" +
                $"<ImpNeto>{Dec(c.ImpNeto)}</ImpNeto><ImpOpEx>{Dec(c.ImpOpEx)}</ImpOpEx>" +
                $"<ImpIVA>{Dec(c.ImpIva)}</ImpIVA><ImpTrib>{Dec(c.ImpTrib)}</ImpTrib>" +
                $"<MonId>PES</MonId><MonCotiz>1</MonCotiz><CAEA>{caea}</CAEA>" +
                (c.Ivas.Count > 0 ? $"<Iva>{ivas}</Iva>" : "") +
                "</FECAEADetRequest>";
        }));

        var body = "<FECAEARegInformativo xmlns=\"" + Ns + "\">" + Auth(cred, cuit) +
            "<FeCAEARegInfReq>" +
            $"<FeCabReq><CantReg>{lote.Count}</CantReg><PtoVta>{ptoVta}</PtoVta><CbteTipo>{cbteTipo}</CbteTipo></FeCabReq>" +
            $"<FeDetReq>{detalles}</FeDetReq>" +
            "</FeCAEARegInfReq></FECAEARegInformativo>";

        var doc = await LlamarAsync("FECAEARegInformativo", body, ct);
        var result = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECAEARegInformativoResult");
        var errores = ObtenerErrores(result);
        return errores.Count > 0
            ? new AfipResultadoCaea(false, null, null, null, string.Join(" | ", errores))
            : new AfipResultadoCaea(true, caea, null, null, null);
    }

    /// <summary>Se informa "sin movimiento" cuando un CAEA vigente terminó sin usarse ni una vez
    /// en su período — igual de obligatorio que informar los que sí se usaron.</summary>
    public async Task<AfipResultadoCaea> InformarSinMovimientoAsync(AfipCredencial cred, string cuit, int ptoVta, string caea, CancellationToken ct)
    {
        var body = "<FECAEASinMovimientoInformar xmlns=\"" + Ns + "\">" + Auth(cred, cuit) +
            $"<PtoVta>{ptoVta}</PtoVta><CAEA>{caea}</CAEA>" +
            "</FECAEASinMovimientoInformar>";
        var doc = await LlamarAsync("FECAEASinMovimientoInformar", body, ct);
        var result = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECAEASinMovimientoInformarResult");
        var errores = ObtenerErrores(result);
        return errores.Count > 0
            ? new AfipResultadoCaea(false, null, null, null, string.Join(" | ", errores))
            : new AfipResultadoCaea(true, caea, null, null, null);
    }

    // ---------- SOAP plumbing ----------

    private async Task<XDocument> LlamarAsync(string accion, string bodyXml, CancellationToken ct)
    {
        var soap = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
            $"<soap:Body>{bodyXml}</soap:Body></soap:Envelope>";

        using var req = new HttpRequestMessage(HttpMethod.Post, AfipUrls.Wsfe(_opciones.Ambiente))
        {
            Content = new StringContent(soap, Encoding.UTF8, "text/xml")
        };
        req.Headers.TryAddWithoutValidation("SOAPAction", $"{Ns}{accion}");

        using var resp = await _http.SendAsync(req, ct);
        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new DomainException("WSFE_ERROR", $"WSFEv1 ({accion}) respondió {(int)resp.StatusCode}: {texto}");

        XDocument doc;
        try { doc = XDocument.Parse(texto); }
        catch (System.Xml.XmlException ex)
        {
            throw new DomainException("WSFE_ERROR", $"WSFEv1 ({accion}) devolvió una respuesta no-XML: {ex.Message}");
        }

        var fault = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "faultstring");
        if (fault is not null)
            throw new DomainException("WSFE_ERROR", $"WSFEv1 ({accion}): {fault.Value}");

        return doc;
    }

    /// <summary>WSFEv1 no siempre usa SOAP Fault para los errores de negocio (CUIT no autorizado,
    /// certificado revocado, etc.) — la mayoría vienen en un array <c>Errors</c> dentro del propio
    /// resultado, con respuesta HTTP 200. Hay que revisarlo siempre.</summary>
    private static List<string> ObtenerErrores(XElement? result)
    {
        if (result is null) return new List<string> { "Respuesta vacía de WSFEv1." };
        return result.Descendants().Where(x => x.Name.LocalName == "Err")
            .Select(e => $"{Texto(e, "Code")}: {Texto(e, "Msg")}").ToList();
    }

    private static void LanzarSiHayErrores(XElement? result)
    {
        var errores = ObtenerErrores(result);
        if (errores.Count > 0)
            throw new DomainException("WSFE_ERROR", string.Join(" | ", errores));
    }

    private static string Auth(AfipCredencial cred, string cuit) =>
        $"<Auth><Token>{cred.Token}</Token><Sign>{cred.Sign}</Sign><Cuit>{cuit}</Cuit></Auth>";

    private static string? Texto(XElement? parent, string localName) =>
        parent?.Descendants().FirstOrDefault(x => x.Name.LocalName == localName)?.Value;

    private static string Dec(decimal v) => v.ToString("F2", CultureInfo.InvariantCulture);
    private static string Fecha(DateTime d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    private static DateTime? ParseFechaAaaammdd(string? s) =>
        s is { Length: 8 } ? DateTime.ParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture) : null;
}
