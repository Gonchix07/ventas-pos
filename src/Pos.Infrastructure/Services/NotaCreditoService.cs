using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Application.Abstractions;
using Pos.Application.Abstractions.Fiscal;
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

    private readonly PosDbContext _db;
    private readonly IFiscalService _fiscal;
    private readonly IFiscalPrinter _impresora;
    private readonly ICurrentUser _currentUser;
    private readonly ISupervisorAuthService _supervisorAuth;

    public NotaCreditoService(PosDbContext db, IFiscalService fiscal, IFiscalPrinter impresora,
        ICurrentUser currentUser, ISupervisorAuthService supervisorAuth)
    {
        _db = db; _fiscal = fiscal; _impresora = impresora; _currentUser = currentUser;
        _supervisorAuth = supervisorAuth;
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
                               x.c.IdCliente, x.c.Total })
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
                c.Total, ya, saldo, saldo > 0);
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

        var anuladas = await LineasYaAnuladasAsync(idSucursal, idComprobante, ct);
        var ya = (await AcreditadoPorComprobanteAsync(idSucursal, new[] { idComprobante }, ct))
            .GetValueOrDefault(idComprobante, 0m);
        var saldo = NotaCreditoReglas.SaldoAnulable(cab.Total, ya);

        var clientes = await DescripcionesClientesAsync(new[] { cab.IdCliente }, ct);

        return new ComprobanteAnulableDetalleDto(
            new ComprobanteAnulableDto(idSucursal, idComprobante, cab.NumeroCompleto ?? "", cab.Letra,
                cab.Fecha, cab.IdCliente,
                cab.IdCliente is int id ? clientes.GetValueOrDefault(id) : null,
                cab.Total, ya, saldo, saldo > 0),
            lineas.Select(d => new LineaAnulableDto(d.IdDetalleComprobante, d.IdPresentacion,
                d.DescripcionTicket, d.Cantidad, d.PrecioUnit, d.Descuento, d.AlicuotaIva,
                d.Importe, anuladas.Contains(d.IdDetalleComprobante))).ToList());
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
        var anuladas = await LineasYaAnuladasAsync(req.IdSucursal, req.IdComprobanteOrigen, ct);

        // Líneas de la NC a construir: (descripción, cantidad, precio unitario, alícuota, importe,
        // presentación, línea de origen).
        var lineasNc = ArmarLineas(req, detallesOrigen, anuladas, saldo);
        if (lineasNc.Count == 0)
            throw new DomainException("NADA_PARA_ANULAR", "No hay nada para anular con esos datos.");

        var totalNc = lineasNc.Sum(l => l.Importe);
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

        var efectivo = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
            .FirstOrDefaultAsync(m => m.TipoPago!.Fuente == FuentePago.Efectivo, ct)
            ?? throw new DomainException("MEDIO_EFECTIVO_INEXISTENTE",
                "No hay un medio de pago en efectivo configurado para devolver el importe.");

        decimal totalNeto = 0, totalIva = 0;
        foreach (var l in lineasNc)
        {
            var (neto, iva) = DesglioIva.Calcular(l.Importe, l.Alicuota);
            totalNeto += neto; totalIva += iva;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serie propia de notas de crédito (independiente de la de facturas ante ARCA).
            var idNumero = NumeradorIds.NotaCredito(lote.IdPuntoVenta);
            await AsegurarNumeradorAsync(req.IdSucursal, idNumero, lote.IdPuntoVenta, ct);
            var numero = await IncrementarNumeradorAsync(req.IdSucursal, idNumero, ct);

            await RecursoLockHelper.AdquirirAsync(_db, $"Comprobante:{req.IdSucursal}", ct);

            // Re-chequeo del saldo DESPUÉS de tomar el lock: entre la lectura de más arriba y este
            // punto, otra caja pudo haber acreditado la misma factura. Sin esto, dos notas de
            // crédito concurrentes sobre el mismo comprobante podrían sumar más que su total.
            var yaAcreditadoAhora = (await AcreditadoPorComprobanteAsync(req.IdSucursal, new[] { req.IdComprobanteOrigen }, ct))
                .GetValueOrDefault(req.IdComprobanteOrigen, 0m);
            var saldoAhora = NotaCreditoReglas.SaldoAnulable(origen.Total, yaAcreditadoAhora);
            if (!NotaCreditoReglas.ImporteAcreditable(totalNc, saldoAhora))
                throw new DomainException("EXCEDE_SALDO_ANULABLE",
                    $"El saldo anulable del comprobante cambió (${saldoAhora:0.00}). Volvé a intentar.");

            var idComprobante = (await _db.CabecerasComprobantes.Where(c => c.IdSucursal == req.IdSucursal)
                .Select(c => c.IdComprobante).MaxAsync(x => (int?)x, ct) ?? 0) + 1;

            var cabecera = new CabeceraComprobante
            {
                IdSucursal = req.IdSucursal, IdComprobante = idComprobante,
                IdTipoComprobante = tipoNc.IdTipoComprobante, IdCliente = origen.IdCliente,
                IdPuntoVenta = lote.IdPuntoVenta, IdOperacion = null, Letra = letra,
                NumeroCompleto = NumeroComprobanteFormatter.Formatear(puntoVenta.NumeroPuntoVenta, numero),
                Fecha = DateTime.UtcNow, Neto = totalNeto, Iva = totalIva, Percepciones = 0,
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

            // Devolución en efectivo: movimiento NEGATIVO. El acumulador del arqueo/cierre lo
            // clasifica como anulación por el signo del tipo de comprobante, no por el signo del
            // monto, así que la plata sale de la caja y además se muestra discriminada.
            var movPago = new MovimientoPago
            {
                IdMedioPago = efectivo.IdMedioPago, Total = -totalNc, Redondeo = 0
            };
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

            // Si la factura se había cargado a cuenta corriente, la NC la descarga (Haber).
            if (origen.IdCliente is int idCliente && await TieneAsientoCuentaCorrienteAsync(req.IdSucursal, req.IdComprobanteOrigen, ct))
            {
                _db.CuentasCorrientes.Add(new CuentaCorriente
                {
                    IdSucursal = req.IdSucursal, IdCliente = idCliente,
                    IdComprobante = idComprobante, Debe = 0, Haber = totalNc
                });
            }

            // CAE de la nota de crédito. Si falla, la NC no se emite: a diferencia de la impresión
            // (best-effort), sin autorización fiscal no hay comprobante válido que entregar.
            var cf = new ComprobanteFiscal(puntoVenta.IdSucursal, puntoVenta.NumeroPuntoVenta,
                tipoNc.Descripcion, letra, numero, null, totalNeto, totalIva, totalNc, DateTime.UtcNow,
                req.IdSucursal, req.IdCaja);

            var cae = await _fiscal.SolicitarCaeAsync(cf, ct);
            if (!cae.Ok)
                throw new DomainException("FISCAL_INDISPONIBLE",
                    $"No se pudo autorizar la nota de crédito: {cae.Error}");
            cabecera.Cae = cae.Cae; cabecera.CaeVencimiento = cae.Vencimiento; cabecera.EsCaea = cae.EsCaea;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Impresión fiscal (best-effort, fuera de la transacción): la NC ya está registrada y
            // el efectivo ya salió de la caja; un fallo de la impresora no la invalida.
            var cfImpresion = cf with
            {
                Cliente = await ClienteFiscalAsync(origen.IdCliente, letra, ct),
                Items = lineasNc.Select(l => new ItemFiscal(l.Descripcion, l.Cantidad,
                    l.PrecioUnitario, l.Alicuota, 0m, l.IdPresentacion.ToString())).ToList(),
                Pagos = new List<PagoFiscal> { new(efectivo.Descripcion, totalNc, FuentePago.Efectivo, null, 1) }
            };

            ResultadoImpresion impresion;
            try
            {
                impresion = await ResilientCall.ConTimeoutAsync(
                    ct2 => _impresora.ImprimirNotaCreditoAsync(cfImpresion, ct2), TimeoutFiscal, ct);
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

            return new NotaCreditoResponse(req.IdSucursal, idComprobante, cabecera.NumeroCompleto!,
                letra, cabecera.Cae, cabecera.CaeVencimiento, cabecera.EsCaea, cabecera.Estado.ToString(),
                totalNeto, totalIva, totalNc, totalNc, impresion.Ok, impresion.Ok ? null : impresion.Error);
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
        List<DetalleComprobante> detallesOrigen, HashSet<long> anuladas, decimal saldo)
    {
        switch (req.Tipo)
        {
            case TipoAnulacion.Total:
                return detallesOrigen.Where(d => !anuladas.Contains(d.IdDetalleComprobante))
                    .Select(DesdeDetalle).ToList();

            case TipoAnulacion.PorArticulos:
            {
                var ids = (req.IdsDetalle ?? new List<long>()).ToHashSet();
                if (ids.Count == 0)
                    throw new DomainException("SIN_ARTICULOS", "Seleccioná al menos un artículo para anular.");

                var elegidas = detallesOrigen.Where(d => ids.Contains(d.IdDetalleComprobante)).ToList();
                if (elegidas.Count != ids.Count)
                    throw new DomainException("ARTICULO_INEXISTENTE",
                        "Alguno de los artículos seleccionados no pertenece a ese comprobante.");

                var yaAnulada = elegidas.FirstOrDefault(d => anuladas.Contains(d.IdDetalleComprobante));
                if (yaAnulada is not null)
                    throw new DomainException("ARTICULO_YA_ANULADO",
                        $"El artículo \"{yaAnulada.DescripcionTicket}\" ya fue anulado en una nota de crédito anterior.");

                return elegidas.Select(DesdeDetalle).ToList();
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

    private static LineaNc DesdeDetalle(DetalleComprobante d) =>
        new(d.DescripcionTicket, d.Cantidad, d.PrecioUnit, d.AlicuotaIva, d.Importe,
            d.IdPresentacion, d.IdDetalleComprobante);

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

    /// <summary>Líneas de la factura que ya fueron acreditadas por artículo.</summary>
    private async Task<HashSet<long>> LineasYaAnuladasAsync(int idSucursal, int idComprobante, CancellationToken ct)
    {
        var ids = await (
            from d in _db.DetallesComprobantes.AsNoTracking()
            join c in _db.CabecerasComprobantes.AsNoTracking()
                on new { d.IdSucursal, d.IdComprobante } equals new { c.IdSucursal, c.IdComprobante }
            where c.IdSucursal == idSucursal && c.IdComprobanteOrigen == idComprobante
                  && d.IdDetalleOrigen != null
            select d.IdDetalleOrigen!.Value).ToListAsync(ct);
        return ids.ToHashSet();
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
