using Pos.Application.Abstractions.Fiscal;
using Pos.Application.Common;
using Pos.Domain.Enums;
using Pos.Domain.Services;

namespace Pos.Infrastructure.Adapters.Afip;

/// <summary>
/// Adaptador real de <see cref="IFiscalService"/> contra ARCA/AFIP (WSFEv1) — reemplaza al mock en
/// las cajas de tipo Electrónica (ver ModalidadPuntoVenta). Fiscal sigue yendo por
/// <see cref="Pos.Infrastructure.Adapters.Hasar.HasarFiscalPrinter"/>, este adaptador nunca lo toca.
///
/// IMPORTANTE — alcance de lo cubierto hoy:
///  - CAE (<see cref="SolicitarCaeAsync"/>): cubre el caso común, un solo comprobante, con o sin
///    mezcla de alícuotas 21%/10,5%. NO contempla el Impuesto Interno por línea (ver
///    FacturacionService.impuestoInternoTotal) — si una factura Electrónica llegara a tener un
///    artículo con Impuesto Interno, el desglose de IVA por alícuota puede no coincidir exactamente
///    con lo que exige WSFEv1 (necesitaría <c>Tributos</c> aparte, que ItemFiscal hoy no expone por
///    línea). No es un caso esperado hoy (los artículos con Impuesto Interno son bebidas
///    alcohólicas, que en este negocio siempre facturaron Fiscal/Hasar), pero queda anotado.
///  - CAEA (<see cref="ObtenerCaeaAsync"/>/<see cref="InformarComprobantesCaeaAsync"/>): implementado
///    pero SIN wire-up real todavía — nada en FacturacionService/NotaCreditoService dispara la
///    contingencia (ver ReintentosCaeReglas.DebePasarAContingencia, hoy sin usar). Además
///    <see cref="InformarComprobantesCaeaAsync"/> no puede saber CON QUÉ CAEA se emitió el lote: el
///    puerto <see cref="IFiscalService"/> no lo expone (nadie lo llama aún) — lanza
///    DomainException explicando esto en vez de adivinar.
/// </summary>
public class AfipFiscalService : IFiscalService
{
    private readonly AfipWsaaClient _wsaa;
    private readonly AfipWsfeClient _wsfe;
    private readonly AfipCertificadoStore _certificados;
    private const string Servicio = "wsfe";

    public AfipFiscalService(AfipWsaaClient wsaa, AfipWsfeClient wsfe, AfipCertificadoStore certificados)
    {
        _wsaa = wsaa;
        _wsfe = wsfe;
        _certificados = certificados;
    }

