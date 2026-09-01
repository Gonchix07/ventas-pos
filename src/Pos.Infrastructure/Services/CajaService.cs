using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Caja;
using Pos.Application.Common;
using Pos.Application.Percepciones;
using Pos.Application.Pricing;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

public class CajaService : ICajaService
{
    private readonly PosDbContext _db;
    private readonly IPricingService _pricing;
    private readonly IImageBank _images;
    private readonly ICurrentUser _currentUser;
    private readonly ISupervisorAuthService _supervisorAuth;
    private readonly IPercepcionesCalculoService _percepciones;

    public CajaService(PosDbContext db, IPricingService pricing, IImageBank images, ICurrentUser currentUser,
        ISupervisorAuthService supervisorAuth, IPercepcionesCalculoService percepciones)
    {
        _db = db;
        _pricing = pricing;
        _images = images;
        _currentUser = currentUser;
        _supervisorAuth = supervisorAuth;
        _percepciones = percepciones;
    }

    // ---------- Apertura de caja ----------

    public async Task<LoteDto> AbrirCajaAsync(AperturaRequest req, CancellationToken ct = default)
    {
        await AsegurarCajaAsync(req.IdSucursal, req.IdCaja, paraApertura: true, ct);

        // Abrir una caja distinta a la que el puesto tiene asignada (PC caída, se sigue vendiendo
        // desde otra) requiere autorización de supervisor — la apertura en LA PROPIA caja no.
        if (_currentUser.IdCaja is int propia && propia != req.IdCaja)
            await _supervisorAuth.ExigirAsync(req.CodigoSupervisor, ct);

        var idUsuario = _currentUser.IdUsuario ?? 0;

        if (!await _db.Cajas.AnyAsync(c => c.IdSucursal == req.IdSucursal && c.IdCaja == req.IdCaja, ct))
            throw new DomainException("CAJA_INEXISTENTE", "La caja indicada no existe en la sucursal.");

        if (req.MontoInicial < 0)
            throw new DomainException("MONTO_INVALIDO", "El fondo inicial no puede ser negativo.");

        // Se resuelve ANTES del lock/transacción: es solo lectura y así no se retiene el lock más de
        // lo necesario si el medio no existe.
        int? idMedioPagoInicial = null;
        if (req.MontoInicial > 0)
        {
            if (req.IdMedioPago is int idm)
            {
                if (!await _db.MediosPago.AsNoTracking().AnyAsync(m => m.IdMedioPago == idm, ct))
                    throw new DomainException("MEDIO_INEXISTENTE", "El medio de pago indicado no existe.");
                idMedioPagoInicial = idm;
            }
            else
            {
                var efectivo = await _db.MediosPago.AsNoTracking().Include(m => m.TipoPago)
                    .FirstOrDefaultAsync(m => m.TipoPago!.Fuente == FuentePago.Efectivo, ct)
                    ?? throw new DomainException("SIN_MEDIO_EFECTIVO", "No hay un medio de pago de tipo Efectivo configurado.");
                idMedioPagoInicial = efectivo.IdMedioPago;
            }
        }

        // El chequeo "no hay ya un lote abierto hoy" + la generación del próximo IdLote se hacen
        // bajo un lock de aplicación por caja+cajero: sin esto, dos aperturas simultáneas del MISMO
        // cajero en la MISMA caja (doble clic, dos pestañas) pueden pasar ambas el chequeo antes de
        // que ninguna haya insertado, y terminar con dos lotes "Abierto" a la vez (ver FASE-5, bug
        // de lote viejo — este es el mismo problema pero en el mismo día). El lote es por
        // (sucursal, caja, cajero): varios cajeros pueden compartir la misma caja física a la vez,
        // cada uno con su propio lote — no confundir con "una caja física, un solo lote".
        //
        // Un cajero SÍ puede abrir más de un lote el mismo día, uno atrás del otro (turno partido,
        // reabrir después de cerrar por error, etc.) — lo único que no puede haber es DOS abiertos
        // a la vez, que es exactamente lo que impide el índice único de abajo (filtrado por
        // Estado=Abierto, ver PosDbContext). Antes había además una regla de dominio
        // (LoteCajaReglas.PuedeAbrirNuevoLote) que bloqueaba un SEGUNDO lote el mismo día aunque el
        // primero ya estuviera cerrado — se sacó a pedido del usuario (2026-08-25).
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await RecursoLockHelper.AdquirirAsync(_db, $"LoteCaja:{req.IdSucursal}:{req.IdCaja}:{idUsuario}", ct);

        var existente = await ObtenerLoteAbiertoHoyAsync(req.IdSucursal, req.IdCaja, idUsuario, ct);
        if (existente is not null)
        {
            await tx.CommitAsync(ct);
            return await MapAsync(existente, ct);
        }

        var next = (await _db.LotesCaja.Where(l => l.IdSucursal == req.IdSucursal)
            .Select(l => l.IdLote).MaxAsync(x => (int?)x, ct) ?? 0) + 1;

        var lote = new LoteCaja
        {
            IdSucursal = req.IdSucursal, IdLote = next, IdCaja = req.IdCaja,
            IdUsuarioApertura = idUsuario,
            FechaApertura = DateTime.UtcNow, Estado = EstadoLote.Abierto
        };
        _db.LotesCaja.Add(lote);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EsChoqueDeLoteAbierto(ex))
        {
            // Respaldo del lock de aplicación de más arriba: si el índice único rechaza la inserción,
            // el motivo es siempre "ya hay un lote abierto para esta caja+cajero hoy". Traducirlo a
            // DomainException lo convierte en un 409 con código, en vez del 500 genérico que no le
            // dice nada al cajero.
            throw new DomainException("LOTE_YA_ABIERTO", "Ya existe un lote abierto hoy para este cajero en esta caja.");
        }

