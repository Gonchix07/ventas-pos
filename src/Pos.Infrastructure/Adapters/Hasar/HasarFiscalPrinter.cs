using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions.Fiscal;
using Pos.Application.Common;

namespace Pos.Infrastructure.Adapters.Hasar;

/// <summary>
/// Impresora fiscal real: controlador fiscal HASAR de 2ª generación (probado contra SMH/PT-250F),
/// hablando el protocolo XML sobre HTTP directamente por la LAN.
/// </summary>
public class HasarFiscalPrinter : IFiscalPrinter, IDisposable
{
    private readonly HasarOptions _opciones;
    private readonly SecuenciaStore _secuencias;
    private readonly ILogger<HasarFiscalPrinter> _log;
    private readonly ConcurrentDictionary<string, HasarProtocolo> _equipos = new();

    public HasarFiscalPrinter(HasarOptions opciones, ILogger<HasarFiscalPrinter> log)
    {
        _opciones = opciones;
        _secuencias = new SecuenciaStore(opciones.EstadoDir);
        _log = log;
    }

    private HasarProtocolo Equipo(int idSucursal, int idCaja)
    {
        var cfg = _opciones.Resolver(idSucursal, idCaja)
            ?? throw new DomainException("IMPRESORA_NO_CONFIGURADA",
                $"No hay una impresora fiscal configurada para la caja {idCaja} de la sucursal {idSucursal}.");

        return _equipos.GetOrAdd($"{idSucursal}:{idCaja}",
            _ => new HasarProtocolo(cfg, _opciones, _secuencias, _log));
    }

    public async Task<ResultadoImpresion> ImprimirFiscalAsync(ComprobanteFiscal cmp, CancellationToken ct)
        => await EmitirAsync(cmp, notaCredito: false, ct);

    public async Task<ResultadoImpresion> ImprimirNotaCreditoAsync(ComprobanteFiscal cmp, CancellationToken ct)
        => await EmitirAsync(cmp, notaCredito: true, ct);