    public async Task<ResultadoCae> SolicitarCaeAsync(ComprobanteFiscal cmp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmp.CodigoArca))
            return new ResultadoCae(false, null, null, false,
                "El comprobante no tiene código ARCA (TipoComprobante.CodigoArca) — no se puede mapear a CbteTipo de WSFEv1.");
        var cbteTipo = int.Parse(cmp.CodigoArca);

        var cuit = await _certificados.ObtenerCuitAsync(cmp.IdEmpresa, ct);
        var cred = await _wsaa.ObtenerCredencialAsync(cmp.IdEmpresa, Servicio, ct);

        // Verificación contra el numerador real de ARCA antes de pedir nada: el numerador propio
        // (Numeros, ver FacturacionService/NotaCreditoService) tiene que estar SIEMPRE un paso por
        // delante del último autorizado. Si no coincide, algo se desincronizó (reintento fallido,
        // comprobante de prueba emitido a mano, etc.) — mejor frenar con un error claro que pedir un
        // número que ARCA va a rechazar de todas formas.
        var ultimoAutorizado = await _wsfe.UltimoAutorizadoAsync(cred, cuit, cmp.PuntoVenta, cbteTipo, ct);
        if (cmp.Numero != ultimoAutorizado + 1)
            return new ResultadoCae(false, null, null, false,
                $"Numerador desincronizado con ARCA: se pidió el Nº {cmp.Numero} pero el último autorizado en ARCA para este punto de venta/tipo es {ultimoAutorizado} (correspondería el {ultimoAutorizado + 1}).");

        var req = MapearComprobante(cmp, cbteTipo);
        var resultado = await _wsfe.SolicitarCaeAsync(cred, cuit, req, ct);

        if (!resultado.Aprobado)
        {
            var detalle = resultado.Observaciones.Count > 0
                ? string.Join(" | ", resultado.Observaciones.Select(o => $"{o.Codigo}: {o.Mensaje}"))
                : "ARCA rechazó el comprobante sin observaciones detalladas.";
            return new ResultadoCae(false, null, null, false, detalle);
        }

        return new ResultadoCae(true, resultado.Cae, resultado.Vencimiento, false, null);
    }

    public async Task<ResultadoCaea> ObtenerCaeaAsync(int idEmpresa, PeriodoFiscal periodo, CancellationToken ct)
    {
        var cuit = await _certificados.ObtenerCuitAsync(idEmpresa, ct);
        var cred = await _wsaa.ObtenerCredencialAsync(idEmpresa, Servicio, ct);
        var periodoAaaamm = periodo.Anio * 100 + periodo.Mes;

        // El puerto solo recibe Año+Mes, no la quincena — se asume la quincena EN CURSO del mes
        // pedido (si el período pedido no es el mes actual, se asume la 1ª quincena). Esto es
        // aproximado a propósito: nadie llama este método todavía (ver nota de clase), así que no
        // hay un caso real contra el que validar el criterio correcto.
        var hoy = DateTime.UtcNow;
        var orden = (hoy.Year == periodo.Anio && hoy.Month == periodo.Mes && hoy.Day > 15) ? 2 : 1;

        var resultado = await _wsfe.SolicitarCaeaAsync(cred, cuit, periodoAaaamm, orden, ct);
        return new ResultadoCaea(resultado.Ok, resultado.Caea, resultado.Desde, resultado.Hasta, resultado.Error);
    }

    public Task<ResultadoCaea> InformarComprobantesCaeaAsync(int idEmpresa, IEnumerable<ComprobanteFiscal> lote, CancellationToken ct) =>
        throw new DomainException("CAEA_INTERFAZ_INCOMPLETA",
            "IFiscalService.InformarComprobantesCaeaAsync no recibe el CAEA del lote a informar (el " +
            "puerto no lo expone) — hace falta extender la interfaz antes de poder implementarlo de " +
            "verdad. Hoy nada llama a este método (la contingencia CAEA no está conectada en la saga).");

    public async Task<EstadoServicioFiscal> PingAsync(CancellationToken ct)
    {
        try
        {
            var ok = await _wsfe.DummyAsync(ct);
            return new EstadoServicioFiscal(ok, ok ? "ARCA (WSFEv1) disponible" : "ARCA (WSFEv1) reporta algún servicio caído (FEDummy)");
        }
        catch (Exception ex)
        {
            return new EstadoServicioFiscal(false, $"ARCA (WSFEv1) no disponible: {ex.Message}");
        }
    }

    // ---------- Mapeo ComprobanteFiscal → AfipComprobanteReq ----------

    private static AfipComprobanteReq MapearComprobante(ComprobanteFiscal cmp, int cbteTipo)
    {
        var (docTipo, docNro) = MapearDocumento(cmp.Cliente, cmp.CuitCliente);
        var condicionIva = MapearCondicionIva(cmp.Cliente?.Responsabilidad);

        // Base imponible por línea (precio unitario × cantidad − descuento), para prorratear el
        // Neto/IVA de la cabecera entre las alícuotas presentes — reutiliza la misma regla que ya
        // usa el prorrateo de Notas de Crédito por monto, así la suma de los tramos cierra EXACTO
        // contra cmp.Neto/cmp.Iva (WSFEv1 valida que ImpTotal = suma de todo, al centavo).
        var lineasConIva = (cmp.Items ?? Array.Empty<ItemFiscal>())
            .Select((it, idx) => new LineaOriginal(idx, it.PrecioUnitario * it.Cantidad - it.Descuento, it.AlicuotaIva, false))
            .Where(l => l.Alicuota > 0m)
            .ToList();

        List<AfipTramoIva> tramos;
        decimal impOpEx;
        if (lineasConIva.Count == 0)
        {
            // Todo lo gravado a la venta quedó al 0% (o no hay ítems detallados): se declara como
            // operación exenta en vez de mandar un array de IVA vacío con ImpIVA>0 inconsistente.
            tramos = new List<AfipTramoIva>();
            impOpEx = cmp.Neto;
        }
        else
        {
            var bases = NotaCreditoReglas.Prorratear(cmp.Neto, lineasConIva);
            var cuotas = NotaCreditoReglas.Prorratear(cmp.Iva, lineasConIva);
            tramos = bases.Select(b => new AfipTramoIva(
                AlicuotaId(b.Alicuota), b.Importe,
                cuotas.FirstOrDefault(c => c.Alicuota == b.Alicuota)?.Importe ?? 0m)).ToList();
            impOpEx = 0m;
        }

        // Percepciones (IVA/IIBB) como tributos — sin esto, cmp.Total (que YA las incluye, ver
        // FacturacionService) no cerraba contra ImpTotConc+ImpNeto+ImpOpEx+ImpTrib+ImpIVA y ARCA
        // rechazaba con el error 10048. Bug real (2026-08-24): esta rama nunca leía cmp.Tributos.
        var tributosAfip = (cmp.Tributos ?? Array.Empty<TributoFiscal>())
            .Select(t => new AfipTributo(TributoId(t.Tipo), Descripcion(t.Tipo), t.BaseImponible,
                t.BaseImponible == 0m ? 0m : Math.Round(t.Importe / t.BaseImponible * 100m, 2), t.Importe))
            .ToList();
        var impTrib = tributosAfip.Sum(t => t.Importe);

        return new AfipComprobanteReq(
            PtoVta: cmp.PuntoVenta, CbteTipo: cbteTipo,
            Concepto: 1, // Productos — este POS no vende servicios.
            DocTipo: docTipo, DocNro: docNro,
            CbteNro: cmp.Numero, Fecha: cmp.Fecha,
            ImpTotal: cmp.Total, ImpNeto: cmp.Neto, ImpIva: cmp.Iva,
            ImpTotConc: 0m, ImpOpEx: impOpEx, ImpTrib: impTrib,
            CondicionIvaReceptorId: condicionIva, Ivas: tramos, Tributos: tributosAfip);
    }

    /// <summary>Id de tributo de WSFEv1 (tabla FEParamGetTiposTributos): 1=Impuestos nacionales
    /// (percepción de IVA), 2=Impuestos provinciales (percepción de IIBB). Aproximado a propósito
    /// del lado seguro — si ARCA lo rechaza con una observación de código de tributo, ese mensaje
    /// va a decir exactamente cuál corresponde y se ajusta acá.</summary>
    private static int TributoId(TipoTributoFiscal t) => t switch
    {
        TipoTributoFiscal.PercepcionIva => 1,
        TipoTributoFiscal.PercepcionIibb => 2,
        _ => 99
    };

    private static string Descripcion(TipoTributoFiscal t) => t switch
    {
        TipoTributoFiscal.PercepcionIva => "Percepcion IVA",
        TipoTributoFiscal.PercepcionIibb => "Percepcion IIBB",
        _ => "Otro"
    };

    /// <summary>Id de alícuota de WSFEv1 (tabla de parámetros FEParamGetTiposIva): 3=0%, 4=10,5%,
    /// 5=21%, 6=27%, 8=5%, 9=2,5%. El sistema hoy solo vende a 21%/10,5%/0%, pero se cubren todas
    /// por si algún artículo se recategoriza.</summary>
    private static int AlicuotaId(decimal alicuota) => alicuota switch
    {
        0.21m => 5,
        0.105m => 4,
        0.27m => 6,
        0.05m => 8,
        0.025m => 9,
        _ => 3
    };

    /// <summary>DocTipo de WSFEv1 (tabla FEParamGetTiposDoc): 80=CUIT, 86=CUIL, 96=DNI, 93=Pasaporte,
    /// 91=CI, 89=LE, 90=LC, 99=Consumidor Final sin identificar.</summary>
    private static (int DocTipo, string DocNro) MapearDocumento(ClienteFiscal? cliente, string? cuitCliente)
    {
        if (cliente is null)
            return string.IsNullOrWhiteSpace(cuitCliente) ? (99, "0") : (80, Limpiar(cuitCliente));

        var numero = Limpiar(cliente.NumeroDocumento);
        if (numero.Length == 0) return (99, "0");

        return cliente.TipoDocumento switch
        {
            TipoDocumentoFiscal.Cuit => (80, numero),
            TipoDocumentoFiscal.Cuil => (86, numero),
            TipoDocumentoFiscal.Dni => (96, numero),
            TipoDocumentoFiscal.Pasaporte => (93, numero),
            TipoDocumentoFiscal.Ci => (91, numero),
            TipoDocumentoFiscal.Le => (89, numero),
            TipoDocumentoFiscal.Lc => (90, numero),
            _ => (99, "0")
        };
    }

    /// <summary>CondicionIVAReceptorId de WSFEv1 (obligatorio desde RG 5616): 1=Resp. Inscripto,
    /// 4=Exento, 5=Consumidor Final, 6=Monotributo, 7=No Categorizado, 13=Monotributo Social.</summary>
    private static int MapearCondicionIva(ResponsabilidadIvaFiscal? r) => r switch
    {
        ResponsabilidadIvaFiscal.ResponsableInscripto => 1,
        ResponsabilidadIvaFiscal.Exento => 4,
        ResponsabilidadIvaFiscal.Monotributo => 6,
        ResponsabilidadIvaFiscal.MonotributoSocial => 13,
        ResponsabilidadIvaFiscal.NoCategorizado => 7,
        ResponsabilidadIvaFiscal.NoResponsable => 15,
        _ => 5 // ConsumidorFinal (default, incluido cliente no identificado)
    };

    private static string Limpiar(string? s) => (s ?? "").Replace("-", "").Replace(".", "").Trim();
}
