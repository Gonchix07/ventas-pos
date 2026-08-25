using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Application.Abstractions;
using Pos.Application.Abstractions.Fiscal;
using Pos.Application.Abstractions.Interfase;
using Pos.Application.Common;
using Pos.Application.Facturacion;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Notas de crédito sobre comprobantes ya emitidos: anulación total, por artículos (líneas
/// completas) o por diferencia de precio (importe suelto, prorrateado entre las alícuotas de la
/// factura original).
///
/// La devolución al cliente es siempre en EFECTIVO por ahora, sin importar cómo se pagó la
/// factura. Queda registrada como un movimiento de caja negativo, así el arqueo/cierre del cajero
/// la muestra aparte como anulación y le resta al total a rendir.
/// </summary>
public class NotaCreditoService : INotaCreditoService
{
    private static readonly TimeSpan TimeoutFiscal = TimeSpan.FromSeconds(20);
    // Mismo criterio que FacturacionService: reintentos por CAE inaccesible antes de pasar a
    // contingencia con el CAEA precargado (ver ICaeaCargadoService).
    private const int MaxIntentosCae = 3;
    private static readonly TimeSpan EsperaEntreIntentosCae = TimeSpan.FromMilliseconds(500);

    private readonly PosDbContext _db;
    private readonly IFiscalService _fiscal;
    private readonly ICaeaCargadoService _caeaCargado;
    private readonly IFiscalPrinter _impresora;
    private readonly ICurrentUser _currentUser;
    private readonly ISupervisorAuthService _supervisorAuth;
    private readonly IInterfaseContableService _interfase;

    public NotaCreditoService(PosDbContext db, IFiscalService fiscal, ICaeaCargadoService caeaCargado,
        IFiscalPrinter impresora, ICurrentUser currentUser, ISupervisorAuthService supervisorAuth,
        IInterfaseContableService interfase)
    {
        _db = db; _fiscal = fiscal; _caeaCargado = caeaCargado; _impresora = impresora; _currentUser = currentUser;
        _supervisorAuth = supervisorAuth; _interfase = interfase;
    }

    // ---------- Búsqueda ----------

    public async Task<IReadOnlyList<ComprobanteAnulableDto>> BuscarAsync(int idSucursal, string texto,
        DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        texto = (texto ?? "").Trim();

        // Sólo comprobantes de venta (signo +1): una nota de crédito no se acredita a sí misma.
        var q = from c in _db.CabecerasComprobantes.AsNoTracking()
                join t in _db.TiposComprobante.AsNoTracking() on c.IdTipoComprobante equals t.IdTipoComprobante
                where c.IdSucursal == idSucursal && t.Signo == 1
                select new { c, t };

        if (desde is not null) q = q.Where(x => x.c.Fecha >= desde.Value.Date);
        if (hasta is not null) q = q.Where(x => x.c.Fecha < hasta.Value.Date.AddDays(1));

        if (texto.Length > 0)
        {
            q = from x in q
                join cli in _db.Clientes.AsNoTracking() on x.c.IdCliente equals cli.IdCliente into gj
                from cli in gj.DefaultIfEmpty()
                where x.c.NumeroCompleto!.Contains(texto)
                      || (cli != null && (cli.Descripcion.Contains(texto) || cli.Cuit!.Contains(texto)))
                select x;
        }

        // Se ordena ANTES de proyectar: EF no traduce OrderBy sobre una proyección a record.
        var cabeceras = await q.OrderByDescending(x => x.c.Fecha).Take(100)
            .Select(x => new { x.c.IdComprobante, x.c.NumeroCompleto, x.c.Letra, x.c.Fecha,
                               x.c.IdCliente, x.c.Total,
                               x.c.PercepcionIva21, x.c.PercepcionIva105, x.c.PercepcionIibb })
            .ToListAsync(ct);

        var ids = cabeceras.Select(c => c.IdComprobante).ToList();
        var acreditado = await AcreditadoPorComprobanteAsync(idSucursal, ids, ct);
        var clientes = await DescripcionesClientesAsync(cabeceras.Select(c => c.IdCliente), ct);

        return cabeceras.Select(c =>
        {
            var ya = acreditado.GetValueOrDefault(c.IdComprobante, 0m);
            var saldo = NotaCreditoReglas.SaldoAnulable(c.Total, ya);
            return new ComprobanteAnulableDto(idSucursal, c.IdComprobante, c.NumeroCompleto ?? "",
                c.Letra, c.Fecha, c.IdCliente,
                c.IdCliente is int id ? clientes.GetValueOrDefault(id) : null,
                c.Total, ya, saldo, saldo > 0,
                c.PercepcionIva21, c.PercepcionIva105, c.PercepcionIibb);
        }).ToList();
    }

    public async Task<ComprobanteAnulableDetalleDto> ObtenerAsync(int idSucursal, int idComprobante,
        CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        var cab = await _db.CabecerasComprobantes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IdSucursal == idSucursal && c.IdComprobante == idComprobante, ct)
            ?? throw new DomainException("NO_ENCONTRADO", "El comprobante no existe.");

        var lineas = await _db.DetallesComprobantes.AsNoTracking()
            .Where(d => d.IdSucursal == idSucursal && d.IdComprobante == idComprobante)
            .OrderBy(d => d.IdDetalleComprobante)
            .ToListAsync(ct);

        var anuladas = await CantidadAnuladaPorLineaAsync(idSucursal, idComprobante, ct);
        var ya = (await AcreditadoPorComprobanteAsync(idSucursal, new[] { idComprobante }, ct))
            .GetValueOrDefault(idComprobante, 0m);
        var saldo = NotaCreditoReglas.SaldoAnulable(cab.Total, ya);

        var clientes = await DescripcionesClientesAsync(new[] { cab.IdCliente }, ct);

        return new ComprobanteAnulableDetalleDto(
            new ComprobanteAnulableDto(idSucursal, idComprobante, cab.NumeroCompleto ?? "", cab.Letra,
                cab.Fecha, cab.IdCliente,
                cab.IdCliente is int id ? clientes.GetValueOrDefault(id) : null,
                cab.Total, ya, saldo, saldo > 0,
                cab.PercepcionIva21, cab.PercepcionIva105, cab.PercepcionIibb),
            lineas.Select(d =>
            {
                var cantidadYaAnulada = anuladas.GetValueOrDefault(d.IdDetalleComprobante);
                var disponible = d.Cantidad - cantidadYaAnulada;
                return new LineaAnulableDto(d.IdDetalleComprobante, d.IdPresentacion,
                    d.DescripcionTicket, d.Cantidad, d.PrecioUnit, d.Descuento, d.AlicuotaIva,
                    d.Importe, cantidadYaAnulada, disponible, disponible <= 0m);
            }).ToList());
    }

    // ---------- Emisión ----------

    public async Task<NotaCreditoResponse> EmitirAsync(EmitirNotaCreditoRequest req, CancellationToken ct = default)
    {
        await CajaAccesoHelper.AsegurarCajaOperableAsync(_db, _currentUser, req.IdSucursal, req.IdCaja, false, ct);
        await _supervisorAuth.ExigirAsync(req.CodigoSupervisor, ct);

        var origen = await _db.CabecerasComprobantes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IdSucursal == req.IdSucursal && c.IdComprobante == req.IdComprobanteOrigen, ct)
            ?? throw new DomainException("NO_ENCONTRADO", "El comprobante a anular no existe.");

        var tipoOrigen = await _db.TiposComprobante.AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdTipoComprobante == origen.IdTipoComprobante, ct);
        if (tipoOrigen?.Signo != 1)
            throw new DomainException("COMPROBANTE_NO_ANULABLE",
                "Sólo se puede emitir una nota de crédito sobre una factura, no sobre otra nota de crédito.");

        var lote = await ObtenerLoteAbiertoAsync(req.IdSucursal, req.IdCaja, ct)
            ?? throw new DomainException("SIN_LOTE_ABIERTO",
                "No hay una caja abierta: la devolución en efectivo tiene que quedar registrada en un turno.");

        // Estado actual de lo ya acreditado, para no pasarse del total de la factura.
        var yaAcreditado = (await AcreditadoPorComprobanteAsync(req.IdSucursal, new[] { req.IdComprobanteOrigen }, ct))
            .GetValueOrDefault(req.IdComprobanteOrigen, 0m);
        var saldo = NotaCreditoReglas.SaldoAnulable(origen.Total, yaAcreditado);
        if (saldo <= 0)
            throw new DomainException("SIN_SALDO_ANULABLE",
                $"El comprobante {origen.NumeroCompleto} ya fue acreditado en su totalidad.");

        var detallesOrigen = await _db.DetallesComprobantes.AsNoTracking()
            .Where(d => d.IdSucursal == req.IdSucursal && d.IdComprobante == req.IdComprobanteOrigen)
            .OrderBy(d => d.IdDetalleComprobante).ToListAsync(ct);
        var anuladas = await CantidadAnuladaPorLineaAsync(req.IdSucursal, req.IdComprobanteOrigen, ct);

        // Líneas de la NC a construir: (descripción, cantidad, precio unitario, alícuota, importe,
        // presentación, línea de origen).
        var lineasNc = ArmarLineas(req, detallesOrigen, anuladas, saldo);
        if (lineasNc.Count == 0)
            throw new DomainException("NADA_PARA_ANULAR", "No hay nada para anular con esos datos.");

        var totalNc = lineasNc.Sum(l => l.Importe);

        // "Anulación total" tiene que arrastrar también las percepciones (IVA 21%/10,5%/IIBB) que
        // todavía queden sin acreditar — viven en la cabecera del comprobante original
        // (PercepcionIva21/105/Iibb), no en ninguna línea de detalle, así que ArmarLineas nunca las
        // ve. Sin esto, el saldo anulable (que SÍ las incluye, ver SaldoAnulable) nunca se podía
        // acreditar completo en una factura con percepciones: quedaba un remanente perdido sin
        // aviso, Y la devolución en efectivo le daba de menos al cliente (ver "devoluciones" más
        // abajo, que reparte exactamente totalNc).
        // No hace falta saber cuánta percepción ya se acreditó en NC anteriores: "saldo" ya lo
        // sabe (es el total original menos TODO lo acreditado hasta ahora, sin importar de qué
        // estaba compuesto) — lo que falta entre el saldo y lo que suman los artículos de ESTA NC
        // es, por descarte, la percepción (u otro concepto fuera de línea) que todavía falta.
        var percepcionRestante = req.Tipo == TipoAnulacion.Total
            ? Math.Round(saldo - totalNc, 2, MidpointRounding.AwayFromZero) : 0m;
        var (percepcionIva21Nc, percepcionIva105Nc, percepcionIibbNc) = percepcionRestante > 0.005m
            ? NotaCreditoReglas.RepartirPercepcion(percepcionRestante, origen.PercepcionIva21, origen.PercepcionIva105, origen.PercepcionIibb)
            : (0m, 0m, 0m);
        totalNc += percepcionIva21Nc + percepcionIva105Nc + percepcionIibbNc;

        if (!NotaCreditoReglas.ImporteAcreditable(totalNc, saldo))
            throw new DomainException("EXCEDE_SALDO_ANULABLE",
                $"La nota de crédito (${totalNc:0.00}) supera el saldo anulable del comprobante (${saldo:0.00}).");

        var letra = origen.Letra ?? "B";
        var tipoNc = await _db.TiposComprobante.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Letra == letra && t.Signo == -1, ct)
            ?? throw new DomainException("TIPO_COMPROBANTE_INEXISTENTE",
                $"No hay un tipo de nota de crédito configurado para la letra {letra}.");

        var puntoVenta = await _db.PuntosVenta.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdSucursal == req.IdSucursal && p.IdPuntoVenta == lote.IdPuntoVenta, ct)
            ?? throw new DomainException("PUNTO_VENTA_INEXISTENTE", "El punto de venta del lote no existe.");

        // Mismo camino único que la venta: lo define el tipo de punto de venta de la CAJA que
        // emite la NC (no el de la factura original — una caja Fiscal siempre sale por el
        // controlador, una Electrónica siempre por CAE, sin importar qué modalidad tenía la
        // venta que se está acreditando).
        var esElectronica = puntoVenta.IdTipoPuntoVenta == (int)ModalidadPuntoVenta.Electronica;

        var idEmpresa = await _db.Sucursales.AsNoTracking()
            .Where(s => s.IdSucursal == req.IdSucursal).Select(s => s.IdEmpresa).FirstOrDefaultAsync(ct);

        var medios = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
            .ToDictionaryAsync(m => m.IdMedioPago, m => m, ct);
        var efectivo = medios.Values.FirstOrDefault(m => m.TipoPago?.Fuente == FuentePago.Efectivo)
            ?? throw new DomainException("MEDIO_EFECTIVO_INEXISTENTE",
                "No hay un medio de pago en efectivo configurado para devolver el importe.");

        decimal totalNeto = 0, totalIva = 0, netoExento = 0;
        foreach (var l in lineasNc)
        {
            var (neto, iva) = DesglioIva.Calcular(l.Importe, l.Alicuota);
            totalNeto += neto; totalIva += iva;
            if (l.Alicuota == 0m) netoExento += neto;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var cbteTipoNc = int.Parse(tipoNc.CodigoArca!);
            long numero;
            if (esElectronica)
            {
                // Igual que en FacturacionService: para Electrónica, ARCA es la única fuente de
                // verdad del próximo número — nunca se reserva ni se persiste localmente, así que
                // no puede desincronizarse. El lock se toma acá (antes recién más abajo) para
                // serializar "preguntar+pedir CAE" completo contra otra emisión concurrente del
                // mismo punto de venta.
                await RecursoLockHelper.AdquirirAsync(_db, $"Comprobante:{req.IdSucursal}", ct);
                numero = await _fiscal.ObtenerProximoNumeroAsync(idEmpresa, puntoVenta.NumeroPuntoVenta, cbteTipoNc, ct);
            }
            else
            {
                // Fiscal: serie propia de notas de crédito (independiente de la de facturas), propia
                // por TIPO de comprobante (NC A y NC B del mismo punto de venta nunca comparten
                // numeración) — acá SÍ se persiste en Numeros porque no hay ARCA de por medio que
                // trackee un "último autorizado" con el que contrastar.
                var idNumero = NumeradorIds.NotaCredito(lote.IdPuntoVenta, cbteTipoNc);
                await AsegurarNumeradorAsync(req.IdSucursal, idNumero, lote.IdPuntoVenta, ct);
                numero = await IncrementarNumeradorAsync(req.IdSucursal, idNumero, ct);

                await RecursoLockHelper.AdquirirAsync(_db, $"Comprobante:{req.IdSucursal}", ct);
            }

            // Re-chequeo del saldo DESPUÉS de tomar el lock: entre la lectura de más arriba y este
            // punto, otra caja pudo haber acreditado la misma factura. Sin esto, dos notas de
            // crédito concurrentes sobre el mismo comprobante podrían sumar más que su total.
            var yaAcreditadoAhora = (await AcreditadoPorComprobanteAsync(req.IdSucursal, new[] { req.IdComprobanteOrigen }, ct))
                .GetValueOrDefault(req.IdComprobanteOrigen, 0m);
            var saldoAhora = NotaCreditoReglas.SaldoAnulable(origen.Total, yaAcreditadoAhora);
            if (!NotaCreditoReglas.ImporteAcreditable(totalNc, saldoAhora))
                throw new DomainException("EXCEDE_SALDO_ANULABLE",
                    $"El saldo anulable del comprobante cambió (${saldoAhora:0.00}). Volvé a intentar.");

            // Reversión completa: mismo día, 100% de la venta en una sola NC, y el lote de la VENTA
            // ORIGINAL (no el del cajero que emite la NC) todavía abierto — si ya cerró, esa
            // rendición quedó fija y no se puede tocar retroactivamente. Solo entonces se anulan
            // todos los medios originales (cupones incluidos); en cualquier otro caso, sigue el
            // comportamiento de siempre (devolución genérica en efectivo).
            var loteOrigenAbierto = false;
            if (req.Tipo == TipoAnulacion.Total && yaAcreditadoAhora == 0m)
            {
                var idLoteOrigen = await _db.MovimientosCaja.AsNoTracking()
                    .Where(m => m.IdSucursal == req.IdSucursal && m.IdComprobante == req.IdComprobanteOrigen)
                    .Select(m => (int?)m.IdLote).FirstOrDefaultAsync(ct);
                if (idLoteOrigen is int idLo)
                    loteOrigenAbierto = await _db.LotesCaja.AsNoTracking()
                        .AnyAsync(l => l.IdSucursal == req.IdSucursal && l.IdLote == idLo && l.Estado == EstadoLote.Abierto, ct);
            }
            var esReversionCompleta = NotaCreditoReglas.EsReversionCompleta(req.Tipo, totalNc, origen.Total,
                yaAcreditadoAhora, origen.Fecha, DateTime.UtcNow, loteOrigenAbierto);

            var idComprobante = (await _db.CabecerasComprobantes.Where(c => c.IdSucursal == req.IdSucursal)
                .Select(c => c.IdComprobante).MaxAsync(x => (int?)x, ct) ?? 0) + 1;

            var cabecera = new CabeceraComprobante
            {
                IdSucursal = req.IdSucursal, IdComprobante = idComprobante,
                IdTipoComprobante = tipoNc.IdTipoComprobante, IdCliente = origen.IdCliente,
                IdPuntoVenta = lote.IdPuntoVenta, IdOperacion = null, Letra = letra,
                NumeroCompleto = NumeroComprobanteFormatter.Formatear(puntoVenta.NumeroPuntoVenta, numero),
                Fecha = DateTime.UtcNow, Neto = totalNeto, Iva = totalIva,
                Percepciones = percepcionIva21Nc + percepcionIva105Nc + percepcionIibbNc,
                PercepcionIva21 = percepcionIva21Nc, PercepcionIva105 = percepcionIva105Nc,
                PercepcionIibb = percepcionIibbNc, AlicuotaIibb = percepcionIibbNc > 0 ? origen.AlicuotaIibb : 0m,
                Total = totalNc, Estado = EstadoComprobante.Persistido,
                IdComprobanteOrigen = req.IdComprobanteOrigen,
                MotivoAnulacion = Limpiar(req.Motivo)
            };
            foreach (var l in lineasNc)
            {
                cabecera.Detalles.Add(new DetalleComprobante
                {
                    IdSucursal = req.IdSucursal, IdComprobante = idComprobante,
                    IdPresentacion = l.IdPresentacion, DescripcionTicket = l.Descripcion,
                    Cantidad = l.Cantidad, PrecioUnit = l.PrecioUnitario, Descuento = 0,
                    AlicuotaIva = l.Alicuota, Importe = l.Importe,
                    IdDetalleOrigen = l.IdDetalleOrigen
                });
            }
            _db.CabecerasComprobantes.Add(cabecera);

            _db.ComprobantesAsociados.Add(new ComprobanteAsociado
            {
                IdComprobanteOrigen = req.IdComprobanteOrigen,
                IdComprobanteAsociado = idComprobante
            });

            // Devolución: uno o más movimientos NEGATIVOS (uno por medio). El acumulador del
            // arqueo/cierre los clasifica como anulación por el signo del tipo de comprobante, no
            // por el signo del monto, así que la plata sale de la caja y además se muestra
            // discriminada. Caso general: un solo movimiento en Efectivo, como siempre. Reversión
            // completa: un movimiento por cada medio de la venta original (y esos pagos originales
            // quedan marcados Anulado=true — ver RevertirPagosOriginalesAsync).
            var devoluciones = esReversionCompleta
                ? await RevertirPagosOriginalesAsync(req.IdSucursal, req.IdComprobanteOrigen,
                    origen.NumeroCompleto ?? "", medios, ct)
                : new List<(int IdMedioPago, decimal Monto)>();
            // Si por algún motivo no salió ninguna devolución de la reversión (ej. el vuelto neteó
            // justo todo el único leg de efectivo), no se puede dejar la NC sin ningún movimiento de
            // caja: cae al comportamiento genérico para ese resto.
            if (devoluciones.Count == 0)
                devoluciones.Add((efectivo.IdMedioPago, totalNc));

            foreach (var (idMedioPago, monto) in devoluciones)
            {
                var movPago = new MovimientoPago { IdMedioPago = idMedioPago, Total = -monto, Redondeo = 0 };
                _db.MovimientosPagos.Add(movPago);
                await _db.SaveChangesAsync(ct); // la BD asigna IdMovPagos

                var idMov = (await _db.MovimientosCaja.Where(m => m.IdSucursal == req.IdSucursal)
                    .Select(m => m.IdMovCaja).MaxAsync(x => (int?)x, ct) ?? 0) + 1;
                _db.MovimientosCaja.Add(new MovimientoCaja
                {
                    IdSucursal = req.IdSucursal, IdMovCaja = idMov, IdUsuario = _currentUser.IdUsuario ?? 0,
                    IdCaja = req.IdCaja, IdComprobante = idComprobante, IdLote = lote.IdLote,
                    IdMovPagos = movPago.IdMovPagos, Estado = "Confirmado", Fecha = DateTime.UtcNow
                });
            }

            // Si la factura se había cargado a cuenta corriente, la NC la descarga (Haber). Se
            // guarda el resultado (no solo el if) porque también lo necesita movstock.modofact.
            var esNcDeCuentaCorriente = await TieneAsientoCuentaCorrienteAsync(req.IdSucursal, req.IdComprobanteOrigen, ct);
            if (origen.IdCliente is int idCliente && esNcDeCuentaCorriente)
            {
                _db.CuentasCorrientes.Add(new CuentaCorriente
                {
                    IdSucursal = req.IdSucursal, IdCliente = idCliente,
                    IdComprobante = idComprobante, Debe = 0, Haber = totalNc
                });
            }

            // Se arma completo (ítems + cliente + pagos) de una sola vez: lo necesitan tanto el
            // pedido de CAE (Electrónica, el array de IVA de WSFEv1 sale de la alícuota por ítem)
            // como la impresión fiscal (Fiscal, más abajo) — antes se armaba dos veces distinto.
            var cf = new ComprobanteFiscal(idEmpresa, puntoVenta.NumeroPuntoVenta,
                tipoNc.Descripcion, letra, numero, null, totalNeto, totalIva, totalNc, DateTime.UtcNow,
                req.IdSucursal, req.IdCaja,
                Cliente: await ClienteFiscalAsync(origen.IdCliente, letra, ct),
                Items: lineasNc.Select(l => new ItemFiscal(l.Descripcion, l.Cantidad,
                    l.PrecioUnitario, l.Alicuota, 0m, l.IdPresentacion.ToString())).ToList(),
                // Caso general: un solo pago en Efectivo por el total, como siempre. Reversión
                // completa: un renglón por cada medio realmente devuelto.
                Pagos: devoluciones.Select(d => new PagoFiscal(
                    medios.GetValueOrDefault(d.IdMedioPago)?.Descripcion ?? $"Medio {d.IdMedioPago}",
                    d.Monto, medios.GetValueOrDefault(d.IdMedioPago)?.TipoPago?.Fuente ?? FuentePago.Efectivo,
                    null, 1)).ToList(),
                CodigoArca: tipoNc.CodigoArca,
                // Obligatorio para WSFEv1 (error 10197 si falta): el comprobante que esta NC
                // acredita. PtoVta y Numero salen de su propio NumeroCompleto ("PPPP-NNNNNNNN"),
                // no del de esta NC — puede haberse emitido por otro punto de venta.
                Asociado: tipoOrigen?.CodigoArca is string codigoArcaOrigen
                    ? new ComprobanteAsociadoFiscal(
                        int.Parse(origen.NumeroCompleto!.Split('-')[0]), codigoArcaOrigen,
                        long.Parse(origen.NumeroCompleto!.Split('-')[1]))
                    : null,
                // Percepción que esta NC acredita (solo en Anulación total, ver más arriba) — sin
                // esto, ImpTotal no cerraba contra ImpNeto+ImpIva+ImpTrib y ARCA rechazaba con el
                // mismo error 10048 que ya se corrigió una vez en FacturacionService. BaseImponible
                // en 0: no se persistió la base original de cada percepción (solo el importe
                // final), y ARCA no la valida contra el resto del comprobante.
                Tributos: BuildTributosNc(percepcionIva21Nc, percepcionIva105Nc, percepcionIibbNc));

            // CAE de la nota de crédito — SOLO en caja Electrónica: en Fiscal, el controlador Hasar
            // ya autoriza la NC al imprimirla (más abajo, fuera de la tx), pedir CAE ahí no
            // correspondería a nada real. Mismos reintentos + contingencia CAEA que
            // FacturacionService — un rechazo de negocio de ARCA no se reintenta ni pasa a CAEA
            // (reintentar el mismo dato rechazado no cambia nada); solo la falla de conectividad lo
            // hace, y sin autorización de ningún tipo la NC no se emite.
            if (esElectronica)
            {
                ResultadoCae? resultadoCae = null;
                Exception? fallaConectividad = null;
                try
                {
                    resultadoCae = await ResilientCall.ConTimeoutYReintentosAsync(
                        ct2 => _fiscal.SolicitarCaeAsync(cf, ct2), TimeoutFiscal, MaxIntentosCae, EsperaEntreIntentosCae, ct);
                }
                catch (Exception ex)
                {
                    fallaConectividad = ex;
                }

                if (resultadoCae is { Ok: true })
                {
                    cabecera.Cae = resultadoCae.Cae; cabecera.CaeVencimiento = resultadoCae.Vencimiento; cabecera.EsCaea = false;
                }
                else if (resultadoCae is { Ok: false })
                {
                    throw new DomainException("FISCAL_INDISPONIBLE",
                        $"No se pudo autorizar la nota de crédito: {resultadoCae.Error}");
                }
                else
                {
                    var caeaVigente = await _caeaCargado.BuscarVigenteAsync(idEmpresa, DateTime.UtcNow, ct);
                    if (caeaVigente is null)
                        throw new DomainException("FISCAL_INDISPONIBLE",
                            $"No se pudo conectar con ARCA ({fallaConectividad?.Message}) y no hay un CAEA cargado vigente para hoy — no se puede emitir la nota de crédito.");
                    cabecera.Cae = caeaVigente.Valor; cabecera.CaeVencimiento = caeaVigente.VigenciaHasta; cabecera.EsCaea = true;
                }
                cabecera.Estado = EstadoComprobante.CaeOk;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Interfase contable (best-effort, ver IInterfaseContableService): mismo criterio que
            // FacturacionService (iva_adic/periva = percepción IVA 21%+10,5%, impperc/porciibb1 =
            // percepción IIBB) — solo que acá casi siempre da 0, porque la NC solo llega a
            // acreditar percepción en una Anulación total (ver más arriba). baseimp queda en 0: no
            // se persistió la base imponible de IIBB de la factura original, solo el importe final.
            var datosClienteNc = await DatosInterfaseClienteAsync(origen.IdCliente, ct);
            var codigoEmpresaNc = await _db.Sucursales.AsNoTracking()
                .Where(s => s.IdSucursal == req.IdSucursal)
                .Select(s => s.Empresa!.CodigoInterno).FirstOrDefaultAsync(ct) ?? "";
            await _interfase.RegistrarVentaAsync(new IvaVtaInterfase(
                Fecha: cabecera.Fecha,
                Cliente: Truncar(datosClienteNc?.CodigoInt, 5),
                Nombre: Truncar(datosClienteNc?.Descripcion, 30),
                CondIva: InterfaseContableReglas.CondIva(datosClienteNc?.IdCondIva),
                Cuit: Truncar(datosClienteNc?.Cuit, 13),
                Tipo: InterfaseContableReglas.TipoComprobante(tipoNc.Signo, letra),
                Prenum: InterfaseContableReglas.Prenum(puntoVenta.NumeroPuntoVenta),
                Numero: InterfaseContableReglas.Numero(numero),
                Neto: totalNeto, Iva: totalIva,
                IvaAdic: percepcionIva21Nc + percepcionIva105Nc, Exento: netoExento,
                Periva: percepcionIva21Nc + percepcionIva105Nc, Final: totalNc,
                BaseImp: 0m, ImpPerc: percepcionIibbNc,
                PorcIibb1: percepcionIibbNc > 0 ? origen.AlicuotaIibb : 0m,
                Prov: InterfaseContableReglas.ProvFijo, Empresa: codigoEmpresaNc,
                IdVentaSalon: idComprobante), ct);

            // movstock: una fila por línea de la NC. impint y codconv van en 0/NULL — la NC no
            // rastrea impuesto interno por línea ni "oferta aplicada" (eso es un concepto de la
            // venta original, no de su reversión). reparto usa la operación de la VENTA ORIGINAL
            // (la NC en sí no tiene una operación propia, ver IdOperacion=null más arriba) —
            // confirmado con el usuario (2026-08-21): la NC referencia la misma operación que la
            // factura que acredita.
            var idsPresNc = lineasNc.Select(l => l.IdPresentacion).Distinct().ToList();
            var codigosArticuloNc = await (
                from pr in _db.Presentaciones.AsNoTracking().Where(p => idsPresNc.Contains(p.IdPresentacion))
                join a in _db.Articulos.AsNoTracking() on pr.IdArticulo equals a.IdArticulo
                select new { pr.IdPresentacion, a.CodigoInterno }
            ).ToDictionaryAsync(x => x.IdPresentacion, x => x.CodigoInterno, ct);
            var modoFactNc = InterfaseContableReglas.ModoFact(esNcDeCuentaCorriente);
            var repartoNc = InterfaseContableReglas.Reparto(origen.IdOperacion ?? idComprobante);
            var filasMovStockNc = lineasNc.Select(l => new MovStockInterfase(
                Fecha: cabecera.Fecha,
                Articulo: InterfaseContableReglas.Articulo(codigosArticuloNc.GetValueOrDefault(l.IdPresentacion, "")),
                Salida: l.Cantidad, Descto: 0m, Unitario: l.PrecioUnitario, Pesos: l.Importe,
                DeDeposito: InterfaseContableReglas.DepositoFijo,
                Cliente: Truncar(datosClienteNc?.CodigoInt, 5), Nombre: Truncar(datosClienteNc?.Descripcion, 30),
                Tipo: InterfaseContableReglas.TipoComprobante(tipoNc.Signo, letra),
                Numero: cabecera.NumeroCompleto!,
                Vendedor: null, Lista: InterfaseContableReglas.ListaFija, ImpInt: 0m,
                Reparto: repartoNc, ModoFact: modoFactNc, CodConv: null,
                Prov: InterfaseContableReglas.ProvFijo, Empresa: codigoEmpresaNc,
                IdVentaSalon: idComprobante, Iva: l.Alicuota * 100m, Periva: 0m)).ToList();
            await _interfase.RegistrarMovStockAsync(filasMovStockNc, ct);

            // ctacte: solo si esta NC descargó una cuenta corriente (misma condición que el asiento
            // de CuentaCorriente de arriba) — confirmado con el usuario, no se manda "en 0" en el
            // resto de las NC.
            if (esNcDeCuentaCorriente)
            {
                await _interfase.RegistrarCtaCteAsync(new CtaCteInterfase(
                    Fecha: cabecera.Fecha,
                    Tipo: InterfaseContableReglas.TipoComprobante(tipoNc.Signo, letra),
                    Prenum: InterfaseContableReglas.Prenum(puntoVenta.NumeroPuntoVenta),
                    Numero: InterfaseContableReglas.Numero(numero),
                    Debe: 0m, Haber: totalNc,
                    Cliente: Truncar(datosClienteNc?.CodigoInt, 5),
                    Prov: InterfaseContableReglas.ProvFijo, Empresa: codigoEmpresaNc,
                    IdVentaSalon: idComprobante), ct);
            }

            // comision: usa Reparto de la venta original, mismo criterio que movstock arriba.
            await _interfase.RegistrarComisionAsync(new ComisionInterfase(
                Fecha: cabecera.Fecha,
                Cliente: Truncar(datosClienteNc?.CodigoInt, 5),
                Tipo: InterfaseContableReglas.TipoComprobante(tipoNc.Signo, letra),
                Prenum: InterfaseContableReglas.Prenum(puntoVenta.NumeroPuntoVenta),
                Numero: InterfaseContableReglas.Numero(numero),
                Neto: totalNeto, Final: totalNc, Vendedor: null,
                CondVta: InterfaseContableReglas.CondVta(esNcDeCuentaCorriente),
                Reparto: repartoNc,
                Prov: InterfaseContableReglas.ProvFijo, Empresa: codigoEmpresaNc,
                IdVentaSalon: idComprobante, Hora: InterfaseContableReglas.Hora(cabecera.Fecha)), ct);

            // Impresión fiscal — SOLO en caja Fiscal (best-effort, fuera de la transacción: la NC ya
            // está registrada y el efectivo ya salió de la caja; un fallo de la impresora no la
            // invalida). En Electrónica no hay controlador que imprima nada acá: la comandera local
            // la maneja el navegador, igual que una factura Electrónica o un Presupuesto.
            ResultadoImpresion impresion;
            if (esElectronica)
            {
                impresion = new ResultadoImpresion(true, null, null);
            }
            else
            {
                try
                {
                    impresion = await ResilientCall.ConTimeoutAsync(
                        ct2 => _impresora.ImprimirNotaCreditoAsync(cf, ct2), TimeoutFiscal, ct);
                }
                catch (Exception ex)
                {
                    impresion = new ResultadoImpresion(false, null, ex.Message);
                }
                if (impresion.Ok)
                {
                    cabecera.Estado = EstadoComprobante.Impreso;
                    await _db.SaveChangesAsync(ct);
                }
            }

            var devolucionesDto = devoluciones.Select(d => new DevolucionMedioDto(d.IdMedioPago,
                medios.GetValueOrDefault(d.IdMedioPago)?.Descripcion ?? $"Medio {d.IdMedioPago}", d.Monto)).ToList();
            var devueltoEnEfectivo = devolucionesDto.Where(d => d.IdMedioPago == efectivo.IdMedioPago).Sum(d => d.Monto);

            return new NotaCreditoResponse(req.IdSucursal, idComprobante, cabecera.NumeroCompleto!,
                letra, cabecera.Cae, cabecera.CaeVencimiento, cabecera.EsCaea, cabecera.Estado.ToString(),
                totalNeto, totalIva, totalNc, devueltoEnEfectivo, impresion.Ok, impresion.Ok ? null : impresion.Error,
                devolucionesDto, esReversionCompleta);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ---------- Armado de líneas ----------

    private record LineaNc(string Descripcion, decimal Cantidad, decimal PrecioUnitario,
        decimal Alicuota, decimal Importe, int IdPresentacion, long? IdDetalleOrigen);

    private static List<LineaNc> ArmarLineas(EmitirNotaCreditoRequest req,
        List<DetalleComprobante> detallesOrigen, Dictionary<long, decimal> cantidadAnuladaPorLinea, decimal saldo)
    {
        switch (req.Tipo)
        {
            case TipoAnulacion.Total:
                // Todo lo que todavía quede disponible de cada línea — si una línea ya se acreditó
                // parcialmente en una NC anterior, la anulación total se lleva solo el resto.
                return detallesOrigen
                    .Select(d => (Detalle: d, Disponible: Disponible(d, cantidadAnuladaPorLinea)))
                    .Where(x => x.Disponible > 0m)
                    .Select(x => DesdeDetalleParcial(x.Detalle, x.Disponible))
                    .ToList();

            case TipoAnulacion.PorArticulos:
            {
                var seleccion = req.Lineas ?? new List<LineaSeleccionNc>();
                if (seleccion.Count == 0)
                    throw new DomainException("SIN_ARTICULOS", "Seleccioná al menos un artículo para anular.");

                var idsPedidos = seleccion.Select(s => s.IdDetalle).ToList();
                if (idsPedidos.Distinct().Count() != idsPedidos.Count)
                    throw new DomainException("ARTICULO_DUPLICADO",
                        "Un mismo artículo no puede seleccionarse dos veces en la misma nota de crédito.");

                var porId = detallesOrigen.ToDictionary(d => d.IdDetalleComprobante);
                var resultado = new List<LineaNc>();
                foreach (var s in seleccion)
                {
                    if (!porId.TryGetValue(s.IdDetalle, out var d))
                        throw new DomainException("ARTICULO_INEXISTENTE",
                            "Alguno de los artículos seleccionados no pertenece a ese comprobante.");

                    var disponible = Disponible(d, cantidadAnuladaPorLinea);
                    if (disponible <= 0m)
                        throw new DomainException("ARTICULO_YA_ANULADO",
                            $"El artículo \"{d.DescripcionTicket}\" ya fue anulado en su totalidad en una nota de crédito anterior.");
                    if (!NotaCreditoReglas.CantidadAcreditable(s.Cantidad, disponible))
                        throw new DomainException("CANTIDAD_INVALIDA",
                            $"La cantidad a anular de \"{d.DescripcionTicket}\" debe ser mayor a 0 y no puede superar {disponible} (lo disponible de esa línea).");

                    resultado.Add(DesdeDetalleParcial(d, s.Cantidad));
                }
                return resultado;
            }

            case TipoAnulacion.PorMonto:
            {
                var monto = req.Monto ?? 0m;
                if (!NotaCreditoReglas.ImporteAcreditable(monto, saldo))
                    throw new DomainException("MONTO_INVALIDO",
                        $"El monto debe ser mayor a cero y no puede superar el saldo anulable (${saldo:0.00}).");

                // Se prorratea sobre TODAS las líneas de la factura (incluidas las ya anuladas por
                // artículo): la proporción de alícuotas que se busca replicar es la del
                // comprobante original, no la de lo que queda sin acreditar.
                var tramos = NotaCreditoReglas.Prorratear(monto, detallesOrigen.Select(d =>
                    new LineaOriginal(d.IdDetalleComprobante, d.Importe, d.AlicuotaIva, false)));

                return tramos.Select(t => new LineaNc(
                    $"AJUSTE DE PRECIO {t.Alicuota * 100:0.#}%", 1m, t.Importe, t.Alicuota, t.Importe,
                    detallesOrigen.First(d => d.AlicuotaIva == t.Alicuota).IdPresentacion, null)).ToList();
            }

            default:
                throw new DomainException("TIPO_INVALIDO", "Tipo de anulación no soportado.");
        }
    }

    private static decimal Disponible(DetalleComprobante d, Dictionary<long, decimal> cantidadAnuladaPorLinea) =>
        d.Cantidad - cantidadAnuladaPorLinea.GetValueOrDefault(d.IdDetalleComprobante);

    /// <summary>Línea de NC por una cantidad parcial (o completa, si <paramref name="cantidad"/> es
    /// toda la de la línea original) — el importe y el precio unitario se prorratean, nunca se
    /// devuelven cantidades por encima de lo disponible (eso ya lo valida el caller).</summary>
    private static LineaNc DesdeDetalleParcial(DetalleComprobante d, decimal cantidad) =>
        new(d.DescripcionTicket, cantidad, d.PrecioUnit, d.AlicuotaIva,
            NotaCreditoReglas.ImporteProporcional(d.Importe, d.Cantidad, cantidad),
            d.IdPresentacion, d.IdDetalleComprobante);

    private static List<TributoFiscal>? BuildTributosNc(decimal percepcionIva21, decimal percepcionIva105, decimal percepcionIibb)
    {
        var tributos = new List<TributoFiscal>();
        if (percepcionIva21 > 0m)
            tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIva, "PERCEPCION IVA 21%", 0m, percepcionIva21));
        if (percepcionIva105 > 0m)
            tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIva, "PERCEPCION IVA 10,5%", 0m, percepcionIva105));
        if (percepcionIibb > 0m)
            tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIibb, "PERCEPCION IIBB", 0m, percepcionIibb));
        return tributos.Count > 0 ? tributos : null;
    }

    // ---------- Reversión completa (todos los medios originales, cupones incluidos) ----------

    /// <summary>
    /// Marca Anulado=true en cada MovimientoPago de la venta original (así queda registrado que
    /// ese cupón/pago ya no es válido) y devuelve cuánto hay que revertir por cada medio. El leg de
    /// Efectivo se neta contra el vuelto que se haya entregado en esa misma venta (identificado por
    /// texto en Concepto — no hay FK directa, ver FacturacionService): sin esto, el vuelto se
    /// devolvería dos veces (una al entregarlo en el momento, otra al revertir el pago completo).
    /// </summary>
    private async Task<List<(int IdMedioPago, decimal Monto)>> RevertirPagosOriginalesAsync(
        int idSucursal, int idComprobanteOrigen, string numeroCompletoOrigen,
        Dictionary<int, MedioPago> medios, CancellationToken ct)
    {
        var pagos = await (
            from mc in _db.MovimientosCaja.Where(m => m.IdSucursal == idSucursal && m.IdComprobante == idComprobanteOrigen)
            join mp in _db.MovimientosPagos on mc.IdMovPagos equals mp.IdMovPagos
            select new { mc.IdLote, mp }
        ).ToListAsync(ct);
        if (pagos.Count == 0) return new List<(int, decimal)>();

        var idLoteVenta = pagos[0].IdLote;
        var vuelto = await (
            from mc in _db.MovimientosCaja
                .Where(m => m.IdSucursal == idSucursal && m.IdLote == idLoteVenta
                         && m.TipoManual == TipoMovimientoManual.Vuelto
                         && m.Concepto == $"Vuelto (venta {numeroCompletoOrigen})")
            join mp in _db.MovimientosPagos on mc.IdMovPagos equals mp.IdMovPagos
            select -mp.Total
        ).SumAsync(ct);

        var devoluciones = new List<(int IdMedioPago, decimal Monto)>();
        foreach (var p in pagos)
        {
            var esEfectivo = medios.TryGetValue(p.mp.IdMedioPago, out var medio)
                && medio.TipoPago?.Fuente == FuentePago.Efectivo;
            var monto = p.mp.Total - (esEfectivo ? vuelto : 0m);

            p.mp.Anulado = true;
            p.mp.FechaAnulacion = DateTime.UtcNow;

            if (monto > 0.004m) devoluciones.Add((p.mp.IdMedioPago, monto));
        }
        return devoluciones;
    }

    // ---------- Consultas de apoyo ----------

    /// <summary>Total acreditado por notas de crédito, por comprobante de origen.</summary>
    private async Task<Dictionary<int, decimal>> AcreditadoPorComprobanteAsync(int idSucursal,
        IEnumerable<int> idsOrigen, CancellationToken ct)
    {
        var ids = idsOrigen.ToList();
        if (ids.Count == 0) return new Dictionary<int, decimal>();

        return await _db.CabecerasComprobantes.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdComprobanteOrigen != null
                        && ids.Contains(c.IdComprobanteOrigen.Value))
            .GroupBy(c => c.IdComprobanteOrigen!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Total) })
            .ToDictionaryAsync(x => x.Id, x => x.Total, ct);
    }

    /// <summary>Cuánta cantidad de cada línea de la factura ya se acreditó en notas de crédito
    /// previas (por artículo), sumando todas las NC que la referencian — permite anulaciones
    /// parciales sucesivas sobre la misma línea hasta agotar su cantidad original.</summary>
    private async Task<Dictionary<long, decimal>> CantidadAnuladaPorLineaAsync(int idSucursal, int idComprobante, CancellationToken ct)
    {
        return await (
            from d in _db.DetallesComprobantes.AsNoTracking()
            join c in _db.CabecerasComprobantes.AsNoTracking()
                on new { d.IdSucursal, d.IdComprobante } equals new { c.IdSucursal, c.IdComprobante }
            where c.IdSucursal == idSucursal && c.IdComprobanteOrigen == idComprobante
                  && d.IdDetalleOrigen != null
            group d by d.IdDetalleOrigen!.Value into g
            select new { Id = g.Key, Cantidad = g.Sum(x => x.Cantidad) }
        ).ToDictionaryAsync(x => x.Id, x => x.Cantidad, ct);
    }

    private async Task<Dictionary<int, string>> DescripcionesClientesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var lista = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (lista.Count == 0) return new Dictionary<int, string>();
        return await _db.Clientes.AsNoTracking().Where(c => lista.Contains(c.IdCliente))
            .ToDictionaryAsync(c => c.IdCliente, c => c.Descripcion, ct);
    }

    private async Task<ClienteFiscal?> ClienteFiscalAsync(int? idCliente, string letra, CancellationToken ct)
    {
        if (idCliente is not int id) return null;
        var c = await _db.Clientes.AsNoTracking().Include(x => x.CondicionIva)
            .Where(x => x.IdCliente == id)
            .Select(x => new { x.Descripcion, x.Cuit, x.Documento, x.Domicilio,
                               CondIva = x.CondicionIva!.Descripcion })
            .FirstOrDefaultAsync(ct);
        if (c is null) return null;

        return new ClienteFiscal(c.Descripcion, c.Cuit ?? c.Documento,
            string.IsNullOrWhiteSpace(c.Cuit) ? TipoDocumentoFiscal.Dni : TipoDocumentoFiscal.Cuit,
            ResponsabilidadFiscalDesde(c.CondIva, letra), c.Domicilio);
    }

    private record DatosInterfaseCliente(string? CodigoInt, string Descripcion, int IdCondIva, string? Cuit);

    /// <summary>Datos del cliente que necesita la interfase contable (ver InterfaseContableReglas) —
    /// distinto de <see cref="ClienteFiscalAsync"/>, que arma el objeto para la impresora fiscal.</summary>
    private async Task<DatosInterfaseCliente?> DatosInterfaseClienteAsync(int? idCliente, CancellationToken ct)
    {
        if (idCliente is not int id) return null;
        return await _db.Clientes.AsNoTracking().Where(c => c.IdCliente == id)
            .Select(c => new DatosInterfaseCliente(c.CodigoInt, c.Descripcion, c.IdCondIva, c.Cuit))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Corta a <paramref name="max"/> caracteres — los char(N) de la interfase contable
    /// externa (ivavtas.nombre, etc.) no aceptan más.</summary>
    private static string? Truncar(string? v, int max) => v is null || v.Length <= max ? v : v[..max];

    private static ResponsabilidadIvaFiscal ResponsabilidadFiscalDesde(string? condIva, string? letra)
    {
        var d = (condIva ?? "").ToUpperInvariant();
        if (d.Contains("INSCRIPTO") && !d.Contains("NO INSCRIPTO")) return ResponsabilidadIvaFiscal.ResponsableInscripto;
        if (d.Contains("MONOTRIBUT")) return d.Contains("SOCIAL")
            ? ResponsabilidadIvaFiscal.MonotributoSocial : ResponsabilidadIvaFiscal.Monotributo;
        if (d.Contains("EXENTO")) return ResponsabilidadIvaFiscal.Exento;
        if (d.Contains("NO RESPONSABLE")) return ResponsabilidadIvaFiscal.NoResponsable;
        if (d.Contains("CONSUMIDOR")) return ResponsabilidadIvaFiscal.ConsumidorFinal;
        return string.Equals(letra, "A", StringComparison.OrdinalIgnoreCase)
            ? ResponsabilidadIvaFiscal.ResponsableInscripto
            : ResponsabilidadIvaFiscal.ConsumidorFinal;
    }

    private async Task<bool> TieneAsientoCuentaCorrienteAsync(int idSucursal, int idComprobante, CancellationToken ct) =>
        await _db.CuentasCorrientes.AsNoTracking()
            .AnyAsync(c => c.IdSucursal == idSucursal && c.IdComprobante == idComprobante && c.Debe > 0, ct);

    /// <summary>El lote abierto hoy del cajero logueado, junto con el punto de venta de su caja.</summary>
    private record LoteConPuntoVenta(int IdLote, int IdPuntoVenta);

    private async Task<LoteConPuntoVenta?> ObtenerLoteAbiertoAsync(int idSucursal, int idCaja, CancellationToken ct)
    {
        var idUsuario = _currentUser.IdUsuario ?? 0;
        var idLote = await _db.LotesCaja.AsNoTracking()
            .Where(l => l.IdSucursal == idSucursal && l.IdCaja == idCaja && l.IdUsuarioApertura == idUsuario
                     && l.Estado == EstadoLote.Abierto && l.FechaApertura.Date == DateTime.UtcNow.Date)
            .Select(l => (int?)l.IdLote).FirstOrDefaultAsync(ct);
        if (idLote is null) return null;

        // El punto de venta no está en el lote: se resuelve por la caja, igual que en la venta.
        var idPv = await _db.Cajas.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdCaja == idCaja)
            .Select(c => c.IdPuntoVenta).FirstOrDefaultAsync(ct);
        return new LoteConPuntoVenta(idLote.Value, idPv);
    }

    private async Task AsegurarNumeradorAsync(int idSucursal, int idNumero, int idPuntoVenta, CancellationToken ct)
    {
        if (await _db.Numeros.AnyAsync(n => n.IdSucursal == idSucursal && n.IdNumero == idNumero, ct))
            return;
        try
        {
            _db.Numeros.Add(new Numero { IdSucursal = idSucursal, IdNumero = idNumero, IdPuntoVenta = idPuntoVenta, Valor = 0 });
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Carrera con otra emisión que ya lo creó. No es un error.
        }
    }

    private async Task<long> IncrementarNumeradorAsync(int idSucursal, int idNumero, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Numeros SET Valor = Valor + 1 OUTPUT INSERTED.Valor " +
                          "WHERE IdSucursal = @idSucursal AND IdNumero = @idNumero";
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        var pSuc = cmd.CreateParameter(); pSuc.ParameterName = "@idSucursal"; pSuc.Value = idSucursal;
        var pNum = cmd.CreateParameter(); pNum.ParameterName = "@idNumero"; pNum.Value = idNumero;
        cmd.Parameters.Add(pSuc); cmd.Parameters.Add(pNum);

        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