    private async Task<ResultadoImpresion> EmitirAsync(ComprobanteFiscal cmp, bool notaCredito, CancellationToken ct)
    {
        if (cmp.Items is null || cmp.Items.Count == 0)
            return new ResultadoImpresion(false, null, "El comprobante no tiene ítems para imprimir.");

        HasarProtocolo equipo;
        try { equipo = Equipo(cmp.IdSucursal, cmp.IdCaja); }
        catch (DomainException ex) { return new ResultadoImpresion(false, null, ex.Message); }

        // Un solo comprobante por vez contra el mismo equipo: tiene un único estado fiscal, y dos
        // ventas simultáneas sobre la misma caja mezclarían sus ítems en el mismo ticket.
        using var _ = await equipo.TomarAsync(ct);

        var abierto = false;
        try
        {
            await PrepararAsync(equipo, ct);

            // Los datos del cliente van ANTES de abrir el documento: la impresora los retiene y los
            // imprime en la cabecera al recibir AbrirDocumento.
            if (cmp.Cliente is not null)
            {
                await equipo.EjecutarAsync("CargarDatosCliente", new Dictionary<string, string>
                {
                    ["RazonSocial"] = HasarMapeo.Texto(cmp.Cliente.RazonSocial, 50),
                    ["NumeroDocumento"] = cmp.Cliente.NumeroDocumento ?? "",
                    ["ResponsabilidadIVA"] = HasarMapeo.Responsabilidad(cmp.Cliente.Responsabilidad),
                    ["TipoDocumento"] = HasarMapeo.TipoDocumento(cmp.Cliente.TipoDocumento),
                    ["Domicilio"] = HasarMapeo.Texto(cmp.Cliente.Domicilio, 50)
                }, ct);
            }

            var codigo = HasarMapeo.CodigoComprobante(cmp.Letra, notaCredito);
            var apertura = await equipo.EjecutarAsync("AbrirDocumento",
                new Dictionary<string, string> { ["CodigoComprobante"] = codigo }, ct);
            abierto = true;

            // El número lo asigna el controlador y ya viene en la apertura, antes de imprimir nada.
            var numeroFiscal = apertura.Campo("NumeroComprobante");

            foreach (var item in cmp.Items)
            {
                await equipo.EjecutarAsync("ImprimirItem", new Dictionary<string, string>
                {
                    ["Descripcion"] = HasarMapeo.Texto(item.Descripcion, 50),
                    ["Cantidad"] = HasarMapeo.Cantidad(item.Cantidad),
                    ["PrecioUnitario"] = HasarMapeo.Monto(item.PrecioUnitario),
                    ["CondicionIVA"] = item.AlicuotaIva > 0 ? "Gravado" : "Exento",
                    ["AlicuotaIVA"] = HasarMapeo.Monto(item.AlicuotaIva),
                    ["OperacionMonto"] = "ModoSumaMonto",
                    // Obligatorios aunque no haya impuesto interno: sin ellos el equipo rechaza el
                    // ítem con CMD_PARAM_NOT_PRESENT.
                    ["TipoImpuestoInterno"] = "IIFijoMonto",
                    ["MagnitudImpuestoInterno"] = "0.00",
                    ["ModoDisplay"] = "DisplayNo",
                    ["ModoBaseTotal"] = "ModoPrecioTotal",
                    ["UnidadReferencia"] = "1",
                    ["CodigoInterno"] = HasarMapeo.Texto(item.CodigoInterno, 20),
                    ["UnidadMedida"] = "Unidad"
                }, ct);

                if (item.Descuento > 0)
                {
                    await equipo.EjecutarAsync("ImprimirDescuentoItem", new Dictionary<string, string>
                    {
                        ["Descripcion"] = "DESCUENTO",
                        ["Monto"] = HasarMapeo.Monto(item.Descuento),
                        ["ModoDisplay"] = "DisplayNo",
                        ["ModoBaseTotal"] = "ModoPrecioTotal"
                    }, ct);
                }
            }

            // Tributos (percepciones de IVA/IIBB): van DESPUÉS de ítems/descuentos y ANTES de los
            // pagos — el manual es explícito en que una vez enviado el primer "ImprimirOtrosTributos"
            // quedan inhabilitados los ítems/descuentos, así que el orden acá no es arbitrario.
            foreach (var tributo in cmp.Tributos ?? Array.Empty<TributoFiscal>())
            {
                await equipo.EjecutarAsync("ImprimirOtrosTributos", new Dictionary<string, string>
                {
                    ["Codigo"] = HasarMapeo.Tributo(tributo.Tipo),
                    ["Descripcion"] = HasarMapeo.Texto(tributo.Descripcion, 30),
                    ["BaseImponible"] = HasarMapeo.Monto(tributo.BaseImponible),
                    ["Importe"] = HasarMapeo.Monto(tributo.Importe)
                }, ct);
            }

            foreach (var pago in cmp.Pagos ?? Array.Empty<PagoFiscal>())
            {
                await equipo.EjecutarAsync("ImprimirPago", new Dictionary<string, string>
                {
                    ["Descripcion"] = HasarMapeo.Texto(pago.Descripcion, 30),
                    ["Monto"] = HasarMapeo.Monto(pago.Monto),
                    ["Operacion"] = "Pagar",
                    ["ModoDisplay"] = "DisplayNo",
                    ["DescripcionAdicional"] = HasarMapeo.Texto(pago.DescripcionAdicional, 30),
                    ["CodigoFormaPago"] = HasarMapeo.FormaPago(pago.Fuente),
                    ["Cuotas"] = Math.Max(1, pago.Cuotas).ToString()
                }, ct);
            }

            var cierre = await equipo.EjecutarAsync("CerrarDocumento", null, ct);
            abierto = false;
            numeroFiscal = cierre.Campo("NumeroComprobante") ?? numeroFiscal;

            _log.LogInformation("Comprobante fiscal {Codigo} Nº {Numero} emitido en {Destino}",
                codigo, numeroFiscal, equipo.Destino);

            return new ResultadoImpresion(true, $"{codigo}-{numeroFiscal}", null, numeroFiscal);
        }
        catch (Exception ex)
        {
            // Si el comprobante quedó abierto a mitad de camino hay que cancelarlo: mientras siga
            // abierto el equipo rechaza cualquier otra emisión en esa caja.
            if (abierto) await CancelarSilenciosoAsync(equipo, ct);
            _log.LogError(ex, "Falló la emisión fiscal en {Destino}", equipo.Destino);
            return new ResultadoImpresion(false, null, Explicar(ex));
        }
    }

