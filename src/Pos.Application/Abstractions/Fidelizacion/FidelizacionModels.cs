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
}
