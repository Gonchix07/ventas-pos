using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Application.Abstractions;
using Pos.Application.Abstractions.Fiscal;
using Pos.Application.Abstractions.Interfase;
using Pos.Application.Abstractions.Payments;
using Pos.Application.Common;
using Pos.Application.Facturacion;
using Pos.Application.Percepciones;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Saga de emisión de comprobantes: reservar número → cobrar → CAE (o CAEA en contingencia)
/// → persistir → imprimir. Ver docs/04-seguridad-transacciones.md y docs/05-fiscal-pagos.md.
/// Un fallo antes de persistir el comprobante revierte la transacción de BD (incluida la
/// numeración) y compensa (anula) los pagos ya aprobados; nunca queda un comprobante fiscal
/// "a medias".
/// </summary>
public class FacturacionService : IFacturacionService
{
    // Timeouts/reintentos para llamadas externas (fiscal/pagos) — ver ResilientCall. Hoy los
    // adaptadores son mocks instantáneos; esto es la red de seguridad para cuando se conecten los
    // servicios reales.
    private static readonly TimeSpan TimeoutFiscal = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TimeoutPago = TimeSpan.FromSeconds(10);
    private const int MaxIntentosPago = 3;
    private static readonly TimeSpan EsperaEntreIntentosPago = TimeSpan.FromMilliseconds(500);
    // Regla SRS "límite de reintentos por CAE inaccesible" (ReintentosCaeReglas) — agotados estos
    // intentos sin poder ni hablar con ARCA, se pasa a contingencia (CAEA precargado).
    private const int MaxIntentosCae = 3;
    private static readonly TimeSpan EsperaEntreIntentosCae = TimeSpan.FromMilliseconds(500);

    private readonly PosDbContext _db;
    private readonly IPaymentProviderFactory _pagos;
    private readonly IFiscalPrinter _impresora;
    private readonly IFiscalService _fiscal;
    private readonly ICaeaCargadoService _caeaCargado;
    private readonly ICurrentUser _currentUser;
    private readonly IPercepcionesCalculoService _percepciones;
    private readonly IInterfaseContableService _interfase;

    // IFiscalService (CAE/CAEA) vuelve a inyectarse acá: convive con el controlador fiscal, pero
    // cada punto de venta usa uno solo de los dos caminos (ver ModalidadPuntoVenta) — Fiscal sigue
    // yendo por IFiscalPrinter (Hasar), Electrónica ahora va por acá. Ver EmitirAsync.
    public FacturacionService(PosDbContext db, IPaymentProviderFactory pagos,
        IFiscalPrinter impresora, IFiscalService fiscal, ICaeaCargadoService caeaCargado, ICurrentUser currentUser,
        IPercepcionesCalculoService percepciones, IInterfaseContableService interfase)
    {
        _db = db;
        _pagos = pagos;
        _impresora = impresora;
        _fiscal = fiscal;
        _caeaCargado = caeaCargado;
        _currentUser = currentUser;
        _percepciones = percepciones;
        _interfase = interfase;
    }