    public async Task<ResultadoImpresion> ArqueoXAsync(int idSucursal, int idCaja, CancellationToken ct)
        => await ReporteAsync(idSucursal, idCaja, "ReporteX", ct);

    public async Task<ResultadoImpresion> CierreZAsync(int idSucursal, int idCaja, CancellationToken ct)
        => await ReporteAsync(idSucursal, idCaja, "ReporteZ", ct);

    /// <summary>Arqueo X y cierre Z son el mismo comando fiscal, con distinto tipo de reporte.</summary>
    private async Task<ResultadoImpresion> ReporteAsync(int idSucursal, int idCaja, string reporte, CancellationToken ct)
    {
        HasarProtocolo equipo;
        try { equipo = Equipo(idSucursal, idCaja); }
        catch (DomainException ex) { return new ResultadoImpresion(false, null, ex.Message); }

        using var _ = await equipo.TomarAsync(ct);
        try
        {
            await PrepararAsync(equipo, ct);
            var r = await equipo.EjecutarAsync("CerrarJornadaFiscal",
                new Dictionary<string, string> { ["Reporte"] = reporte }, ct);
            var numero = r.Campo("Numero") ?? r.Campo("NumeroComprobante");
            return new ResultadoImpresion(true, $"{reporte}-{numero}", null, numero);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falló {Reporte} en {Destino}", reporte, equipo.Destino);
            return new ResultadoImpresion(false, null, Explicar(ex));
        }
    }

    /// <summary>
    /// Deja el equipo en condiciones de recibir un comprobante nuevo. Un corte de luz, un cierre
    /// de la app a mitad de venta o un error de red dejan el documento abierto, y en ese estado la
    /// impresora rechaza todo lo demás — así que se limpia antes de empezar, no después de fallar.
    /// </summary>
    private async Task PrepararAsync(HasarProtocolo equipo, CancellationToken ct)
    {
        var estado = await equipo.EjecutarAsync("ConsultarEstado", null, ct);
        if (estado.Tiene("DocumentoFiscalAbierto") || estado.Tiene("DocumentoAbierto"))
        {
            _log.LogWarning("La impresora {Destino} tenía un documento abierto ({Comprobante}); se cancela.",
                equipo.Destino, estado.Campo("ComprobanteEnCurso"));
            await equipo.EjecutarAsync("Cancelar", null, ct);
        }
    }

    private async Task CancelarSilenciosoAsync(HasarProtocolo equipo, CancellationToken ct)
    {
        try { await equipo.EjecutarAsync("Cancelar", null, ct); }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo cancelar el documento abierto en {Destino}. " +
                "La caja va a quedar bloqueada hasta resolverlo.", equipo.Destino);
        }
    }

    /// <summary>Traduce los códigos del equipo a algo que el cajero pueda accionar.</summary>
    private static string Explicar(Exception ex) => ex switch
    {
        HasarException h => h.Codigo switch
        {
            "POS_DOCUMENT_BEYOND_FISCAL_DAY" =>
                "La jornada fiscal anterior quedó sin cerrar. Hay que emitir el Cierre Z pendiente " +
                "antes de poder facturar.",
            "CONTROLADOR_OCUPADO" =>
                "La impresora fiscal está ocupada y no respondió a tiempo.",
            _ => h.Message
        },
        TaskCanceledException or HttpRequestException =>
            "No hay comunicación con la impresora fiscal. Verificá que esté encendida y en red.",
        _ => ex.Message
    };

    public void Dispose()
    {
        foreach (var e in _equipos.Values) e.Dispose();
    }
}