        if (idMedioPagoInicial is int idMedio)
        {
            await MovimientoManualCajaHelper.RegistrarAsync(_db, req.IdSucursal, req.IdCaja, lote.IdLote,
                idUsuario, idMedio, req.MontoInicial, TipoMovimientoManual.Ingreso, "Fondo inicial de caja", ct);
        }

        await tx.CommitAsync(ct);
        return await MapAsync(lote, ct);
    }

    /// <summary>2601/2627 = violación de índice/restricción único en SQL Server.</summary>
    private static bool EsChoqueDeLoteAbierto(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException sql &&
        (sql.Number == 2601 || sql.Number == 2627);

    public async Task<LoteDto?> ObtenerLoteActualAsync(int idSucursal, int idCaja, CancellationToken ct = default)
    {
        await AsegurarCajaAsync(idSucursal, idCaja, paraApertura: false, ct);

        var lote = await ObtenerLoteAbiertoHoyAsync(idSucursal, idCaja, _currentUser.IdUsuario ?? 0, ct);
        return lote is null ? null : await MapAsync(lote, ct);
    }

    /// <summary>
    /// Turnos abiertos hoy del cajero logueado en la sucursal, en CUALQUIER caja — para poder
    /// retomar el turno desde otra PC cuando la original se cae (la caja que resuelve la IP del
    /// puesto no es la del lote). Incluye cuántas ventas quedaron sin cobrar en cada uno.
    /// </summary>
    public async Task<IReadOnlyList<TurnoAbiertoDto>> GetMisTurnosAbiertosAsync(int idSucursal, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var idUsuario = _currentUser.IdUsuario ?? 0;
        var hoy = DateTime.UtcNow.Date;

        var lotes = await (
            from l in _db.LotesCaja.AsNoTracking()
            where l.IdSucursal == idSucursal && l.IdUsuarioApertura == idUsuario
                && l.Estado == EstadoLote.Abierto && l.FechaApertura.Date == hoy
            join c in _db.Cajas.AsNoTracking()
                on new { l.IdSucursal, l.IdCaja } equals new { c.IdSucursal, c.IdCaja }
            orderby l.IdLote
            select new { l.IdSucursal, l.IdLote, l.IdCaja, c.Descripcion, c.IdPuntoVenta, l.FechaApertura }
        ).ToListAsync(ct);

        var ids = lotes.Select(l => l.IdLote).ToList();
        var pendientesPorLote = await _db.Operaciones.AsNoTracking()
            .Where(o => o.IdSucursal == idSucursal && ids.Contains(o.IdLote)
                && (o.Estado == EstadoOperacion.EnCurso || o.Estado == EstadoOperacion.Finalizada)
                && o.Detalles.Any())
            .GroupBy(o => o.IdLote)
            .Select(g => new { IdLote = g.Key, Cantidad = g.Count() })
            .ToListAsync(ct);

        return lotes.Select(l => new TurnoAbiertoDto(l.IdSucursal, l.IdLote, l.IdCaja, l.Descripcion,
            l.IdPuntoVenta, DateTime.SpecifyKind(l.FechaApertura, DateTimeKind.Utc),
            pendientesPorLote.FirstOrDefault(p => p.IdLote == l.IdLote)?.Cantidad ?? 0,
            l.IdCaja == _currentUser.IdCaja)).ToList();
    }

    /// <summary>Cajas de la sucursal, para elegir dónde abrir el turno cuando la PC no tiene puesto
    /// configurado (o el cajero se sienta en otro puesto). Accesible a los roles de caja.</summary>
    public async Task<IReadOnlyList<CajaDisponibleDto>> GetCajasAsync(int idSucursal, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        return await _db.Cajas.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal)
            .OrderBy(c => c.Descripcion)
            .Select(c => new CajaDisponibleDto(c.IdSucursal, c.IdCaja, c.Descripcion, c.IdPuntoVenta))
            .ToListAsync(ct);
    }

    private Task AsegurarCajaAsync(int idSucursal, int idCaja, bool paraApertura, CancellationToken ct) =>
        CajaAccesoHelper.AsegurarCajaOperableAsync(_db, _currentUser, idSucursal, idCaja, paraApertura, ct);

    /// <summary>El lote es siempre del cajero logueado — nunca de "la caja" en abstracto, porque
    /// varios cajeros pueden compartir la misma caja física con lotes propios simultáneos.</summary>
    private async Task<LoteCaja?> ObtenerLoteAbiertoHoyAsync(int idSucursal, int idCaja, int idUsuario, CancellationToken ct) =>
        await _db.LotesCaja.FirstOrDefaultAsync(l =>
            l.IdSucursal == idSucursal && l.IdCaja == idCaja && l.IdUsuarioApertura == idUsuario &&
            l.Estado == EstadoLote.Abierto && l.FechaApertura.Date == DateTime.UtcNow.Date, ct);

    private async Task<LoteDto> MapAsync(LoteCaja l, CancellationToken ct)
    {
        var caja = await (
            from c in _db.Cajas.AsNoTracking()
            join pv in _db.PuntosVenta.AsNoTracking()
                on new { c.IdSucursal, c.IdPuntoVenta } equals new { pv.IdSucursal, pv.IdPuntoVenta }
            where c.IdSucursal == l.IdSucursal && c.IdCaja == l.IdCaja
            select new { c.IdPuntoVenta, c.Descripcion, c.AdmitePresupuesto, pv.IdTipoPuntoVenta }
        ).FirstOrDefaultAsync(ct);
        var modo = TiposPuntoVentaFijos.Buscar(caja?.IdTipoPuntoVenta ?? 0)?.Descripcion ?? "";
        return new LoteDto(l.IdSucursal, l.IdLote, l.IdCaja, caja?.Descripcion ?? "", caja?.IdPuntoVenta ?? 0,
            l.FechaApertura, l.Estado.ToString(), caja?.AdmitePresupuesto ?? false, modo);
    }

    public async Task<DescripcionCajaDto> ObtenerDescripcionCajaAsync(int idSucursal, int idCaja, CancellationToken ct = default)
    {
        // Se consulta también en la pantalla de pre-apertura (todavía sin lote), así que se permite
        // igual que la apertura: cualquier caja de la sucursal autorizada.
        await AsegurarCajaAsync(idSucursal, idCaja, paraApertura: true, ct);
        var descripcionCaja = await _db.Cajas.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdCaja == idCaja)
            .Select(c => c.Descripcion).FirstOrDefaultAsync(ct);
        var nombreSucursal = await _db.Sucursales.AsNoTracking()
            .Where(s => s.IdSucursal == idSucursal)
            .Select(s => s.Descripcion).FirstOrDefaultAsync(ct);
        return new DescripcionCajaDto(nombreSucursal, descripcionCaja);
    }

    public async Task<IReadOnlyList<MedioPagoResumen>> GetMediosPagoAsync(int? idCliente = null, CancellationToken ct = default)
    {
        // Un medio puede estar restringido a un cluster de clientes (ej. una cuenta bancaria propia
        // de un grupo): solo se ofrece si el cliente de la venta pertenece a ese cluster. Los medios
        // sin cluster son para todos.
        var clustersDelCliente = idCliente is int idc
            ? await _db.ClusterClientes.AsNoTracking().Where(cc => cc.IdCliente == idc)
                .Select(cc => cc.IdCluster).ToListAsync(ct)
            : new List<int>();

        var query =
            from m in _db.MediosPago.AsNoTracking().Where(x => x.Activo)
            where m.IdCluster == null || clustersDelCliente.Contains(m.IdCluster.Value)
            join t in _db.TiposPago.AsNoTracking() on m.IdTipoPago equals t.IdTipoPago
            // El predeterminado primero: es el que la caja propone al abrir el cobro.
            orderby m.EsPredeterminado descending, m.Descripcion
            select new MedioPagoResumen(m.IdMedioPago, m.Descripcion, (int)t.Fuente, m.EsPredeterminado,
                m.ImprimeComprobante);
        return await query.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PlanCuotaResumen>> GetPlanesMedioAsync(int idMedioPago, CancellationToken ct = default) =>
        await _db.PlanesCuota.AsNoTracking().Where(p => p.IdMedioPago == idMedioPago)
            .OrderBy(p => p.CantidadCuotas)
            .Select(p => new PlanCuotaResumen(p.IdPlan, p.Denominacion, p.CantidadCuotas))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BancoResumen>> GetBancosAsync(CancellationToken ct = default) =>
        await _db.Bancos.AsNoTracking().OrderBy(b => b.Descripcion)
            .Select(b => new BancoResumen(b.IdBanco, b.Descripcion))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OfertaMedioPagoVigenteDto>> GetOfertasMedioPagoVigentesAsync(int idSucursal, CancellationToken ct = default)
    {
        var hoy = DateTime.UtcNow.Date;
        return await _db.OfertasMedioPago.AsNoTracking()
            .Where(o => o.IdSucursal == idSucursal && o.Activo && o.FechaInicio <= hoy && o.FechaFin >= hoy)
            .Select(o => new OfertaMedioPagoVigenteDto(o.IdMedioPago, o.IdPlanCuota, o.Porcentaje, o.TopeMaximo))
            .ToListAsync(ct);
    }

    // ---------- Identificación de cliente ----------

    public async Task<IReadOnlyList<ClienteResumen>> BuscarClienteAsync(int idSucursal, string query, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var q = query.Trim();
        if (q.Length == 0) return Array.Empty<ClienteResumen>();

        var clientes = await _db.Clientes.AsNoTracking()
            .Include(c => c.CondicionIva)
            .Where(c => c.Activo && (
                c.Descripcion.Contains(q) || c.CodigoInt.Contains(q) ||
                (c.NombreFantasia != null && c.NombreFantasia.Contains(q)) ||
                (c.Cuit != null && c.Cuit.Contains(q)) || (c.Documento != null && c.Documento.Contains(q))))
            .OrderBy(c => c.Descripcion)
            .Take(20).ToListAsync(ct);

        // También se puede identificar por número de tarjeta (SRS).
        if (clientes.Count == 0)
        {
            var porTarjeta = await (
                // Solo la tarjeta VIGENTE: una anulada (reemplazada por otra) no identifica al cliente.
                from t in _db.TarjetasClientes.AsNoTracking().Where(t => t.NroTarjeta == q && t.Activa)
                join c in _db.Clientes.AsNoTracking().Include(c => c.CondicionIva) on t.IdCliente equals c.IdCliente
                select c).Take(5).ToListAsync(ct);
            clientes = porTarjeta;
        }

        var ids = clientes.Select(c => c.IdCliente).ToList();

        // El convenio es POR SUCURSAL (igual que en PricingService): sin este filtro se podía
        // mostrar el descuento de otra sucursal mientras el precio aplicado usaba el de esta.
        var convenios = await _db.Convenios.AsNoTracking()
            .Where(cv => cv.IdSucursal == idSucursal && ids.Contains(cv.IdCliente))
            .ToListAsync(ct);

        var tarjetas = await (
            from t in _db.TarjetasClientes.AsNoTracking().Where(t => ids.Contains(t.IdCliente) && t.Activa)
            join tt in _db.TiposTarjeta.AsNoTracking() on t.IdTipoTarjeta equals tt.IdTipoTarjeta
            select new { t.IdCliente, t.NroTarjeta, TipoDescripcion = tt.Descripcion, tt.IdListaPrecio }
        ).ToListAsync(ct);

        // Autorizados activos: el cajero los ve debajo del nombre para controlar a quién le vende.
        var autorizados = await _db.Autorizados.AsNoTracking()
            .Where(a => ids.Contains(a.IdCliente) && a.Activo)
            .OrderBy(a => a.Descripcion)
            .Select(a => new { a.IdCliente, a.Dni, a.Descripcion })
            .ToListAsync(ct);

        var idsListas = convenios.Select(cv => cv.IdListaPrecio)
            .Concat(tarjetas.Select(t => t.IdListaPrecio))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        // El nombre visible de una lista es su CodigoInterno ("AZUL", "ROJA", "FOLDER AGO").
        var listas = await _db.ListasPrecios.AsNoTracking()
            .Where(l => idsListas.Contains(l.IdListaPrecio))
            .Select(l => new { l.IdListaPrecio, l.CodigoInterno })
            .ToListAsync(ct);
        string? NombreLista(int? id) => id is null ? null
            : listas.FirstOrDefault(l => l.IdListaPrecio == id.Value)?.CodigoInterno;

        return clientes.Select(c =>
        {
            var cv = convenios.FirstOrDefault(x => x.IdCliente == c.IdCliente);
            var delCliente = tarjetas.Where(t => t.IdCliente == c.IdCliente).ToList();
            var tarjeta = delCliente.FirstOrDefault();

            // Prioridad de la lista mostrada: la del convenio (que es la que caja realmente aplica);
            // si no hay, la del tipo de tarjeta, marcada como tal.
            var lista = NombreLista(cv?.IdListaPrecio);
            var origen = lista is null ? null : "Convenio";
            if (lista is null && tarjeta?.IdListaPrecio is not null)
            {
                lista = NombreLista(tarjeta.IdListaPrecio);
                origen = lista is null ? null : "Tarjeta";
            }

            return new ClienteResumen(c.IdCliente, c.CodigoInt, c.Descripcion, c.NombreFantasia, c.Cuit, c.Documento,
                c.PermitePresupuesto, c.CondicionIva?.Descripcion, cv?.IdConvenio, cv?.Descuento,
                c.Domicilio, c.Localidad, tarjeta?.NroTarjeta, tarjeta?.TipoDescripcion, delCliente.Count,
                lista, origen,
                autorizados.Where(a => a.IdCliente == c.IdCliente)
                    .Select(a => new AutorizadoResumen(a.Dni, a.Descripcion)).ToList());
        }).ToList();
    }

    // ---------- Búsqueda de artículo ----------

    public async Task<ArticuloEncontrado?> BuscarArticuloAsync(int idSucursal, string codigo, int? idCliente, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        codigo = codigo.Trim();

        // Código interno PRIMERO (antes que código de barra): un código de artículo (corto, ~5-6
        // dígitos) identifica al ARTÍCULO, no a una presentación puntual — así que siempre se
        // resuelve a su mínima fracción (unidad suelta, menor UnidadXBulto), nunca a un bulto.
        // Se prueba antes que la barra a propósito: si por casualidad ese mismo valor también está
        // cargado como código de barra de una presentación de bulto (pasaba con el artículo 25002:
        // "25002" es su código interno Y coincide con la barra de la presentación x6), la barra no
        // debe ganarle a la búsqueda por código interno — un código de barra real (EAN, 8-13
        // dígitos) prácticamente nunca coincide por casualidad con un código interno corto, así que
        // este orden no cambia nada para un escaneo real de góndola.
        var match = await (
            from a in _db.Articulos.AsNoTracking().Where(x => x.CodigoInterno == codigo)
            join pr in _db.Presentaciones.AsNoTracking() on a.IdArticulo equals pr.IdArticulo
            orderby pr.UnidadXBulto
            select new { pr.IdPresentacion, a.IdArticulo, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket, pr.UnidadXBulto, a.Activo }
        ).FirstOrDefaultAsync(ct);

        // orderby UnidadXBulto también acá: si el código coincide con más de una barra (no debería,
        // pero el catálogo importado puede tener alguna duplicada entre presentaciones del mismo
        // artículo — ver tools/import-catalogo), se prioriza la mínima fracción antes que un bulto.
        if (match is null)
        {
            match = await (
                from b in _db.Barras.AsNoTracking().Where(x => x.CodigoBarra == codigo)
                join pr in _db.Presentaciones.AsNoTracking() on b.IdPresentacion equals pr.IdPresentacion
                join a in _db.Articulos.AsNoTracking() on pr.IdArticulo equals a.IdArticulo
                orderby pr.UnidadXBulto
                select new { pr.IdPresentacion, a.IdArticulo, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket, pr.UnidadXBulto, a.Activo }
            ).FirstOrDefaultAsync(ct);
        }

        // Etiqueta de balanza: la barra no está cargada como tal (cambia con cada pesada), trae
        // adentro el código del artículo y el peso. El peso viaja como cantidad de la línea.
        decimal? cantidadDetectada = null;
        if (match is null && BarraBalanza.TryParse(codigo, out var pesada))
        {
            var codigos = BarraBalanza.CodigosPosibles(pesada.CodigoArticulo);
            match = await (
                from a in _db.Articulos.AsNoTracking().Where(x => codigos.Contains(x.CodigoInterno))
                join pr in _db.Presentaciones.AsNoTracking() on a.IdArticulo equals pr.IdArticulo
                orderby pr.UnidadXBulto
                select new { pr.IdPresentacion, a.IdArticulo, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket, pr.UnidadXBulto, a.Activo }
            ).FirstOrDefaultAsync(ct);
            if (match is not null) cantidadDetectada = pesada.Peso;
        }

        if (match is null || !match.Activo) return null;

        var precio = await _pricing.ResolverPrecioAsync(new ResolverPrecioRequest(idSucursal, match.IdPresentacion, idCliente), ct);

        return new ArticuloEncontrado(match.IdArticulo, match.IdPresentacion, match.CodigoInterno,
            match.Descripcion, match.DescripcionTicket, match.UnidadXBulto,
            _images.BuildImageUrl(match.CodigoInterno).ToString(),
            precio.PrecioVigente, precio.PrecioConvenio, precio.TieneConvenio, cantidadDetectada);
    }

    /// <summary>Cuántos artículos como mucho devuelve la búsqueda manual de la lupa.</summary>
    public const int MaxArticulosBusqueda = 20;

    public async Task<IReadOnlyList<ArticuloEncontrado>> BuscarArticulosAsync(int idSucursal, string texto,
        int? idCliente, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var t = (texto ?? "").Trim();
        if (t.Length < 2) return Array.Empty<ArticuloEncontrado>();

        // Una fila por presentación: el cajero elige el bulto que le están comprando.
        // Se busca igual que el ABM (código, descripción o barra) para que el operador no tenga
        // que saber con cuál de los tres está buscando.
        var candidatos = await (
            from a in _db.Articulos.AsNoTracking().Where(x => x.Activo)
            join pr in _db.Presentaciones.AsNoTracking() on a.IdArticulo equals pr.IdArticulo
            where a.CodigoInterno.Contains(t) || a.Descripcion.Contains(t)
                || pr.Barras.Any(b => b.CodigoBarra == t)
            orderby a.Descripcion, pr.UnidadXBulto
            select new { pr.IdPresentacion, a.IdArticulo, a.CodigoInterno, a.Descripcion, pr.DescripcionTicket, pr.UnidadXBulto }
        ).Take(MaxArticulosBusqueda).ToListAsync(ct);

        var resultado = new List<ArticuloEncontrado>(candidatos.Count);
        foreach (var m in candidatos)
        {
            var precio = await _pricing.ResolverPrecioAsync(
                new ResolverPrecioRequest(idSucursal, m.IdPresentacion, idCliente), ct);
            // Sin precio vigente no se puede vender, así que no se ofrece.
            if (!precio.Encontrado) continue;
            resultado.Add(new ArticuloEncontrado(m.IdArticulo, m.IdPresentacion, m.CodigoInterno,
                m.Descripcion, m.DescripcionTicket, m.UnidadXBulto,
                _images.BuildImageUrl(m.CodigoInterno).ToString(),
                precio.PrecioVigente, precio.PrecioConvenio, precio.TieneConvenio));
        }
        return resultado;
    }

    // ---------- Operación ----------

    public async Task<IReadOnlyList<OperacionPendienteDto>> GetOperacionesPendientesAsync(
        int idSucursal, int idCaja, int idCliente, CancellationToken ct = default)
    {
        await AsegurarCajaAsync(idSucursal, idCaja, paraApertura: false, ct);

        // Sin lote abierto no hay nada que retomar: la venta se factura contra el turno en curso.
        var lote = await ObtenerLoteAbiertoHoyAsync(idSucursal, idCaja, _currentUser.IdUsuario ?? 0, ct);
        if (lote is null) return Array.Empty<OperacionPendienteDto>();

        // EnCurso = quedó a medio escanear. Finalizada = pasó a cobro pero no se emitió el
        // comprobante (se puede facturar tal cual, no se puede seguir editando).
        // Las operaciones vacías se ignoran: son las que deja el solo hecho de identificar un cliente.
        var ops = await _db.Operaciones.AsNoTracking()
            .Where(o => o.IdSucursal == idSucursal && o.IdCaja == idCaja && o.IdLote == lote.IdLote
                && o.IdCliente == idCliente
                && (o.Estado == EstadoOperacion.EnCurso || o.Estado == EstadoOperacion.Finalizada)
                && o.Detalles.Any())
            .OrderByDescending(o => o.IdOperacion)
            .Select(o => new { o.IdOperacion, o.CreatedAtUtc, o.Estado, Lineas = o.Detalles.Count, o.Total })
            .Take(10)
            .ToListAsync(ct);

        // SpecifyKind(Utc) es necesario: el valor viene de SQL sin Kind, y sin él se serializa sin
        // la "Z" final, así que el navegador lo interpreta como hora local y muestra la hora
        // corrida (+3 h en Argentina).
        return ops.Select(o => new OperacionPendienteDto(
            o.IdOperacion, DateTime.SpecifyKind(o.CreatedAtUtc, DateTimeKind.Utc),
            o.Estado.ToString(), o.Lineas, o.Total)).ToList();
    }

    public async Task<OperacionDto> CrearOperacionAsync(CrearOperacionRequest req, CancellationToken ct = default)
    {
        await AsegurarCajaAsync(req.IdSucursal, req.IdCaja, paraApertura: false, ct);

        var lote = await ObtenerLoteAbiertoHoyAsync(req.IdSucursal, req.IdCaja, _currentUser.IdUsuario ?? 0, ct)
            ?? throw new DomainException("SIN_LOTE_ABIERTO", "No hay un lote de caja abierto. Abra la caja primero.");

        if (req.IdCliente is int idc && !await _db.Clientes.AnyAsync(c => c.IdCliente == idc && c.Activo, ct))
            throw new DomainException("CLIENTE_INEXISTENTE", "El cliente no existe o está inactivo.");

        // Lock por sucursal: dos cajas distintas de la misma sucursal creando una operación al
        // mismo tiempo no deben poder calcular el mismo próximo IdOperacion (PK compuesta, sin
        // IDENTITY).
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await RecursoLockHelper.AdquirirAsync(_db, $"Operacion:{req.IdSucursal}", ct);

        var next = (await _db.Operaciones.Where(o => o.IdSucursal == req.IdSucursal)
            .Select(o => o.IdOperacion).MaxAsync(x => (int?)x, ct) ?? 0) + 1;

        var op = new Operacion
        {
            IdSucursal = req.IdSucursal, IdOperacion = next, IdCliente = req.IdCliente,
            IdCaja = req.IdCaja, IdLote = lote.IdLote,
            Estado = EstadoOperacion.EnCurso, Total = 0, DescuentoTotal = 0
        };
        _db.Operaciones.Add(op);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await MapOperacionAsync(op, ct);
    }

    public async Task<OperacionDto?> ObtenerOperacionAsync(int idSucursal, int idOperacion, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var op = await _db.Operaciones.Include(o => o.Detalles)
            .FirstOrDefaultAsync(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion, ct);
        return op is null ? null : await MapOperacionAsync(op, ct);
    }

    public async Task<OperacionDto?> AgregarLineaAsync(int idSucursal, int idOperacion, AgregarLineaRequest req, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        // Mismo lock que AnularLineaAsync/CambiarCantidadLineaAsync (ver comentario ahí): la cola de
        // escaneo puede disparar un Agregar casi al mismo tiempo que el cajero anula/cambia otra
        // línea de la misma operación.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await RecursoLockHelper.AdquirirAsync(_db, $"Operacion:{idSucursal}:{idOperacion}", ct);

        var op = await _db.Operaciones.Include(o => o.Detalles)
            .FirstOrDefaultAsync(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion, ct);
        if (op is null) return null;
        if (op.Estado != EstadoOperacion.EnCurso)
            throw new DomainException("OPERACION_CERRADA", "La operación ya fue finalizada o anulada.");
        if (req.Cantidad <= 0)
            throw new DomainException("CANTIDAD_INVALIDA", "La cantidad debe ser mayor a cero.");

        var presentacion = await _db.Presentaciones.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdPresentacion == req.IdPresentacion, ct)
            ?? throw new DomainException("PRESENTACION_INEXISTENTE", "La presentación no existe.");

        var precio = await _pricing.ResolverPrecioAsync(
            new ResolverPrecioRequest(idSucursal, req.IdPresentacion, op.IdCliente), ct);
        if (!precio.Encontrado)
            throw new DomainException("SIN_PRECIO", "El artículo no tiene precio vigente en esta sucursal.");

        var precioUnit = precio.TieneConvenio ? precio.PrecioConvenio : precio.PrecioVigente;

        // Artículo repetido: se acumula en la línea que ya existe (escanear 3 veces el mismo producto
        // deja UNA fila con cantidad 3) en vez de repetir filas. Se exige el mismo precio unitario:
        // si por algún motivo se resolvió distinto, va en una línea aparte para no perder el importe.
        var existente = op.Detalles.FirstOrDefault(d => d.IdPresentacion == req.IdPresentacion && d.Precio == precioUnit);

        // Ofertas: se recalculan sobre TODAS las líneas vigentes de la operación (SRS: recálculo por
        // cada nuevo artículo). El orden de esta lista es el mismo en el que se reasignan los
        // descuentos más abajo, así que se fija explícitamente.
        var vivas = op.Detalles.OrderBy(d => d.IdDetalleOperacion).ToList();
        var lineasPendientes = vivas
            .Select(d => new LineaOfertaRequest(d.IdPresentacion,
                ReferenceEquals(d, existente) ? d.Cantidad + req.Cantidad : d.Cantidad, d.Precio))
            .ToList();
        if (existente is null)
            lineasPendientes.Add(new LineaOfertaRequest(req.IdPresentacion, req.Cantidad, precioUnit));

        var resultado = await _pricing.AplicarOfertasAsync(
            new AplicarOfertasRequest(idSucursal, op.IdCliente, lineasPendientes), ct);

        if (existente is not null)
        {
            existente.Cantidad += req.Cantidad;
        }
        else
        {
            var detalle = new DetalleOperacion
            {
                IdSucursal = idSucursal, IdOperacion = idOperacion, IdPresentacion = req.IdPresentacion,
                Cantidad = req.Cantidad, Precio = precioUnit, IdListaPrecio = precio.IdListaPrecio
            };
            _db.DetallesOperaciones.Add(detalle);
            vivas.Add(detalle);
        }

        // El descuento se reasigna a TODAS las líneas, no solo a la última: una oferta por cantidad
        // puede cambiar el descuento de líneas anteriores al sumar este artículo (antes se guardaba
        // solo el de la nueva, así que la suma de las líneas podía no dar el total de la operación).
        for (int i = 0; i < vivas.Count; i++)
        {
            vivas[i].Descuento = resultado.Lineas[i].Descuento;
            vivas[i].OfertasAplicadas = JsonSerializer.Serialize(resultado.Lineas[i].Ofertas.Select(o => o.Descripcion));
            vivas[i].IdOfertaPrincipal = resultado.Lineas[i].Ofertas.FirstOrDefault()?.IdOferta;
        }

        op.Total = resultado.TotalNeto;
        op.DescuentoTotal = resultado.TotalDescuento;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var reloaded = await _db.Operaciones.Include(o => o.Detalles)
            .FirstAsync(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion, ct);
        return await MapOperacionAsync(reloaded, ct);
    }

    public async Task<OperacionDto?> AnularLineaAsync(int idSucursal, int idOperacion, long idDetalle,
        string? codigoSupervisor = null, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        await _supervisorAuth.ExigirAsync(codigoSupervisor, ct);

        // Lock por operación: agregar/anular/cambiar cantidad recalculan y reescriben TODAS las
        // líneas vigentes (no solo la tocada). Sin este lock, dos de estas acciones disparadas casi
        // en simultáneo sobre la misma operación (ej. la cola de escaneo agregando un artículo
        // mientras el cajero anula otra línea) pueden pisarse: una ya borró/actualizó una línea que
        // la otra todavía tiene cargada en memoria, y el SaveChanges de la segunda intenta un UPDATE
        // sobre una fila que ya no existe → DbUpdateConcurrencyException ("affected 0 row(s)").
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await RecursoLockHelper.AdquirirAsync(_db, $"Operacion:{idSucursal}:{idOperacion}", ct);

        var op = await _db.Operaciones.Include(o => o.Detalles)
            .FirstOrDefaultAsync(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion, ct);
        if (op is null) return null;
        if (op.Estado != EstadoOperacion.EnCurso)
            throw new DomainException("OPERACION_CERRADA", "La operación ya fue finalizada o anulada.");

        var linea = op.Detalles.FirstOrDefault(d => d.IdDetalleOperacion == idDetalle)
            ?? throw new DomainException("LINEA_INEXISTENTE", "La línea no existe en esta operación.");
        // Se marca el borrado en el change tracker pero NO se persiste todavía: el recálculo de
        // ofertas/totales de abajo se hace en memoria y todo se graba en un único SaveChanges al
        // final (atómico: si algo falla en el medio, no queda la línea borrada con el total viejo).
        _db.DetallesOperaciones.Remove(linea);

        // Recalcular ofertas y totales con las líneas restantes.
        var restantes = op.Detalles.Where(d => d.IdDetalleOperacion != idDetalle)
            .Select(d => new LineaOfertaRequest(d.IdPresentacion, d.Cantidad, d.Precio)).ToList();

        if (restantes.Count == 0)
        {
            op.Total = 0; op.DescuentoTotal = 0;
        }
        else
        {
            var resultado = await _pricing.AplicarOfertasAsync(new AplicarOfertasRequest(idSucursal, op.IdCliente, restantes), ct);
            var vivas = op.Detalles.Where(d => d.IdDetalleOperacion != idDetalle).ToList();
            for (int i = 0; i < vivas.Count; i++)
            {
                vivas[i].Descuento = resultado.Lineas[i].Descuento;
                vivas[i].OfertasAplicadas = JsonSerializer.Serialize(resultado.Lineas[i].Ofertas.Select(o => o.Descripcion));
                vivas[i].IdOfertaPrincipal = resultado.Lineas[i].Ofertas.FirstOrDefault()?.IdOferta;
            }
            op.Total = resultado.TotalNeto;
            op.DescuentoTotal = resultado.TotalDescuento;
        }
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var reloaded = await _db.Operaciones.Include(o => o.Detalles)
            .FirstAsync(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion, ct);
        return await MapOperacionAsync(reloaded, ct);
    }

    public async Task<OperacionDto?> CambiarCantidadLineaAsync(int idSucursal, int idOperacion, long idDetalle,
        decimal cantidad, string? codigoSupervisor = null, CancellationToken ct = default)
    {
        // Bajar a 0 es sacar el artículo: se reusa la anulación (que ya recalcula, persiste y pide
        // el mismo control de supervisor).
        if (cantidad <= 0) return await AnularLineaAsync(idSucursal, idOperacion, idDetalle, codigoSupervisor, ct);

        _currentUser.AsegurarSucursal(idSucursal);

        // Mismo lock que AnularLineaAsync (ver comentario ahí): evita el UPDATE sobre una línea que
        // otra acción concurrente sobre la misma operación ya borró/modificó.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await RecursoLockHelper.AdquirirAsync(_db, $"Operacion:{idSucursal}:{idOperacion}", ct);

        var op = await _db.Operaciones.Include(o => o.Detalles)
            .FirstOrDefaultAsync(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion, ct);
        if (op is null) return null;
        if (op.Estado != EstadoOperacion.EnCurso)
            throw new DomainException("OPERACION_CERRADA", "La operación ya fue finalizada o anulada.");

        var linea = op.Detalles.FirstOrDefault(d => d.IdDetalleOperacion == idDetalle)
            ?? throw new DomainException("LINEA_INEXISTENTE", "La línea no existe en esta operación.");

        linea.Cantidad = cantidad;

        // Mismo recálculo que al agregar: una oferta por cantidad puede cambiar el descuento de
        // otras líneas, así que se reasigna sobre todas.
        var vivas = op.Detalles.OrderBy(d => d.IdDetalleOperacion).ToList();
        var resultado = await _pricing.AplicarOfertasAsync(new AplicarOfertasRequest(idSucursal, op.IdCliente,
            vivas.Select(d => new LineaOfertaRequest(d.IdPresentacion, d.Cantidad, d.Precio)).ToList()), ct);

        for (int i = 0; i < vivas.Count; i++)
        {
            vivas[i].Descuento = resultado.Lineas[i].Descuento;
            vivas[i].OfertasAplicadas = JsonSerializer.Serialize(resultado.Lineas[i].Ofertas.Select(o => o.Descripcion));
            vivas[i].IdOfertaPrincipal = resultado.Lineas[i].Ofertas.FirstOrDefault()?.IdOferta;
        }
        op.Total = resultado.TotalNeto;
        op.DescuentoTotal = resultado.TotalDescuento;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await MapOperacionAsync(op, ct);
    }

    public async Task<OperacionDto?> FinalizarOperacionAsync(int idSucursal, int idOperacion, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var op = await _db.Operaciones.Include(o => o.Detalles)
            .FirstOrDefaultAsync(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion, ct);
        if (op is null) return null;
        if (op.Estado != EstadoOperacion.EnCurso)
            throw new DomainException("OPERACION_CERRADA", "La operación ya fue finalizada o anulada.");
        if (op.Detalles.Count == 0)
            throw new DomainException("OPERACION_VACIA", "No se puede finalizar una operación sin artículos.");

        op.Estado = EstadoOperacion.Finalizada;
        await _db.SaveChangesAsync(ct);
        return await MapOperacionAsync(op, ct);
    }

    public async Task<OperacionDto?> ReabrirOperacionAsync(int idSucursal, int idOperacion, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);
        var op = await _db.Operaciones.Include(o => o.Detalles)
            .FirstOrDefaultAsync(o => o.IdSucursal == idSucursal && o.IdOperacion == idOperacion, ct);
        if (op is null) return null;
        // Solo se puede volver atrás desde Finalizada (esperando cobro). Si ya se facturó o anuló
        // no hay nada que reabrir: el error "OPERACION_CERRADA" es correcto en ese caso.
        if (op.Estado != EstadoOperacion.Finalizada)
            throw new DomainException("OPERACION_CERRADA", "La operación ya fue finalizada o anulada.");

        op.Estado = EstadoOperacion.EnCurso;
        await _db.SaveChangesAsync(ct);
        return await MapOperacionAsync(op, ct);
    }

    public Task<RedondeoDto> CalcularRedondeoAsync(decimal total, CancellationToken ct = default)
    {
        // TODO Fase 4: leer Configuraciones.RangoRedondeo en vez de la constante.
        var ajuste = RedondeoService.CalcularAjuste(total, 1m);
        return Task.FromResult(new RedondeoDto(ajuste, total + ajuste));
    }

    // ---------- Mapeo ----------

    private async Task<OperacionDto> MapOperacionAsync(Operacion op, CancellationToken ct)
    {
        var idsPres = op.Detalles.Select(d => d.IdPresentacion).Distinct().ToList();
        var info = await (
            from pr in _db.Presentaciones.AsNoTracking().Where(p => idsPres.Contains(p.IdPresentacion))
            join a in _db.Articulos.AsNoTracking() on pr.IdArticulo equals a.IdArticulo
            select new { pr.IdPresentacion, a.CodigoInterno, a.Descripcion }
        ).ToDictionaryAsync(x => x.IdPresentacion, ct);

        string? clienteDesc = null;
        if (op.IdCliente is int idc)
            clienteDesc = await _db.Clientes.AsNoTracking().Where(c => c.IdCliente == idc)
                .Select(c => c.Descripcion).FirstOrDefaultAsync(ct);

        // Listas de origen de los precios cobrados: una sola consulta para todas las líneas (no una
        // por línea), así la caja puede destacar los precios que vienen de un folder.
        var idsListas = op.Detalles.Where(d => d.IdListaPrecio != null)
            .Select(d => d.IdListaPrecio!.Value).Distinct().ToList();
        var listas = idsListas.Count == 0
            ? new Dictionary<int, (string Codigo, TipoListaPrecio Tipo)>()
            : (await _db.ListasPrecios.AsNoTracking()
                .Where(l => idsListas.Contains(l.IdListaPrecio))
                .Select(l => new { l.IdListaPrecio, l.CodigoInterno, l.Tipo })
                .ToListAsync(ct))
                .ToDictionary(l => l.IdListaPrecio, l => (Codigo: l.CodigoInterno, Tipo: l.Tipo));

        // Último escaneado arriba: es el orden que espera el cajero en pantalla. El comprobante no
        // usa este DTO (lee op.Detalles directo), así que el detalle fiscal no se ve afectado.
        var lineas = op.Detalles.OrderByDescending(d => d.IdDetalleOperacion).Select(d =>
        {
            var i = info.GetValueOrDefault(d.IdPresentacion);
            var bruto = d.Precio * d.Cantidad;
            var ofertas = string.IsNullOrWhiteSpace(d.OfertasAplicadas)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(d.OfertasAplicadas!) ?? new List<string>();
            var tieneLista = d.IdListaPrecio is int idl && listas.TryGetValue(idl, out var lista);
            var datosLista = tieneLista ? listas[d.IdListaPrecio!.Value] : default;
            return new OperacionLineaDto(d.IdDetalleOperacion, d.IdPresentacion,
                i?.CodigoInterno ?? "", i?.Descripcion ?? "", d.Cantidad, d.Precio,
                bruto, d.Descuento, bruto - d.Descuento, ofertas,
                tieneLista ? datosLista.Codigo : null,
                tieneLista && datosLista.Tipo == TipoListaPrecio.Folder);
        }).ToList();

        // Vista previa de percepciones: se recalcula en cada consulta de la operación (no se
        // persiste en el carrito) para que el cajero vea, ANTES de cobrar, cuánto va a sumar el
        // padrón de IIBB/IVA — el cálculo definitivo (autoritativo) se repite en Facturación al
        // emitir, por si algo cambió (padrón importado de nuevo, mínimo editado) entre medio.
        var percepciones = await _percepciones.CalcularAsync(op.IdSucursal, op.IdCliente,
            op.Detalles.Select(d => new LineaParaPercepcion(d.IdPresentacion, d.Cantidad, d.Precio, d.Descuento, d.IdListaPrecio)).ToList(), ct);

        return new OperacionDto(op.IdSucursal, op.IdOperacion, op.IdCliente, clienteDesc,
            op.Estado.ToString(), lineas, lineas.Sum(l => l.Bruto), op.DescuentoTotal, op.Total,
            percepciones.PercepcionIva21, percepciones.PercepcionIva105, percepciones.PercepcionIibb,
            op.Total + percepciones.Total, percepciones.AlicuotaIibb);
    }
}
