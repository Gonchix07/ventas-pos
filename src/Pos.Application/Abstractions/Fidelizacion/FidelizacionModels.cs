namespace Pos.Application.Abstractions.Fidelizacion;

/// <summary>Datos de una Factura de venta para sumar puntos en puntos-app. Dni identifica al
/// cliente allá (Cliente.Documento de pos-mayorista); FacturaNumero se manda con el prefijo de
/// sucursal (ver FacturacionService) porque puntos-app exige unicidad global de factura_numero,
/// y dos sucursales pueden compartir numeración de comprobante.</summary>
public record CargaPuntosFidelizacion(string Dni, decimal FacturaPesos, string FacturaNumero);

/// <summary>Resultado de intentar sumar puntos. <c>Ok=false</c> cubre tanto "no se intentó" (la
/// integración está deshabilitada/sin configurar) como "se intentó y falló" (tarjeta inexistente,
/// puntos-app inalcanzable, etc.) — en ambos casos <see cref="Error"/> puede venir null (caso
/// silencioso, best-effort, no hay nada que mostrarle al cajero) o con un mensaje. El llamador
/// (FacturacionService) nunca debe tratar <c>Ok=false</c> como una falla de la venta: la venta ya
/// se facturó antes de llegar acá.</summary>
public record ResultadoCargaPuntos(bool Ok, string? Cliente, decimal? PuntosOtorgados,
    decimal? PuntosTotales, string? Error);

/// <summary>Una campaña de descuento vigente en puntos-app para el cliente/local consultados —
/// ver GET /api/campanias de puntos-app. <c>Local</c> es el nombre del local al que está
/// restringida, o "General" si aplica a cualquier local.</summary>
public record CampaniaVigente(string Id, string Nombre, string? Descripcion, decimal DescuentoPorcentaje,
    string Local, DateOnly? FechaDesde);

/// <summary>Resultado de consultar campañas vigentes. <c>Ok=false</c> cubre tanto "no se intentó"
/// (integración deshabilitada/sin configurar) como "se intentó y falló" (DNI no encontrado en
/// puntos-app, timeout, etc.) — best-effort, mismo criterio que <see cref="ResultadoCargaPuntos"/>:
/// nunca debe bloquear la identificación del cliente en Caja, es solo un dato informativo extra.</summary>
public record ResultadoCampanias(bool Ok, IReadOnlyList<CampaniaVigente> Campanias, string? Error);

/// <summary>Resultado de registrar el USO de una campaña — ver POST /api/campanias de puntos-app.
/// Best-effort, mismo criterio que el resto de esta integración: nunca debe afectar una venta ya
/// facturada. <c>Ok=false</c> no se le muestra al cajero, solo se loguea (la periodicidad de la
/// campaña en puntos-app puede quedar sin marcar ese uso puntual, pero es preferible eso a bloquear
/// o revertir una venta ya fiscal).</summary>
public record ResultadoUsoCampania(bool Ok, string? Error);

/// <summary>
/// Puerto hacia el API de puntos-app (programa de fidelización externo, proyecto aparte) —
/// <c>POST /api/cargar-puntos</c>. Best-effort A PROPÓSITO, mismo criterio que
/// <see cref="Pos.Application.Abstractions.Interfase.IInterfaseContableService"/>: la integración
/// deshabilitada, sin configurar, o inalcanzable NUNCA debe impedir ni revertir una venta ya
/// facturada — ver <see cref="Pos.Infrastructure.Services.PuntosFidelizacionService"/>. A
/// diferencia de la interfase contable, acá SÍ devuelve un resultado (nunca lanza) porque el
/// frontend lo usa para mostrarle al cajero si sumó puntos.
///
/// Alcance actual: solo Facturas de venta (no Presupuesto). Las Notas de Crédito NO restan puntos
/// todavía — puntos-app no expone un endpoint de reversión de carga; queda pendiente agregarlo allá
/// antes de poder cubrir NC acá.
/// </summary>
public interface IPuntosFidelizacionService
{
    Task<ResultadoCargaPuntos> CargarPuntosAsync(CargaPuntosFidelizacion carga, CancellationToken ct = default);

    /// <summary>Campañas vigentes del cliente (por DNI) restringidas a este local (el <c>Comercio</c>
    /// configurado en ConexionPuntosApp) + las generales — GET /api/campanias?dni=...&amp;local=...
    /// Se usa tanto para mostrar el descuento en el globo violeta de Caja como para resolver el %
    /// que efectivamente se aplica al precio (ver CajaService.CrearOperacionAsync).</summary>
    Task<ResultadoCampanias> ConsultarCampaniasAsync(string dni, CancellationToken ct = default);

    /// <summary>Registra en puntos-app que la campaña <paramref name="idCampania"/> se usó de verdad
    /// en una venta ya facturada — POST /api/campanias. Hay que llamarlo SIEMPRE que la campaña se
    /// haya aplicado al precio (Operacion.IdCampaniaPuntos no nulo), para que la periodicidad
    /// (diaria/semanal/mensual) de esa campaña se cumpla del lado de puntos-app — sin este llamado,
    /// el cliente podría "reusar" una campaña de una sola vez por día indefinidamente.</summary>
    Task<ResultadoUsoCampania> RegistrarUsoCampaniaAsync(string idCampania, string dni, CancellationToken ct = default);
}