    public async Task<EmitirComprobanteResponse> EmitirAsync(EmitirComprobanteRequest req, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(req.IdSucursal);

        if (!Enum.IsDefined(typeof(ModoFacturacion), req.Modo))
            throw new DomainException("MODO_NO_SOPORTADO", "Modo de facturación inválido.");
        var esPresupuesto = req.Modo == (int)ModoFacturacion.Presupuesto;

        if (req.Pagos.Count == 0)
            throw new DomainException("SIN_PAGOS", "Debe indicar al menos un medio de pago.");
        if (req.Pagos.Any(p => p.Monto <= 0))
            throw new DomainException("MONTO_INVALIDO",
                "Cada pago debe tener un monto mayor a cero (no se permiten montos negativos combinados para cuadrar el total).");
        // El presupuesto (comprobante X) se cierra siempre en efectivo, con un único pago — no
        // hay vuelto ni combinación de medios (se valida la fuente del medio más abajo, cuando
        // ya está cargado desde la BD).
        if (esPresupuesto && req.Pagos.Count != 1)
            throw new DomainException("PRESUPUESTO_SOLO_EFECTIVO", "El presupuesto se cobra siempre con un único pago en efectivo.");

        var operacion = await _db.Operaciones.Include(o => o.Detalles)
            .FirstOrDefaultAsync(o => o.IdSucursal == req.IdSucursal && o.IdOperacion == req.IdOperacion, ct)
            ?? throw new DomainException("OPERACION_INEXISTENTE", "La operación no existe.");

        // Idempotencia de negocio: una operación ya facturada no se vuelve a emitir.
        if (operacion.Estado == EstadoOperacion.Facturada)
            throw new DomainException("YA_FACTURADA", "La operación ya fue facturada.");
        if (operacion.Estado != EstadoOperacion.Finalizada)
            throw new DomainException("OPERACION_NO_FINALIZADA", "La operación debe estar finalizada antes de facturar.");
        if (operacion.Detalles.Count == 0)
            throw new DomainException("OPERACION_VACIA", "La operación no tiene artículos.");

        // Percepciones (IVA 21%/10,5% + IIBB según padrón del cliente): se calculan ACÁ, temprano,
        // para poder exigirle al cajero que las cubra en el pago (son parte de lo que hay que
        // cobrar) — el mismo cálculo que ya vio en el carrito (CajaService.MapOperacionAsync), pero
        // este es el AUTORITATIVO. El presupuesto no tiene valor fiscal: no percibe nada.
        var detallesList = operacion.Detalles.ToList();
        var percepcionResultado = esPresupuesto
            ? new PercepcionesResultado(0, 0, 0, 0, Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>())
            : await _percepciones.CalcularAsync(req.IdSucursal, operacion.IdCliente,
                detallesList.Select(d => new LineaParaPercepcion(d.IdPresentacion, d.Cantidad, d.Precio, d.Descuento, d.IdListaPrecio)).ToList(), ct);

        var sumaPagos = req.Pagos.Sum(p => p.Monto);
        var totalACobrar = operacion.Total + percepcionResultado.Total;

        // El vuelto solo puede salir de Efectivo: se resuelve acá qué parte de la suma es Efectivo
        // (una sola consulta liviana, antes del loop de cobro que trae cada MedioPago completo).
        var idsMediosPago = req.Pagos.Select(p => p.IdMedioPago).Distinct().ToList();
        var fuentesPorMedio = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
            .Where(m => idsMediosPago.Contains(m.IdMedioPago))
            .ToDictionaryAsync(m => m.IdMedioPago, m => m.TipoPago!.Fuente, ct);
        var sumaEfectivo = req.Pagos.Where(p => fuentesPorMedio.GetValueOrDefault(p.IdMedioPago) == FuentePago.Efectivo)
            .Sum(p => p.Monto);
        var sumaNoEfectivo = sumaPagos - sumaEfectivo;

        if (!ValidacionPagos.CubreElTotal(sumaPagos, totalACobrar))
            throw new DomainException("PAGOS_NO_CUBREN_TOTAL",
                $"La suma de los pagos (${sumaPagos:0.00}) no coincide con el total a cobrar, percepciones incluidas (${totalACobrar:0.00}).");
        // El sobrante (vuelto) se admite, pero nunca puede venir de un medio no-Efectivo (no se
        // puede "dar vuelto" en una tarjeta o transferencia).
        if (!ValidacionPagos.NoEfectivoNoSuperaElTotal(sumaNoEfectivo, totalACobrar))
            throw new DomainException("VUELTO_SOLO_EFECTIVO",
                "El sobrante solo puede darse como vuelto en efectivo: los medios no efectivos no pueden superar lo que les corresponde.");

        // El presupuesto siempre sale por el punto de venta de tipo Presupuesto de la sucursal —
        // se resuelve del lado del servidor (no confía en lo que mande la caja en req.IdPuntoVenta,
        // que sigue siendo el de su lote/caja habitual para el resto del pedido).
        var puntoVenta = esPresupuesto
            ? await _db.PuntosVenta.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdSucursal == req.IdSucursal
                    && p.IdTipoPuntoVenta == (int)ModalidadPuntoVenta.Presupuesto, ct)
                ?? throw new DomainException("PUNTO_VENTA_PRESUPUESTO_INEXISTENTE",
                    "La sucursal no tiene configurado un punto de venta de tipo Presupuesto.")
            : await _db.PuntosVenta.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdSucursal == req.IdSucursal && p.IdPuntoVenta == req.IdPuntoVenta, ct)
                ?? throw new DomainException("PUNTO_VENTA_INEXISTENTE", "El punto de venta no existe en la sucursal.");

        // No se factura Fiscal/Electrónica contra el punto de venta de tipo Presupuesto: mezclaría
        // comprobantes con y sin valor fiscal en la misma numeración.
        if (!esPresupuesto && puntoVenta.IdTipoPuntoVenta == (int)ModalidadPuntoVenta.Presupuesto)
            throw new DomainException("PUNTO_VENTA_ES_PRESUPUESTO",
                "Ese punto de venta es de tipo Presupuesto: no puede emitir facturas fiscales/electrónicas.");

        // El camino único de cada caja lo define el TIPO del punto de venta asignado (ABM de
        // Asignación de Cajas / TiposPuntoVentaFijos), nunca el request: Presupuesto ya se resolvió
        // arriba ignorando req.IdPuntoVenta; entre Fiscal (controlador Hasar) y Electrónica
        // (CAE/CAEA vía ARCA) decide la asignación, no el cliente que factura.
        var esElectronica = !esPresupuesto && puntoVenta.IdTipoPuntoVenta == (int)ModalidadPuntoVenta.Electronica;

        var sucursal = await _db.Sucursales.AsNoTracking().Include(s => s.Empresa)
            .FirstOrDefaultAsync(s => s.IdSucursal == req.IdSucursal, ct)
            ?? throw new DomainException("SUCURSAL_INEXISTENTE", "La sucursal no existe.");

        // La letra la decide la condición del CLIENTE frente al IVA, no el frontend: Responsable
        // Inscripto/Monotributista → A (IVA discriminado), el resto → B. Lo que venga en req.Letra
        // se ignora: es un dato fiscal, no una opción de la caja.
        var datosCliente = operacion.IdCliente is int idc
            ? await _db.Clientes.AsNoTracking().Include(c => c.CondicionIva)
                .Where(c => c.IdCliente == idc)
                .Select(c => new { c.Cuit, c.Documento, c.Domicilio, c.Descripcion, c.PermitePresupuesto,
                    c.CodigoInt, c.IdCondIva,
                    LetraCondIva = c.CondicionIva!.Letra, CondIva = c.CondicionIva!.Descripcion })
                .FirstOrDefaultAsync(ct)
            : null;

        if (esPresupuesto)
        {
            if (datosCliente is null)
                throw new DomainException("CLIENTE_REQUERIDO", "El presupuesto requiere un cliente identificado.");
            if (!datosCliente.PermitePresupuesto)
                throw new DomainException("CLIENTE_NO_ADMITE_PRESUPUESTO",
                    $"{datosCliente.Descripcion} no está habilitado para comprar con presupuesto.");

            // Además del cliente, la CAJA tiene que admitirlo (Caja.AdmitePresupuesto) — un admin
            // puede desactivarlo en cajas puntuales aunque el cliente sí lo tenga permitido.
            var admiteCaja = await _db.Cajas.AsNoTracking()
                .Where(c => c.IdSucursal == req.IdSucursal && c.IdCaja == operacion.IdCaja)
                .Select(c => c.AdmitePresupuesto).FirstOrDefaultAsync(ct);
            if (!admiteCaja)
                throw new DomainException("CAJA_NO_ADMITE_PRESUPUESTO",
                    "Esta caja no está habilitada para vender con Presupuesto.");
        }

        // El presupuesto (letra "X") no tiene valor fiscal: no discrimina IVA ni exige CUIT/domicilio
        // del cliente, sea cual sea su condición frente al IVA — el precio final va como en una
        // Factura B a consumidor final.
        var letra = esPresupuesto ? "X" : LetraComprobante.Resolver(datosCliente?.LetraCondIva);
        if (!esPresupuesto && LetraComprobante.ExigeIdentificacion(letra))
        {
            if (string.IsNullOrWhiteSpace(datosCliente?.Cuit))
                throw new DomainException("CUIT_REQUERIDO",
                    $"La factura A exige el CUIT del cliente. Cargalo en el ABM de clientes ({datosCliente?.Descripcion}).");
            if (string.IsNullOrWhiteSpace(datosCliente?.Domicilio))
                throw new DomainException("DOMICILIO_REQUERIDO",
                    $"La factura A exige el domicilio del cliente. Cargalo en el ABM de clientes ({datosCliente?.Descripcion}).");
        }

        // Norma AFIP: una venta a Consumidor Final (letra B, sin discriminar IVA) por encima de un
        // monto no se puede facturar de forma anónima — hay que identificar al comprador con su
        // CUIT. No aplica a la A (ya exige CUIT siempre, sin importar el monto, arriba) ni al
        // Presupuesto (sin valor fiscal).
        if (!esPresupuesto && letra != LetraComprobante.A && string.IsNullOrWhiteSpace(datosCliente?.Cuit))
        {
            var limiteConsumidorFinal = await ObtenerConfigDecimalAsync("LimiteConsumidorFinal", 417400m, ct);
            if (totalACobrar > limiteConsumidorFinal)
                throw new DomainException("LIMITE_CONSUMIDOR_FINAL",
                    $"Las ventas a Consumidor Final por más de ${limiteConsumidorFinal:N2} no se pueden facturar de forma anónima: " +
                    "identificá al cliente con su CUIT (buscalo en caja o cargalo en el ABM de clientes).");
        }

        // Signo +1: la letra sola no alcanza, hay una nota de crédito por cada letra.
        var tipoComprobante = await _db.TiposComprobante.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Letra == letra && t.Signo == 1, ct)
            ?? throw new DomainException("TIPO_COMPROBANTE_INEXISTENTE", $"No hay un tipo de comprobante para la letra {letra}.");

        var clienteCuit = datosCliente?.Cuit;

        // Descripción de ticket por línea (presentación→artículo). La alícuota, el Neto y el Iva YA
        // no salen de acá: salen de percepcionResultado.AlicuotaPorLinea/NetoPorLinea/IvaPorLinea,
        // calculados arriba — ahí es donde se resta el Impuesto Interno de la base antes de
        // discriminar IVA, así que no se repite (ni se puede desincronizar) esa cuenta acá.
        var idsPres = detallesList.Select(d => d.IdPresentacion).Distinct().ToList();
        var infoPresentaciones = await (
            from pr in _db.Presentaciones.AsNoTracking().Where(p => idsPres.Contains(p.IdPresentacion))
            join a in _db.Articulos.AsNoTracking() on pr.IdArticulo equals a.IdArticulo
            select new { pr.IdPresentacion, DescripcionTicket = pr.DescripcionTicket ?? a.Descripcion, a.CodigoInterno }
        ).ToDictionaryAsync(x => x.IdPresentacion, ct);

        decimal totalNeto = 0, totalIva = 0;
        var detallesCalculados = new List<(DetalleOperacion Origen, string DescTicket, decimal Alicuota, decimal Importe, decimal Neto, decimal Iva)>();
        for (var idx = 0; idx < detallesList.Count; idx++)
        {
            var det = detallesList[idx];
            if (!infoPresentaciones.TryGetValue(det.IdPresentacion, out var info))
                throw new DomainException("PRESENTACION_INEXISTENTE", $"La presentación {det.IdPresentacion} ya no existe.");

            var importe = det.Precio * det.Cantidad - det.Descuento;
            decimal alicuotaEfectiva, neto, iva;
            if (esPresupuesto)
            {
                // El presupuesto no discrimina impuestos (alícuota 0 → Neto = precio final, Iva = 0),
                // sin importar la alícuota real del artículo ni la condición del cliente frente al IVA.
                alicuotaEfectiva = 0m;
                (neto, iva) = DesglioIva.Calcular(importe, 0m);
            }
            else
            {
                alicuotaEfectiva = percepcionResultado.AlicuotaPorLinea[idx];
                neto = percepcionResultado.NetoPorLinea[idx];
                iva = percepcionResultado.IvaPorLinea[idx];
            }
            totalNeto += neto; totalIva += iva;
            detallesCalculados.Add((det, info.DescripcionTicket, alicuotaEfectiva, importe, neto, iva));
        }

        // El Impuesto Interno se restó de la base de cada línea antes de discriminar IVA (ver
        // PercepcionesCalculoService), así que Neto+Iva por línea queda short exactamente en ese
        // monto respecto del Importe realmente cobrado (precio final, que ya lo incluye). Sin
        // sumarlo de vuelta acá, el Total del comprobante queda por debajo de la suma de sus
        // propias líneas — bug real: una Nota de Crédito sobre artículos con Impuesto Interno
        // (bebidas alcohólicas, etc.) mostraba "supera el saldo anulable" al seleccionar TODAS las
        // líneas de la factura, porque el saldo (basado en este Total) era menor que la suma de
        // los Importe de esas mismas líneas.
        var impuestoInternoTotal = percepcionResultado.ImpuestoInternoPorLinea?.Sum() ?? 0m;

        // Ítems reales tal como van a la impresora fiscal (antes de agregarle la línea sintética
        // "Descuento x MP" más abajo — ver por qué esa línea NUNCA se manda a la fiscal, en el
        // comentario de RepartirDescuentoMp).
        var itemsFiscalesBase = detallesCalculados.Select(d => new ItemFiscal(
            d.DescTicket, d.Origen.Cantidad, d.Origen.Precio, d.Alicuota,
            d.Origen.Descuento, d.Origen.IdPresentacion.ToString())).ToList();

        // Ofertas por medio de pago vigentes: se evalúan también en el presupuesto — el pago en
        // Efectivo del presupuesto es un pago como cualquier otro, y el presupuesto tiene que
        // mostrarle al cliente lo que realmente pagaría si lo confirma (antes se excluía acá y el
        // descuento nunca se calculaba para Presupuesto). Vigencia inclusive, mismo criterio que
        // CabeceraOferta (ver PricingService.AplicarOfertasAsync).
        var hoyOfertasMp = DateTime.UtcNow.Date;
        var ofertasMedioPago = await _db.OfertasMedioPago.AsNoTracking()
            .Where(o => o.IdSucursal == req.IdSucursal && o.Activo
                && o.FechaInicio <= hoyOfertasMp && o.FechaFin >= hoyOfertasMp)
            .Select(o => new OfertaMedioPagoDef(o.IdMedioPago, o.IdPlanCuota, o.Porcentaje, o.TopeMaximo))
            .ToListAsync(ct);

        // Solo para armar la referencia fiscal de los pagos con Cheque (ver más abajo) — el nombre
        // del banco no viaja en PagoInput, solo el IdBanco.
        var bancos = await _db.Bancos.AsNoTracking().ToDictionaryAsync(b => b.IdBanco, b => b.Descripcion, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var pagosAprobados = new List<(IPaymentProvider Provider, string IdTransaccion)>();
        try
        {
            // 1) Procesar pagos (fuera de la transacción de BD: son llamadas externas). La
            // numeración (paso 3, más abajo) se resuelve DESPUÉS de cobrar: en Fiscal/Electrónica la
            // asigna el controlador fiscal al abrir el documento, no un numerador propio.
            var resultadosPago = new List<PagoResultadoDto>();
            // Medios que resultaron ser cuenta corriente — el asiento en CuentaCorriente recién se
            // puede insertar en el paso 4 (necesita IdComprobante, que todavía no existe acá).
            var pagosCuentaCorriente = new List<PagoResultadoDto>();
            // Pagos con tarjeta, para la interfase contable (cupones) — se completan en el paso 4
            // (necesita IdMovPagos/cuotas, que recién se resuelven ahí) y se mandan recién después
            // del commit, junto con el resto de la interfase.
            var filasCupon = new List<CuponInterfase>();
            // Detalle de pagos tal como debe salir impreso en el comprobante fiscal. Se arma acá
            // (no al final) porque es el único punto donde ya se resolvió el medio de pago contra
            // la BD, y la impresora exige discriminar cada medio por separado.
            var pagosFiscales = new List<PagoFiscal>();
            // Total del descuento por medio de pago de TODOS los pagos, para la línea sintética
            // "Descuento x MP" que se agrega al comprobante después del loop (más abajo).
            var descuentoMpTotal = 0m;
            // Sobrante devuelto en efectivo, acumulado pago a pago (ver más abajo por qué no se
            // puede calcular de una sola vez con sumaPagos-totalACobrar cuando hay descuento por
            // medio de pago de por medio).
            var vuelto = 0m;
            // Cuánto de la VENTA (no de lo entregado) queda todavía sin asignar a un pago. Un pago
            // en Efectivo por más de lo que falta cubrir no "compra" más venta: el excedente es
            // plata que se devuelve, y el descuento por medio de pago se calcula sobre lo que sí
            // cubre la venta, nunca sobre lo entregado de más (ver bug real: $15.000 entregados
            // para cubrir $14.899,90 con 10% Efectivo — el descuento es 10% de $14.899,90, no de
            // $15.000, porque esos $100,10 de más nunca fueron parte de la venta).
            var restanteSaldo = totalACobrar;
            foreach (var (pago, i) in req.Pagos.Select((p, i) => (p, i)))
            {
                var medio = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
                    .FirstOrDefaultAsync(m => m.IdMedioPago == pago.IdMedioPago, ct)
                    ?? throw new DomainException("MEDIO_PAGO_INEXISTENTE", $"El medio de pago {pago.IdMedioPago} no existe.");

                if (esPresupuesto && medio.TipoPago!.Fuente != FuentePago.Efectivo)
                    throw new DomainException("PRESUPUESTO_SOLO_EFECTIVO", "El presupuesto se cobra siempre en efectivo.");

                ValidarCuponYLote(medio, pago);
                ValidarCheque(medio, pago);

                var esEfectivo = medio.TipoPago!.Fuente == FuentePago.Efectivo;
                // Lo que este pago realmente "compra" de la venta: en Efectivo, tope al saldo que
                // todavía falta cubrir (el resto es vuelto); en el resto de los medios, el nominal
                // completo (ya se validó arriba que ninguno no-Efectivo supera el total en conjunto).
                var cubierto = esEfectivo ? Math.Min(pago.Monto, Math.Max(restanteSaldo, 0m)) : pago.Monto;
                var excedente = pago.Monto - cubierto;
                restanteSaldo -= cubierto;

                // Descuento por medio de pago (y cuotas, si es tarjeta): se calcula sobre "cubierto"
                // (lo que de verdad se le atribuye a la venta), no sobre pago.Monto — si no, entregar
                // de más en Efectivo inflaría el descuento sobre plata que iba a volver como vuelto.
                // montoACobrar es lo que se manda al proveedor de pago/impresora fiscal y lo que
                // queda en MovimientoPago.Total — la plata real que se mueve por ese medio.
                var ofertaMp = OfertaMedioPagoReglas.Resolver(ofertasMedioPago, medio.IdMedioPago, pago.IdPlan);
                var descuentoMp = ofertaMp is null ? 0m
                    : OfertaMedioPagoReglas.CalcularDescuento(cubierto, ofertaMp.Porcentaje, ofertaMp.TopeMaximo);
                var montoACobrar = cubierto - descuentoMp;
                descuentoMpTotal += descuentoMp;
                // El vuelto es el excedente entregado MÁS el descuento: ese descuento también achica
                // lo que hacía falta cobrar, así que también se devuelve como parte del vuelto.
                if (esEfectivo) vuelto += excedente + descuentoMp;

                var referenciaPago = medio.TipoPago!.Fuente == FuentePago.Cheque
                    ? $"CHEQUE {pago.NumeroCheque} BANCO {bancos.GetValueOrDefault(pago.IdBanco ?? 0, "")}".TrimEnd()
                    : (string.IsNullOrWhiteSpace(pago.NumeroCupon) ? null
                        : $"CUPON {pago.NumeroCupon}" + (string.IsNullOrWhiteSpace(pago.NumeroLote) ? "" : $" LOTE {pago.NumeroLote}"));
                pagosFiscales.Add(new PagoFiscal(medio.Descripcion, montoACobrar,
                    medio.TipoPago!.Fuente, referenciaPago, 1));
                await ValidarClusterDelMedioAsync(medio, operacion.IdCliente, ct);

                if (medio.TipoPago!.Fuente == FuentePago.CuentaCorriente)
                {
                    // Cuenta corriente no es un medio de pago externo (no hay proveedor que
                    // llamar) — es un control de crédito interno contra CuentaCorriente/
                    // ClienteEnCuenta. Antes esto se aprobaba ciegamente vía el proveedor mock,
                    // sin chequear límite ni registrar nada en la cuenta corriente del cliente.
                    var dto = await AprobarCuentaCorrienteAsync(req.IdSucursal, operacion.IdCliente, pago.IdMedioPago, montoACobrar, ct);
                    resultadosPago.Add(dto);
                    pagosCuentaCorriente.Add(dto);
                    continue;
                }

                // El adaptador lo decide el CANAL configurado en el tipo de pago (Manual o iCARD),
                // no la fuente: lo que cambia es por dónde se efectúa el cobro, no qué medio es.
                var provider = _pagos.Resolve(medio.TipoPago.Canal);
                var solicitud = new SolicitudPago(medio.TipoPago.Fuente, medio.TipoPago.Canal,
                    medio.IdMedioPago, montoACobrar, $"{req.IdSucursal}-{req.IdOperacion}-{i}");

                // Reintentable: usa la misma IdempotencyKey en cada intento, así que un timeout
                // que en realidad sí procesó el cobro del lado del proveedor no debería duplicarlo
                // (asumiendo que el proveedor real deduplica por esa clave, como es estándar en
                // pasarelas de pago).
                ResultadoPago resultado;
                try
                {
                    resultado = await ResilientCall.ConTimeoutYReintentosAsync(
                        ct2 => provider.CobrarAsync(solicitud, ct2), TimeoutPago, MaxIntentosPago, EsperaEntreIntentosPago, ct);
                }
                catch (Exception ex)
                {
                    resultadosPago.Add(new PagoResultadoDto(pago.IdMedioPago, montoACobrar, false, null, ex.Message));
                    throw new DomainException("PAGO_INDISPONIBLE",
                        $"No se pudo procesar el pago (medio {medio.Descripcion}): {ex.Message}");
                }

                if (!resultado.Aprobado)
                {
                    resultadosPago.Add(new PagoResultadoDto(pago.IdMedioPago, montoACobrar, false, null, resultado.Error));
                    throw new DomainException("PAGO_RECHAZADO", resultado.Error ?? $"Pago rechazado (medio {medio.Descripcion}).");
                }
                pagosAprobados.Add((provider, resultado.IdTransaccion!));
                resultadosPago.Add(new PagoResultadoDto(pago.IdMedioPago, montoACobrar, true, resultado.IdTransaccion, null));
            }

            // Descuento por medio de pago: se agrega como una línea más del comprobante ("Descuento
            // x MP"), en negativo y Exenta de IVA (alícuota 0), igual que cualquier otra línea — así
            // el campo agregado "descuento" del ticket (que suma Detalle.Descuento de TODAS las
            // líneas, ver ObtenerParaImprimirAsync) la incluye solo. No se agrega a DetalleOperacion
            // (el carrito no cambia): esta línea nace recién acá, al facturar.
            if (descuentoMpTotal > 0)
            {
                var (netoDesc, ivaDesc) = DesglioIva.Calcular(-descuentoMpTotal, 0m);
                var origenDescuentoMp = new DetalleOperacion { IdPresentacion = 0, Cantidad = 1, Precio = 0m, Descuento = descuentoMpTotal };
                detallesCalculados.Add((origenDescuentoMp, "Descuento x MP", 0m, -descuentoMpTotal, netoDesc, ivaDesc));
                totalNeto += netoDesc; totalIva += ivaDesc;
            }

            // 3) Numeración + autorización. Presupuesto usa su propio numerador interno (sin valor
            // fiscal). Fiscal usa el modelo "controlador fiscal": el ÚNICO numerador válido es el
            // del controlador Hasar — lo asigna él mismo al abrir el documento (antes de imprimir
            // ningún ítem), así que hay que imprimir PRIMERO y recién usar ESE número como el nuestro
            // (reservar un número propio de antemano desincroniza para siempre el numerador interno
            // del que realmente queda impreso en el papel/memoria fiscal). Electrónica usa numerador
            // propio (como Presupuesto) porque el número SÍ lo define el emisor antes de pedir el
            // CAE — ARCA autoriza el número que uno le manda, no asigna uno nuevo.
            long numero;
            ComprobanteFiscal? comprobanteFiscal = null;
            ResultadoImpresion impresion;
            string? cae = null;
            DateTime? caeVencimiento = null;
            var esCaea = false;

            // Percepciones de IVA/IIBB como tributos separados — las necesitan por igual el
            // controlador fiscal (Hasar, ver su manual: van después de los ítems/descuentos y
            // antes de los pagos) y ARCA (WSFEv1 exige que ImpTotal cierre contra
            // ImpTotConc+ImpNeto+ImpOpEx+ImpTrib+ImpIVA — sin esto acá, una Factura A a un
            // Responsable Inscripto con percepción de IVA quedaba con el total mal cerrado y ARCA
            // la rechazaba con el error 10048. Bug real encontrado facturando Electrónica de verdad
            // en homologación (2026-08-24): el bloque de tributos vivía solo dentro de la rama
            // Fiscal, nunca se armaba para Electrónica).
            var tributos = new List<TributoFiscal>();
            if (percepcionResultado.PercepcionIva21 > 0)
                tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIva, "PERCEPCION IVA 21%",
                    percepcionResultado.BaseImponibleIva21, percepcionResultado.PercepcionIva21));
            if (percepcionResultado.PercepcionIva105 > 0)
                tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIva, "PERCEPCION IVA 10,5%",
                    percepcionResultado.BaseImponibleIva105, percepcionResultado.PercepcionIva105));
            if (percepcionResultado.PercepcionIibb > 0)
                tributos.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIibb, "PERCEPCION IIBB",
                    percepcionResultado.BaseImponibleIibb, percepcionResultado.PercepcionIibb));

            if (esPresupuesto)
            {
                await AsegurarNumeradorAsync(req.IdSucursal, puntoVenta.IdPuntoVenta, ct);
                numero = await IncrementarNumeradorAsync(req.IdSucursal, puntoVenta.IdPuntoVenta, ct);
                impresion = new ResultadoImpresion(true, null, null); // no hay impresora fiscal que imprima esto
            }
            else if (esElectronica)
            {
                // Serie propia por punto de venta + TIPO de comprobante: Factura A y Factura B (o
                // cualquier otra letra) nunca comparten numeración ante ARCA.
                var idNumeroElectronica = NumeradorIds.Factura(puntoVenta.IdPuntoVenta, int.Parse(tipoComprobante.CodigoArca!));
                await AsegurarNumeradorAsync(req.IdSucursal, idNumeroElectronica, ct);
                numero = await IncrementarNumeradorAsync(req.IdSucursal, idNumeroElectronica, ct);

                comprobanteFiscal = new ComprobanteFiscal(sucursal.IdEmpresa, puntoVenta.NumeroPuntoVenta,
                    tipoComprobante.Descripcion, letra, numero, clienteCuit, totalNeto, totalIva,
                    totalNeto + totalIva + impuestoInternoTotal + percepcionResultado.Total, DateTime.UtcNow,
                    req.IdSucursal, operacion.IdCaja,
                    datosCliente is null ? null : ConstruirClienteFiscal(datosCliente.Descripcion,
                        datosCliente.Cuit, datosCliente.Documento, datosCliente.Domicilio,
                        datosCliente.CondIva, datosCliente.LetraCondIva),
                    // El puerto de CAE necesita el detalle por línea (alícuota por ítem) para armar
                    // el array de IVA que exige WSFEv1 — mismo criterio que la impresora fiscal: el
                    // descuento por medio de pago va prorrateado dentro de los ítems reales.
                    RepartirDescuentoMp(itemsFiscalesBase, descuentoMpTotal),
                    null, tributos, tipoComprobante.CodigoArca);

                // Reintentos por CAE inaccesible (ReintentosCaeReglas): solo cubren fallas de
                // CONECTIVIDAD (timeout, ARCA caído) — un rechazo de negocio de ARCA (Ok=false con
                // Error) no se reintenta, porque reintentar el mismo dato rechazado no cambia nada,
                // y tampoco amerita contingencia (el problema no es que ARCA esté inaccesible).
                ResultadoCae? resultadoCae = null;
                Exception? fallaConectividad = null;
                try
                {
                    resultadoCae = await ResilientCall.ConTimeoutYReintentosAsync(
                        ct2 => _fiscal.SolicitarCaeAsync(comprobanteFiscal!, ct2),
                        TimeoutFiscal, MaxIntentosCae, EsperaEntreIntentosCae, ct);
                }
                catch (Exception ex)
                {
                    fallaConectividad = ex;
                }

                if (resultadoCae is { Ok: true })
                {
                    cae = resultadoCae.Cae; caeVencimiento = resultadoCae.Vencimiento; esCaea = false;
                }
                else if (resultadoCae is { Ok: false })
                {
                    throw new DomainException("FISCAL_INDISPONIBLE",
                        resultadoCae.Error ?? "ARCA no autorizó el comprobante.");
                }
                else
                {
                    // Se agotaron los reintentos sin poder ni hablar con ARCA: contingencia con el
                    // CAEA precargado a mano (ver ICaeaCargadoService) — si no hay ninguno vigente
                    // para hoy, no hay forma de emitir y se aborta la saga completa (con
                    // compensación de los pagos ya aprobados, ver el catch más abajo).
                    var caeaVigente = await _caeaCargado.BuscarVigenteAsync(sucursal.IdEmpresa, DateTime.UtcNow, ct);
                    if (caeaVigente is null)
                        throw new DomainException("FISCAL_INDISPONIBLE",
                            $"No se pudo conectar con ARCA ({fallaConectividad?.Message}) y no hay un CAEA cargado vigente para hoy — no se puede emitir el comprobante.");
                    cae = caeaVigente.Valor; caeVencimiento = caeaVigente.VigenciaHasta; esCaea = true;
                }
                // La impresión en sí es cosa de la comandera local del navegador (como
                // Presupuesto), no hay un puerto de impresora acá.
                impresion = new ResultadoImpresion(true, null, null);
            }
            else
            {
                // El "Numero" que se manda acá es un placeholder sin uso real: AbrirDocumento no
                // recibe número (lo asigna el equipo), y ya no se pide CAE (única otra cosa que lo
                // usaba). Se sobreescribe más abajo con el número real que devuelve la impresora.
                comprobanteFiscal = new ComprobanteFiscal(sucursal.IdEmpresa, puntoVenta.NumeroPuntoVenta,
                    tipoComprobante.Descripcion, letra, 0, clienteCuit, totalNeto, totalIva,
                    totalNeto + totalIva + impuestoInternoTotal + percepcionResultado.Total, DateTime.UtcNow,
                    req.IdSucursal, operacion.IdCaja,
                    datosCliente is null ? null : ConstruirClienteFiscal(datosCliente.Descripcion,
                        datosCliente.Cuit, datosCliente.Documento, datosCliente.Domicilio,
                        datosCliente.CondIva, datosCliente.LetraCondIva),
                    // Se manda el precio unitario CON IVA y el descuento de la línea por separado: es
                    // como los tiene el POS y como los espera el controlador fiscal (modo precio total).
                    // El descuento por medio de pago va prorrateado dentro de estos ítems reales, NUNCA
                    // como línea aparte (ver RepartirDescuentoMp).
                    RepartirDescuentoMp(itemsFiscalesBase, descuentoMpTotal),
                    pagosFiscales, tributos);

                try
                {
                    impresion = await ResilientCall.ConTimeoutAsync(
                        ct2 => _impresora.ImprimirFiscalAsync(comprobanteFiscal, ct2), TimeoutFiscal, ct);
                }
                catch (Exception ex)
                {
                    impresion = new ResultadoImpresion(false, null, ex.Message);
                }
                // A diferencia de antes, esto YA NO es best-effort: sin el número real que asigna el
                // controlador no hay nada válido para persistir. Un fallo acá aborta toda la venta
                // (con compensación de pagos ya aprobados, ver el catch de la saga) — el pago se
                // vuelve a intentar cuando el controlador esté disponible, no queda "Persistido" sin
                // papel como antes.
                if (!impresion.Ok)
                    throw new DomainException("FISCAL_INDISPONIBLE",
                        impresion.Error ?? "No se pudo emitir el comprobante en el controlador fiscal.");
                if (!long.TryParse(impresion.NumeroFiscal, out numero))
                    throw new DomainException("FISCAL_INDISPONIBLE",
                        $"El controlador fiscal no devolvió un número de comprobante válido ('{impresion.NumeroFiscal}').");
            }

            // 4) Persistir comprobante + detalle + movimientos (misma transacción que el número).
            // Lock por sucursal: el numerador fiscal (Numeros, arriba) ya serializa por punto de
            // venta, pero IdComprobante/IdMovCaja son Max()+1 sin IDENTITY compartidos por TODA la
            // sucursal — sin este lock, dos emisiones concurrentes en puntos de venta DISTINTOS de
            // la misma sucursal podrían calcular el mismo próximo id (recién después de cobrar y
            // obtener el CAE, lo peor posible). Se toma acá, no al principio de la saga, para no
            // serializar las llamadas externas a pagos/fiscal (lentas) entre sí.
            await RecursoLockHelper.AdquirirAsync(_db, $"Comprobante:{req.IdSucursal}", ct);

            var idComprobante = (await _db.CabecerasComprobantes.Where(c => c.IdSucursal == req.IdSucursal)
                .Select(c => c.IdComprobante).MaxAsync(x => (int?)x, ct) ?? 0) + 1;

            var cabecera = new CabeceraComprobante
            {
                IdSucursal = req.IdSucursal, IdComprobante = idComprobante,
                IdTipoComprobante = tipoComprobante.IdTipoComprobante, IdCliente = operacion.IdCliente,
                IdPuntoVenta = puntoVenta.IdPuntoVenta, IdOperacion = req.IdOperacion, Letra = letra,
                NumeroCompleto = NumeroComprobanteFormatter.Formatear(puntoVenta.NumeroPuntoVenta, numero),
                Fecha = DateTime.UtcNow, Neto = totalNeto, Iva = totalIva,
                PercepcionIva21 = percepcionResultado.PercepcionIva21,
                PercepcionIva105 = percepcionResultado.PercepcionIva105,
                PercepcionIibb = percepcionResultado.PercepcionIibb,
                AlicuotaIibb = percepcionResultado.AlicuotaIibb,
                Percepciones = percepcionResultado.Total,
                Total = totalNeto + totalIva + impuestoInternoTotal + percepcionResultado.Total,
                Cae = cae, CaeVencimiento = caeVencimiento, EsCaea = esCaea,
                // Presupuesto: siempre Persistido (no hay impresora fiscal que lo pase a Impreso).
                // Electrónica: si llegamos hasta acá ya tiene CAE/CAEA autorizado por ARCA — queda
                // CaeOk, la impresión en la comandera local es cosa del navegador, no de acá.
                // Fiscal: si llegamos hasta acá ya se imprimió con éxito en el controlador (si
                // hubiera fallado, se arrojó FISCAL_INDISPONIBLE más arriba y nunca se persiste nada).
                Estado = esPresupuesto ? EstadoComprobante.Persistido
                    : esElectronica ? EstadoComprobante.CaeOk
                    : EstadoComprobante.Impreso
            };
            // Se agregan vía la navegación (no directo al DbSet) para que EF resuelva el orden
            // de inserción cabecera→detalle correctamente (claves compuestas asignadas a mano).
            foreach (var (origen, descTicket, alicuota, importe, neto, iva) in detallesCalculados)
            {
                cabecera.Detalles.Add(new DetalleComprobante
                {
                    IdSucursal = req.IdSucursal, IdComprobante = idComprobante,
                    IdPresentacion = origen.IdPresentacion, DescripcionTicket = descTicket,
                    Cantidad = origen.Cantidad, PrecioUnit = origen.Precio, Descuento = origen.Descuento,
                    AlicuotaIva = alicuota, Importe = importe
                });
            }
            _db.CabecerasComprobantes.Add(cabecera);

            // Asiento de cuenta corriente (Debe): recién acá existe IdComprobante, que es parte de
            // su clave. Misma transacción que el resto — si algo falla después, se revierte junto
            // con todo lo demás (nunca queda un cargo de cuenta corriente sin su comprobante). Se
            // suman en un único asiento (la clave es por comprobante, no por línea de pago).
            if (pagosCuentaCorriente.Count > 0)
            {
                _db.CuentasCorrientes.Add(new CuentaCorriente
                {
                    IdSucursal = req.IdSucursal, IdCliente = operacion.IdCliente!.Value,
                    IdComprobante = idComprobante, Debe = pagosCuentaCorriente.Sum(p => p.Monto), Haber = 0
                });
            }

            // Para la interfase contable (cupones): Fuente + código de tarjeta por medio, resuelto
            // una sola vez para todos los pagos en vez de repetir la consulta pago por pago.
            var idsMediosPagoCupon = req.Pagos.Select(p => p.IdMedioPago).Distinct().ToList();
            var mediosPorId = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
                .Where(m => idsMediosPagoCupon.Contains(m.IdMedioPago))
                .ToDictionaryAsync(m => m.IdMedioPago, ct);

            foreach (var (pago, resultado) in req.Pagos.Zip(resultadosPago))
            {
                // Se copia la cantidad de cuotas del plan AL MOMENTO del pago, no se referencia solo
                // por IdPlanCuota: si el plan se edita o se borra después, el historial no cambia.
                int? cuotas = pago.IdPlan is int idPlan
                    ? await _db.PlanesCuota.AsNoTracking().Where(p => p.IdPlan == idPlan)
                        .Select(p => (int?)p.CantidadCuotas).FirstOrDefaultAsync(ct)
                    : null;

                // IdMovPagos es identity (PK de una sola columna): no se asigna a mano. Total sale de
                // resultado.Monto (ya con el descuento por medio de pago aplicado, si correspondía),
                // no de pago.Monto (nominal) — es la plata real que se movió por ese medio, la que
                // tiene que reconciliar el cierre de caja contra el cajón/las liquidaciones de tarjeta.
                var movPago = new MovimientoPago
                {
                    IdMedioPago = pago.IdMedioPago, Total = resultado.Monto, Redondeo = 0,
                    NumeroCupon = Limpiar(pago.NumeroCupon), NumeroLote = Limpiar(pago.NumeroLote),
                    IdPlanCuota = pago.IdPlan, CantidadCuotas = cuotas,
                    IdBanco = pago.IdBanco, NumeroCheque = Limpiar(pago.NumeroCheque),
                    ObservacionesCheque = Limpiar(pago.ObservacionesCheque)
                };
                _db.MovimientosPagos.Add(movPago);
                await _db.SaveChangesAsync(ct); // la DB asigna IdMovPagos; queda disponible para referenciarlo abajo

                // cupones: una fila por cada pago con tarjeta (el resto de los medios no genera
                // cupón). El código de tarjeta puede no estar cargado en el medio todavía — se manda
                // null en ese caso, no se bloquea la venta por eso.
                if (mediosPorId.TryGetValue(pago.IdMedioPago, out var medioPago)
                    && medioPago.TipoPago?.Fuente == FuentePago.Tarjeta)
                {
                    filasCupon.Add(new CuponInterfase(
                        Numero: Limpiar(pago.NumeroCupon), Tarjeta: medioPago.CodigoTarjetaInterfase,
                        Plan: InterfaseContableReglas.Plan(cuotas), Importe: resultado.Monto,
                        FechaRec: DateTime.UtcNow,
                        CodCli: Truncar(datosCliente?.CodigoInt, 5), NomCli: Truncar(datosCliente?.Descripcion, 30),
                        Caja: InterfaseContableReglas.CajaCodigo(operacion.IdCaja),
                        Cajero: InterfaseContableReglas.CajeroCodigo(_currentUser.IdUsuario ?? 0),
                        Operacion: InterfaseContableReglas.Reparto(req.IdOperacion),
                        IdVentaSalon: idComprobante));
                }

                var idMov = (await _db.MovimientosCaja.Where(m => m.IdSucursal == req.IdSucursal)
                    .Select(m => m.IdMovCaja).MaxAsync(x => (int?)x, ct) ?? 0) + 1;
                _db.MovimientosCaja.Add(new MovimientoCaja
                {
                    IdSucursal = req.IdSucursal, IdMovCaja = idMov, IdUsuario = _currentUser.IdUsuario ?? 0,
                    IdCaja = operacion.IdCaja, IdComprobante = idComprobante, IdLote = operacion.IdLote,
                    IdMovPagos = movPago.IdMovPagos, Estado = "Confirmado", Fecha = DateTime.UtcNow
                });
            }

            // Vuelto: se registra como un movimiento APARTE (no se descuenta del pago en Efectivo),
            // exactamente con el mismo mecanismo que un retiro de efectivo — MovimientoPago negativo
            // + MovimientoCaja con IdComprobante null (no es parte del comprobante en sí, es una
            // salida de caja) y Concepto identificable, para que CierreLoteEjecutor lo reste del
            // efectivo esperado en la rendición del turno sin mezclarlo con los retiros manuales.
            if (vuelto > 0)
            {
                var idMedioEfectivo = req.Pagos.First(p => fuentesPorMedio.GetValueOrDefault(p.IdMedioPago) == FuentePago.Efectivo).IdMedioPago;
                var movVuelto = new MovimientoPago { IdMedioPago = idMedioEfectivo, Total = -vuelto, Redondeo = 0 };
                _db.MovimientosPagos.Add(movVuelto);
                await _db.SaveChangesAsync(ct);

                var idMovVuelto = (await _db.MovimientosCaja.Where(m => m.IdSucursal == req.IdSucursal)
                    .Select(m => m.IdMovCaja).MaxAsync(x => (int?)x, ct) ?? 0) + 1;
                _db.MovimientosCaja.Add(new MovimientoCaja
                {
                    IdSucursal = req.IdSucursal, IdMovCaja = idMovVuelto, IdUsuario = _currentUser.IdUsuario ?? 0,
                    IdCaja = operacion.IdCaja, IdComprobante = null, IdLote = operacion.IdLote,
                    IdMovPagos = movVuelto.IdMovPagos, Estado = "Confirmado", Fecha = DateTime.UtcNow,
                    Concepto = $"Vuelto (venta {cabecera.NumeroCompleto})", TipoManual = TipoMovimientoManual.Vuelto
                });
            }

            operacion.Estado = EstadoOperacion.Facturada;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Interfase contable (best-effort, ver IInterfaseContableService): solo Facturas/NC con
            // valor fiscal, nunca Presupuesto. Se manda DESPUÉS del commit — si esto falla, la venta
            // ya quedó persistida e impresa igual, nunca se revierte por esto.
            if (!esPresupuesto)
            {
                // Exento = neto de las líneas reales (no la sintética "Descuento x MP", que también
                // tiene alícuota 0 pero no es una venta exenta) con alícuota 0.
                var exento = detallesCalculados
                    .Where(d => d.Alicuota == 0m && d.Origen.IdPresentacion != 0)
                    .Sum(d => d.Neto);
                // iva_adic y periva son la misma cifra (percepción IVA 21%+10,5%) en dos columnas
                // distintas de la tabla — confirmado con el usuario.
                var percepcionIvaTotal = percepcionResultado.PercepcionIva21 + percepcionResultado.PercepcionIva105;
                // A propósito NO se usa ConstruirClienteFiscal acá: esa función existe para que la
                // controladora fiscal NO identifique a un Consumidor Final (manda "CONSUMIDOR
                // FINAL" sin documento, ver más arriba). La interfase contable es otro consumidor
                // completamente distinto — SIEMPRE necesita código/nombre/condición IVA/CUIT del
                // cliente real cuando hay uno seleccionado en la operación, sin importar si el
                // ticket fiscal lo identificó o no. Confirmado con el usuario (2026-08-21).
                await _interfase.RegistrarVentaAsync(new IvaVtaInterfase(
                    Fecha: cabecera.Fecha,
                    Cliente: Truncar(datosCliente?.CodigoInt, 5),
                    Nombre: Truncar(datosCliente?.Descripcion, 30),
                    CondIva: InterfaseContableReglas.CondIva(datosCliente?.IdCondIva),
                    Cuit: Truncar(datosCliente?.Cuit, 13),
                    Tipo: InterfaseContableReglas.TipoComprobante(tipoComprobante.Signo, letra),
                    Prenum: InterfaseContableReglas.Prenum(puntoVenta.NumeroPuntoVenta),
                    Numero: InterfaseContableReglas.Numero(numero),
                    Neto: totalNeto, Iva: totalIva,
                    IvaAdic: percepcionIvaTotal, Exento: exento, Periva: percepcionIvaTotal,
                    Final: cabecera.Total,
                    BaseImp: percepcionResultado.BaseImponibleIibb, ImpPerc: percepcionResultado.PercepcionIibb,
                    PorcIibb1: percepcionResultado.AlicuotaIibb,
                    Prov: InterfaseContableReglas.ProvFijo, Empresa: sucursal.Empresa?.CodigoInterno ?? "",
                    IdVentaSalon: idComprobante), ct);

                // movstock: una fila por línea real (se excluye la sintética "Descuento x MP",
                // IdPresentacion=0 — no es un artículo vendido). modofact es el mismo para todas las
                // líneas del comprobante: "2" si algún pago de la venta fue por cuenta corriente.
                var codigoEmpresaMovStock = sucursal.Empresa?.CodigoInterno ?? "";
                var modoFact = InterfaseContableReglas.ModoFact(pagosCuentaCorriente.Count > 0);
                var filasMovStock = new List<MovStockInterfase>();
                for (var idx = 0; idx < detallesCalculados.Count; idx++)
                {
                    var (origen, _, alicuota, importe, neto, iva) = detallesCalculados[idx];
                    if (origen.IdPresentacion == 0) continue; // línea sintética de descuento por MP
                    var codigoArticulo = infoPresentaciones.TryGetValue(origen.IdPresentacion, out var info)
                        ? info.CodigoInterno : "";
                    var impInt = percepcionResultado.ImpuestoInternoPorLinea?[idx] ?? 0m;
                    filasMovStock.Add(new MovStockInterfase(
                        Fecha: cabecera.Fecha,
                        Articulo: InterfaseContableReglas.Articulo(codigoArticulo),
                        Salida: origen.Cantidad, Descto: origen.Descuento,
                        Unitario: origen.Precio, Pesos: importe,
                        DeDeposito: InterfaseContableReglas.DepositoFijo,
                        Cliente: Truncar(datosCliente?.CodigoInt, 5),
                        Nombre: Truncar(datosCliente?.Descripcion, 30),
                        Tipo: InterfaseContableReglas.TipoComprobante(tipoComprobante.Signo, letra),
                        Numero: cabecera.NumeroCompleto!,
                        Vendedor: null, Lista: InterfaseContableReglas.ListaFija, ImpInt: impInt,
                        Reparto: InterfaseContableReglas.Reparto(req.IdOperacion), ModoFact: modoFact,
                        CodConv: InterfaseContableReglas.Codconv(origen.IdOfertaPrincipal),
                        Prov: InterfaseContableReglas.ProvFijo, Empresa: codigoEmpresaMovStock,
                        IdVentaSalon: idComprobante, Iva: alicuota * 100m, Periva: 0m));
                }
                await _interfase.RegistrarMovStockAsync(filasMovStock, ct);

                // ctacte: solo si algún pago de la venta fue por cuenta corriente — confirmado con
                // el usuario, no se manda una fila "en 0" en el resto de las ventas.
                if (pagosCuentaCorriente.Count > 0)
                {
                    await _interfase.RegistrarCtaCteAsync(new CtaCteInterfase(
                        Fecha: cabecera.Fecha,
                        Tipo: InterfaseContableReglas.TipoComprobante(tipoComprobante.Signo, letra),
                        Prenum: InterfaseContableReglas.Prenum(puntoVenta.NumeroPuntoVenta),
                        Numero: InterfaseContableReglas.Numero(numero),
                        Debe: pagosCuentaCorriente.Sum(p => p.Monto), Haber: 0m,
                        Cliente: Truncar(datosCliente?.CodigoInt, 5),
                        Prov: InterfaseContableReglas.ProvFijo, Empresa: codigoEmpresaMovStock,
                        IdVentaSalon: idComprobante), ct);
                }

                // comision: muy similar a ivavtas (mismo cliente/tipo/prenum/numero/prov/empresa).
                // vendedor siempre null (sin ese concepto en pos-mayorista, ver movstock);
                // condvta "01"/"02" con la misma condición de cuenta corriente que modofact.
                await _interfase.RegistrarComisionAsync(new ComisionInterfase(
                    Fecha: cabecera.Fecha,
                    Cliente: Truncar(datosCliente?.CodigoInt, 5),
                    Tipo: InterfaseContableReglas.TipoComprobante(tipoComprobante.Signo, letra),
                    Prenum: InterfaseContableReglas.Prenum(puntoVenta.NumeroPuntoVenta),
                    Numero: InterfaseContableReglas.Numero(numero),
                    Neto: totalNeto, Final: cabecera.Total, Vendedor: null,
                    CondVta: InterfaseContableReglas.CondVta(pagosCuentaCorriente.Count > 0),
                    Reparto: InterfaseContableReglas.Reparto(req.IdOperacion),
                    Prov: InterfaseContableReglas.ProvFijo, Empresa: codigoEmpresaMovStock,
                    IdVentaSalon: idComprobante, Hora: InterfaseContableReglas.Hora(cabecera.Fecha)), ct);

                // cupones: una fila por cada pago con tarjeta, recolectadas en el paso 4 (arriba).
                foreach (var filaCupon in filasCupon)
                    await _interfase.RegistrarCuponAsync(filaCupon, ct);
            }

            return new EmitirComprobanteResponse(req.IdSucursal, idComprobante, cabecera.NumeroCompleto!,
                letra, null, null, false, cabecera.Estado.ToString(), totalNeto, totalIva,
                cabecera.Total, resultadosPago, impresion.Ok, impresion.Ok ? null : impresion.Error,
                cabecera.PercepcionIva21, cabecera.PercepcionIva105, cabecera.PercepcionIibb, vuelto,
                cabecera.AlicuotaIibb);
        }
        catch
        {
            // Compensación: anular los pagos ya aprobados antes de revertir la transacción de BD.
            // Con timeout+reintentos: sigue siendo best-effort (si falla definitivamente no hay más
            // remedio automático que registrarlo), pero antes un solo intento SIN timeout podía
            // colgar todo el rollback indefinidamente si la llamada de anulación no respondía.
            foreach (var (provider, idTx) in pagosAprobados)
            {
                try
                {
                    await ResilientCall.ConTimeoutYReintentosAsync(
                        ct2 => provider.AnularAsync(idTx, ct2), TimeoutPago, MaxIntentosPago, EsperaEntreIntentosPago, ct);
                }
                catch
                {
                    // Best-effort: se agotaron los reintentos. Queda un pago aprobado sin anular del
                    // lado del proveedor — requiere conciliación manual, pero no puede bloquear la
                    // venta ya decidida como fallida.
                }
            }
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ComprobanteDetailDto?> ObtenerAsync(int idSucursal, int idComprobante, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var cab = await _db.CabecerasComprobantes.AsNoTracking()
            .Include(c => c.Detalles).Include(c => c.TipoComprobante)
            .FirstOrDefaultAsync(c => c.IdSucursal == idSucursal && c.IdComprobante == idComprobante, ct);
        if (cab is null) return null;

        string? clienteDesc = null;
        if (cab.IdCliente is int idc)
            clienteDesc = await _db.Clientes.AsNoTracking().Where(c => c.IdCliente == idc)
                .Select(c => c.Descripcion).FirstOrDefaultAsync(ct);

        return new ComprobanteDetailDto(cab.IdSucursal, cab.IdComprobante, cab.NumeroCompleto ?? "",
            cab.Letra, cab.TipoComprobante?.Descripcion ?? "", cab.Fecha, cab.IdCliente, clienteDesc,
            cab.Neto, cab.Iva, cab.Total, cab.Cae, cab.CaeVencimiento, cab.EsCaea, cab.Estado.ToString(),
            cab.Detalles.Select(d => new DetalleComprobanteDto(d.IdPresentacion, d.DescripcionTicket,
                d.Cantidad, d.PrecioUnit, d.Descuento, d.AlicuotaIva, d.Importe)).ToList());
    }

    public async Task<string> ResolverLetraAsync(int idSucursal, int idOperacion, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var letraCondIva = await _db.Operaciones.AsNoTracking()
            .Where(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion)
            .Select(o => o.Cliente != null && o.Cliente.CondicionIva != null ? o.Cliente.CondicionIva.Letra : null)
            .FirstOrDefaultAsync(ct);
        return LetraComprobante.Resolver(letraCondIva);
    }

    /// <summary>
    /// Arma el comprobante para imprimirlo en su formato real. La diferencia entre A y B no es
    /// cosmética: en la A los importes de las líneas van NETOS y el IVA se discrimina por alícuota
    /// al pie; en la B van con IVA incluido y no se discrimina nada (es el precio final).
    /// </summary>
    public async Task<ComprobanteImpresionDto?> ObtenerParaImprimirAsync(int idSucursal, int idComprobante, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        var cab = await _db.CabecerasComprobantes.AsNoTracking()
            .Include(c => c.Detalles).Include(c => c.TipoComprobante)
            .FirstOrDefaultAsync(c => c.IdSucursal == idSucursal && c.IdComprobante == idComprobante, ct);
        if (cab is null) return null;

        var sucursal = await _db.Sucursales.AsNoTracking().Include(s => s.Empresa)
            .FirstOrDefaultAsync(s => s.IdSucursal == idSucursal, ct);
        var empresa = sucursal?.Empresa;

        // El domicilio de la sucursal pisa al de la empresa (una empresa con varias bocas factura
        // con la dirección de la que vendió); si la sucursal no lo tiene cargado, cae al de la empresa.
        var emisor = new EmisorComprobanteDto(
            empresa?.Descripcion ?? "", empresa?.Cuit, empresa?.CondicionIva,
            Preferir(sucursal?.Domicilio, empresa?.Domicilio),
            Preferir(sucursal?.Localidad, empresa?.Localidad),
            Preferir(sucursal?.Provincia, empresa?.Provincia),
            Preferir(sucursal?.CodigoPostal, empresa?.CodigoPostal),
            empresa?.IngresosBrutos, empresa?.InicioActividad);

        var letra = cab.Letra ?? LetraComprobante.B;
        var esA = LetraComprobante.ExigeIdentificacion(letra);

        var cliente = cab.IdCliente is int idc
            ? await _db.Clientes.AsNoTracking().Include(c => c.CondicionIva)
                .Where(c => c.IdCliente == idc)
                .Select(c => new ClienteComprobanteDto(c.Descripcion, c.Cuit, c.Documento,
                    c.CondicionIva != null ? c.CondicionIva.Descripcion : null,
                    c.Domicilio, c.Localidad, c.Provincia, c.CodigoPostal))
                .FirstOrDefaultAsync(ct)
            : null;
        // Sin cliente identificado la B se emite a consumidor final, como el ticket de mostrador.
        cliente ??= new ClienteComprobanteDto("Consumidor Final", null, null, "Consumidor final", null, null, null, null);

        var lineas = new List<LineaComprobanteDto>();
        var paraDiscriminar = new List<(decimal Alicuota, decimal Neto, decimal Iva)>();
        decimal descuento = 0;
        foreach (var d in cab.Detalles.OrderBy(d => d.IdDetalleComprobante))
        {
            var (neto, iva) = DesglioIva.Calcular(d.Importe, d.AlicuotaIva);
            paraDiscriminar.Add((d.AlicuotaIva, neto, iva));

            var (netoDesc, _) = DesglioIva.Calcular(d.Descuento, d.AlicuotaIva);
            descuento += esA ? netoDesc : d.Descuento;

            lineas.Add(new LineaComprobanteDto(
                d.DescripcionTicket, d.Cantidad,
                esA ? Round2(SinIva(d.PrecioUnit, d.AlicuotaIva)) : Round2(d.PrecioUnit),
                esA ? Round2(netoDesc) : Round2(d.Descuento),
                esA ? Round2(neto) : Round2(d.Importe),
                d.AlicuotaIva));
        }

        var discriminado = esA
            ? DiscriminacionIva.Agrupar(paraDiscriminar)
                .Select(r => new IvaDiscriminadoDto(r.Alicuota, r.Base, r.Importe)).ToList()
            : new List<IvaDiscriminadoDto>();

        // Pagos efectivamente aplicados al comprobante (los que se ven al pie del ticket).
        var pagos = await (
            from mc in _db.MovimientosCaja.AsNoTracking()
            where mc.IdSucursal == idSucursal && mc.IdComprobante == idComprobante && mc.IdMovPagos != null
            join mp in _db.MovimientosPagos.AsNoTracking() on mc.IdMovPagos equals mp.IdMovPagos
            join me in _db.MediosPago.AsNoTracking() on mp.IdMedioPago equals me.IdMedioPago
            select new PagoComprobanteDto(me.Descripcion, mp.Total)
        ).ToListAsync(ct);

        return new ComprobanteImpresionDto(
            cab.IdSucursal, cab.IdComprobante,
            cab.TipoComprobante?.Descripcion ?? $"Factura {letra}", letra,
            CodigoArcaCorto(cab.TipoComprobante?.CodigoArca), cab.NumeroCompleto ?? "",
            DateTime.SpecifyKind(cab.Fecha, DateTimeKind.Utc),
            emisor, cliente, lineas,
            Round2(descuento), Round2(cab.Neto), Round2(cab.Iva), Round2(cab.Total),
            discriminado, pagos,
            cab.Cae, cab.CaeVencimiento, cab.EsCaea, cab.Estado.ToString(),
            cab.PercepcionIva21, cab.PercepcionIva105, cab.PercepcionIibb, cab.AlicuotaIibb);
    }

    /// <summary>
    /// Los pagos con tarjeta guardan cupón, lote y plan de cuotas. Cupón/lote son lo que después
    /// permite rendir los cupones contra el resumen del operador; el plan es obligatorio porque todo
    /// medio Tarjeta tiene al menos uno cargado (ver PagoAdminService.AsegurarPlanPorDefectoAsync —
    /// "1 cuota" por defecto), así que no elegir ninguno sería un olvido, no una opción válida. Se
    /// exigen en el momento del cobro (que es cuando el cajero tiene el ticket del posnet en la mano).
    /// </summary>
    private static void ValidarCuponYLote(MedioPago medio, PagoInput pago)
    {
        if (medio.TipoPago!.Fuente != FuentePago.Tarjeta) return;
        if (string.IsNullOrWhiteSpace(pago.NumeroCupon))
            throw new DomainException("CUPON_REQUERIDO",
                $"El pago con {medio.Descripcion} necesita el número de cupón.");
        if (string.IsNullOrWhiteSpace(pago.NumeroLote))
            throw new DomainException("LOTE_REQUERIDO",
                $"El pago con {medio.Descripcion} necesita el número de lote.");
        if (pago.IdPlan is null)
            throw new DomainException("PLAN_REQUERIDO",
                $"El pago con {medio.Descripcion} necesita elegir un plan de cuotas.");
    }

    /// <summary>
    /// Los pagos con cheque guardan banco emisor y número de cheque — análogo a cupón/lote de
    /// Tarjeta, pero para poder identificar el cheque físico al presentarlo en Tesorería/banco.
    /// Observaciones queda libre (aclaraciones del cajero: titular, fecha de pago diferido, etc.),
    /// no se exige.
    /// </summary>
    private static void ValidarCheque(MedioPago medio, PagoInput pago)
    {
        if (medio.TipoPago!.Fuente != FuentePago.Cheque) return;
        if (pago.IdBanco is null)
            throw new DomainException("BANCO_REQUERIDO",
                $"El pago con {medio.Descripcion} necesita el banco emisor del cheque.");
        var numero = pago.NumeroCheque?.Trim() ?? "";
        if (numero.Length == 0)
            throw new DomainException("NUMERO_CHEQUE_REQUERIDO",
                $"El pago con {medio.Descripcion} necesita el número de cheque.");
        if (numero.Length > 8)
            throw new DomainException("NUMERO_CHEQUE_INVALIDO",
                "El número de cheque no puede tener más de 8 caracteres.");
    }

    /// <summary>
    /// Un medio restringido a un cluster solo lo pueden usar los clientes de ese cluster. La caja ya
    /// no lo ofrece, pero se revalida acá: es una regla de negocio, no un filtro de pantalla.
    /// </summary>
    private async Task ValidarClusterDelMedioAsync(MedioPago medio, int? idCliente, CancellationToken ct)
    {
        if (medio.IdCluster is not int idCluster) return;

        var habilitado = idCliente is int idc && await _db.ClusterClientes.AsNoTracking()
            .AnyAsync(cc => cc.IdCluster == idCluster && cc.IdCliente == idc, ct);
        if (!habilitado)
            throw new DomainException("MEDIO_NO_HABILITADO",
                $"{medio.Descripcion} está limitado a un grupo de clientes y este cliente no pertenece.");
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Corta a <paramref name="max"/> caracteres — los char(N) de la interfase contable
    /// externa (ivavtas.nombre, etc.) no aceptan más.</summary>
    private static string? Truncar(string? v, int max) => v is null || v.Length <= max ? v : v[..max];

    /// <summary>Lee un valor numérico de la tabla Configuracion (clave/valor); si no está cargada o
    /// no es un número válido, devuelve <paramref name="porDefecto"/> en vez de fallar.</summary>
    private async Task<decimal> ObtenerConfigDecimalAsync(string clave, decimal porDefecto, CancellationToken ct)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == clave).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return valor is not null
            && decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
            ? n : porDefecto;
    }

    /// <summary>
    /// Responsabilidad frente a IVA para la impresora fiscal. Se resuelve por la descripción de la
    /// condición de IVA del cliente (que es texto libre de un ABM), y si no matchea nada se cae a
    /// la letra del comprobante, que siempre está definida.
    /// </summary>
    private static ResponsabilidadIvaFiscal ResponsabilidadFiscal(string? condIva, string? letra)
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

    /// <summary>
    /// Datos de cliente para la controladora fiscal. Si es Consumidor Final NO se lo identifica:
    /// se manda "CONSUMIDOR FINAL" sin número de documento (aunque el cliente tenga CUIT/DNI
    /// cargado en el sistema para uso interno del POS) — la identificación fiscal del receptor solo
    /// corresponde a quien factura discriminando IVA/percepciones (Responsable Inscripto,
    /// Monotributista, Exento, etc.).
    /// </summary>
    private static ClienteFiscal ConstruirClienteFiscal(
        string descripcion, string? cuit, string? documento, string? domicilio, string? condIva, string? letraCondIva)
    {
        var responsabilidad = ResponsabilidadFiscal(condIva, letraCondIva);
        if (responsabilidad == ResponsabilidadIvaFiscal.ConsumidorFinal)
            return new ClienteFiscal("CONSUMIDOR FINAL", null, TipoDocumentoFiscal.Ninguno, responsabilidad, null);

        return new ClienteFiscal(descripcion, cuit ?? documento,
            string.IsNullOrWhiteSpace(cuit) ? TipoDocumentoFiscal.Dni : TipoDocumentoFiscal.Cuit,
            responsabilidad, domicilio);
    }

    private static string? Preferir(string? preferido, string? alternativo) =>
        string.IsNullOrWhiteSpace(preferido) ? alternativo : preferido;

    private static decimal SinIva(decimal conIva, decimal alicuota) =>
        alicuota <= 0 ? conIva : conIva / (1 + alicuota);

    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Reparte el descuento por medio de pago entre los ítems reales del comprobante, sumado al
    /// descuento propio de cada uno (si ya tenía). El controlador fiscal Hasar (protocolo 2G) NO
    /// admite una línea "descuento" suelta a precio $0: <c>ImprimirDescuentoItem</c> se aplica
    /// siempre sobre el ítem inmediato anterior y no puede exceder su propia base — un ítem a $0
    /// SIEMPRE la excede, sin importar el monto ("Monto excede la base descontable... base
    /// descontable nula"). Por eso la línea sintética "Descuento x MP" (que sí existe en la BD y en
    /// el ticket de pantalla, para mostrarla discriminada) nunca se manda tal cual a la fiscal:
    /// se prorratea acá, proporcional a la base (Cantidad×Precio−Descuento propio) de cada ítem
    /// real, con el último ítem elegible absorbiendo el resto para que la suma cierre exacta.
    /// Si ningún ítem tiene base positiva (caso límite) se deja sin repartir — imprimir sin ese
    /// descuento es preferible a no poder facturar la venta.
    /// </summary>
    private static List<ItemFiscal> RepartirDescuentoMp(IReadOnlyList<ItemFiscal> items, decimal descuentoMp)
    {
        if (descuentoMp <= 0 || items.Count == 0) return items.ToList();

        var bases = items.Select(i => Math.Max(0m, i.Cantidad * i.PrecioUnitario - i.Descuento)).ToList();
        var baseTotal = bases.Sum();
        if (baseTotal <= 0) return items.ToList();

        var extra = new decimal[items.Count];
        var elegibles = Enumerable.Range(0, items.Count).Where(i => bases[i] > 0).ToList();
        var asignado = 0m;
        for (var k = 0; k < elegibles.Count; k++)
        {
            var i = elegibles[k];
            var esUltimo = k == elegibles.Count - 1;
            var share = esUltimo ? Round2(descuentoMp - asignado) : Round2(descuentoMp * bases[i] / baseTotal);
            share = Math.Min(share, bases[i]); // nunca puede exceder la base de ESE ítem
            extra[i] = share;
            asignado += share;
        }

        return items.Select((it, i) => extra[i] > 0 ? it with { Descuento = it.Descuento + extra[i] } : it).ToList();
    }

    /// <summary>El encabezado del comprobante muestra el código ARCA en 2 dígitos ("Cod.: 06").</summary>
    private static string? CodigoArcaCorto(string? codigo) =>
        string.IsNullOrWhiteSpace(codigo) ? null
            : (int.TryParse(codigo, out var n) ? n.ToString("D2") : codigo);

    public async Task<ReimpresionResponse> ReimprimirAsync(int idSucursal, int idComprobante, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var cab = await _db.CabecerasComprobantes
            .FirstOrDefaultAsync(c => c.IdSucursal == idSucursal && c.IdComprobante == idComprobante, ct)
            ?? throw new DomainException("NO_ENCONTRADO", "El comprobante no existe.");

        // El presupuesto (letra X) no tiene impresora fiscal: se reimprime desde el frontend
        // (window.print() sobre GET /facturacion/{id}/impresion), no hay nada que reintentar acá.
        if (cab.Letra == "X")
            return new ReimpresionResponse(true, null);

        var puntoVenta = await _db.PuntosVenta.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdSucursal == idSucursal && p.IdPuntoVenta == cab.IdPuntoVenta, ct);
        var sucursal = await _db.Sucursales.AsNoTracking().FirstAsync(s => s.IdSucursal == idSucursal, ct);
        var tipo = await _db.TiposComprobante.AsNoTracking().FirstAsync(t => t.IdTipoComprobante == cab.IdTipoComprobante, ct);

        // La impresora fiscal necesita ítems (rechaza el comprobante si no hay ninguno) y la
        // caja física a la que rutear (IdCaja) — la cabecera no la guarda directo, sale de la
        // operación que la originó. Antes este método armaba el ComprobanteFiscal solo con los
        // totales de cabecera: SIEMPRE fallaba con "El comprobante no tiene ítems para imprimir.".
        var idCaja = cab.IdOperacion is int idOp
            ? await _db.Operaciones.AsNoTracking()
                .Where(o => o.IdSucursal == idSucursal && o.IdOperacion == idOp)
                .Select(o => (int?)o.IdCaja).FirstOrDefaultAsync(ct) ?? 0
            : 0;

        // La línea sintética "Descuento x MP" (IdPresentacion=0, ver EmitirAsync) sí quedó
        // persistida para el ticket de pantalla, pero nunca se manda tal cual a la fiscal — se
        // reparte entre los ítems reales, mismo motivo que en la emisión (ver RepartirDescuentoMp).
        var filas = await _db.DetallesComprobantes.AsNoTracking()
            .Where(d => d.IdSucursal == idSucursal && d.IdComprobante == idComprobante)
            .OrderBy(d => d.IdDetalleComprobante)
            .ToListAsync(ct);
        var descuentoMp = filas.Where(d => d.IdPresentacion == 0).Sum(d => d.Descuento);
        var items = RepartirDescuentoMp(filas.Where(d => d.IdPresentacion != 0)
            .Select(d => new ItemFiscal(d.DescripcionTicket, d.Cantidad, d.PrecioUnit, d.AlicuotaIva,
                d.Descuento, d.IdPresentacion.ToString()))
            .ToList(), descuentoMp);

        var datosCliente = cab.IdCliente is int idc
            ? await _db.Clientes.AsNoTracking().Include(c => c.CondicionIva)
                .Where(c => c.IdCliente == idc)
                .Select(c => new { c.Cuit, c.Documento, c.Domicilio, c.Descripcion,
                    LetraCondIva = c.CondicionIva!.Letra, CondIva = c.CondicionIva!.Descripcion })
                .FirstOrDefaultAsync(ct)
            : null;

        // Se proyecta a un tipo anónimo (sin armar la referencia todavía): la referencia de Cheque
        // necesita el nombre del banco, que sale de un diccionario en memoria y no se puede traducir
        // dentro del Select de la query de EF.
        var bancosReimpresion = await _db.Bancos.AsNoTracking().ToDictionaryAsync(b => b.IdBanco, b => b.Descripcion, ct);
        var pagosCrudos = await _db.MovimientosCaja.AsNoTracking()
            .Where(m => m.IdSucursal == idSucursal && m.IdComprobante == idComprobante && m.IdMovPagos != null)
            .Join(_db.MovimientosPagos.AsNoTracking(), m => m.IdMovPagos, p => p.IdMovPagos, (m, p) => p)
            .Join(_db.MediosPago.AsNoTracking().Include(mp => mp.TipoPago), p => p.IdMedioPago, mp => mp.IdMedioPago,
                (p, mp) => new
                {
                    mp.Descripcion, p.Total, Fuente = mp.TipoPago!.Fuente,
                    p.NumeroCupon, p.NumeroLote, p.CantidadCuotas, p.IdBanco, p.NumeroCheque
                })
            .ToListAsync(ct);
        var pagos = pagosCrudos.Select(p => new PagoFiscal(p.Descripcion, p.Total, p.Fuente,
            p.Fuente == FuentePago.Cheque
                ? $"CHEQUE {p.NumeroCheque} BANCO {bancosReimpresion.GetValueOrDefault(p.IdBanco ?? 0, "")}".TrimEnd()
                : (p.NumeroCupon == null ? null : $"CUPON {p.NumeroCupon}" + (p.NumeroLote == null ? "" : $" LOTE {p.NumeroLote}")),
            Math.Max(1, p.CantidadCuotas ?? 1))).ToList();

        // Las percepciones ya persistidas se vuelven a mandar como tributos. La base imponible no
        // se persiste (solo el importe ya calculado), así que se reconstruye por diferencia contra
        // la tasa fija — es solo informativo para el ticket, el importe (lo que realmente importa) es
        // el que ya se cobró y quedó guardado.
        var tributosReimpresion = new List<TributoFiscal>();
        if (cab.PercepcionIva21 > 0)
            tributosReimpresion.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIva, "PERCEPCION IVA 21%",
                Math.Round(cab.PercepcionIva21 / PercepcionesReglas.TasaPercepcionIva21, 2), cab.PercepcionIva21));
        if (cab.PercepcionIva105 > 0)
            tributosReimpresion.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIva, "PERCEPCION IVA 10,5%",
                Math.Round(cab.PercepcionIva105 / PercepcionesReglas.TasaPercepcionIva105, 2), cab.PercepcionIva105));
        if (cab.PercepcionIibb > 0)
            tributosReimpresion.Add(new TributoFiscal(TipoTributoFiscal.PercepcionIibb, "PERCEPCION IIBB",
                cab.PercepcionIibb, cab.PercepcionIibb));

        var numeroSolo = long.Parse((cab.NumeroCompleto ?? "0-0").Split('-')[1]);
        var cf = new ComprobanteFiscal(sucursal.IdEmpresa, puntoVenta?.NumeroPuntoVenta ?? 0,
            tipo.Descripcion, cab.Letra ?? "", numeroSolo, datosCliente?.Cuit, cab.Neto, cab.Iva, cab.Total, cab.Fecha,
            idSucursal, idCaja,
            datosCliente is null ? null : ConstruirClienteFiscal(datosCliente.Descripcion,
                datosCliente.Cuit, datosCliente.Documento, datosCliente.Domicilio,
                datosCliente.CondIva, datosCliente.LetraCondIva),
            items, pagos, tributosReimpresion);

        ResultadoImpresion r;
        try
        {
            r = await ResilientCall.ConTimeoutAsync(ct2 => _impresora.ImprimirFiscalAsync(cf, ct2), TimeoutFiscal, ct);
        }
        catch (Exception ex)
        {
            r = new ResultadoImpresion(false, null, ex.Message);
        }
        if (r.Ok && cab.Estado != EstadoComprobante.Anulado)
        {
            cab.Estado = EstadoComprobante.Impreso;
            await _db.SaveChangesAsync(ct);
        }
        return new ReimpresionResponse(r.Ok, r.Error);
    }

    /// <summary>
    /// Incremento atómico y serializado del numerador (bloqueo pesimista real vía
    /// UPDATE...OUTPUT). Se ejecuta por ADO.NET directo, dentro de la transacción/conexión
    /// activa de EF Core, porque un UPDATE con OUTPUT no es "componible" para `SqlQuery&lt;T&gt;`.
    /// </summary>
    private async Task<long> IncrementarNumeradorAsync(int idSucursal, int idPuntoVenta, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Numeros SET Valor = Valor + 1 OUTPUT INSERTED.Valor " +
                           "WHERE IdSucursal = @idSucursal AND IdNumero = @idNumero";
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        var pSuc = cmd.CreateParameter(); pSuc.ParameterName = "@idSucursal"; pSuc.Value = idSucursal;
        var pNum = cmd.CreateParameter(); pNum.ParameterName = "@idNumero"; pNum.Value = idPuntoVenta;
        cmd.Parameters.Add(pSuc); cmd.Parameters.Add(pNum);

        var resultado = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(resultado);
    }

    private async Task AsegurarNumeradorAsync(int idSucursal, int idPuntoVenta, CancellationToken ct)
    {
        if (await _db.Numeros.AnyAsync(n => n.IdSucursal == idSucursal && n.IdNumero == idPuntoVenta, ct))
            return;
        try
        {
            _db.Numeros.Add(new Numero { IdSucursal = idSucursal, IdNumero = idPuntoVenta, IdPuntoVenta = idPuntoVenta, Valor = 0 });
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Condición de carrera: otra emisión concurrente ya creó el numerador. No es un error.
        }
    }

    /// <summary>
    /// Cuenta corriente no es un medio de pago externo: es un control de crédito interno. Antes
    /// de esto no existía ningún chequeo — cualquier venta a cuenta corriente se aprobaba ciegamente
    /// vía el proveedor mock, sin límite ni registro. Requiere: cliente identificado en la operación,
    /// cuenta habilitada para esa sucursal (ClienteEnCuenta), y que saldo+monto no supere el límite.
    /// El asiento en CuentaCorriente (Debe) se inserta después, en el paso 4, cuando ya existe
    /// IdComprobante (clave del asiento).
    /// </summary>
    private async Task<PagoResultadoDto> AprobarCuentaCorrienteAsync(
        int idSucursal, int? idCliente, int idMedioPago, decimal monto, CancellationToken ct)
    {
        if (idCliente is not int idc)
            throw new DomainException("CLIENTE_REQUERIDO",
                "La cuenta corriente requiere un cliente identificado en la operación.");

        var cuenta = await _db.ClientesEnCuenta.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IdSucursal == idSucursal && c.IdCliente == idc, ct)
            ?? throw new DomainException("CUENTA_CORRIENTE_NO_HABILITADA",
                "El cliente no tiene cuenta corriente habilitada en esta sucursal.");

        var totales = await _db.CuentasCorrientes.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdCliente == idc)
            .GroupBy(_ => 1)
            .Select(g => new { Debe = g.Sum(x => x.Debe), Haber = g.Sum(x => x.Haber) })
            .FirstOrDefaultAsync(ct);
        var saldoActual = CuentaCorrienteReglas.CalcularSaldo(totales?.Debe ?? 0m, totales?.Haber ?? 0m);

        // El monto ya viene con el descuento por medio de pago aplicado (si correspondía): es lo que
        // realmente se le carga a la cuenta corriente del cliente, no el nominal atribuido al medio.
        if (!CuentaCorrienteReglas.PuedeAprobar(saldoActual, monto, cuenta.LimiteCredito))
            throw new DomainException("LIMITE_CREDITO_EXCEDIDO",
                $"El pago excede el límite de crédito disponible (saldo actual ${saldoActual:0.00}, " +
                $"límite ${cuenta.LimiteCredito:0.00}).");

        return new PagoResultadoDto(idMedioPago, monto, true, $"CC-{idc}", null);
    }
}
