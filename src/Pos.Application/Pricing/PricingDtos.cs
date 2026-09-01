namespace Pos.Application.Pricing;

// ---- Precio ----
/// <param name="PorcentajeCampania">% de descuento de una campaña de puntos-app ya resuelta para
/// este cliente/local (0 = ninguna). Se pasa desde afuera en vez de resolverse acá porque implica
/// una llamada HTTP externa — Caja la resuelve UNA vez por operación, no en cada línea.</param>
public record ResolverPrecioRequest(int IdSucursal, int IdPresentacion, int? IdCliente,
    decimal PorcentajeCampania = 0m);
/// <param name="TieneConvenio">
/// El convenio del cliente se APLICÓ a este precio. Es false cuando el cliente tiene convenio pero el
/// precio sale de una lista de folder, que no acumula con convenios.
/// </param>
/// <param name="PrecioBase">La lista que le corresponde a ESTE cliente (la de su tarjeta/convenio
/// propio si tiene una, si no PrecioVigente) — sobre esto se calcula el % de convenio+campaña. Es lo
/// que Caja muestra en la columna "Precio", no PrecioVigente (que es la lista genérica).</param>
public record ResolverPrecioResponse(bool Encontrado, decimal PrecioVigente, decimal ImpuestoInterno,
    decimal PrecioConvenio, bool TieneConvenio, int? IdListaPrecio = null, decimal PrecioBase = 0m);

// ---- Ofertas ----
public record LineaOfertaRequest(int IdPresentacion, decimal Cantidad, decimal PrecioUnit);
public record AplicarOfertasRequest(int IdSucursal, int? IdCliente, List<LineaOfertaRequest> Lineas);

public record OfertaAplicadaDto(int IdOferta, string Descripcion, decimal Descuento);
public record LineaOfertaResponse(int IdPresentacion, decimal Cantidad, decimal PrecioUnit,
    decimal Bruto, decimal Descuento, decimal Neto, List<OfertaAplicadaDto> Ofertas);
public record AplicarOfertasResponse(List<LineaOfertaResponse> Lineas, decimal TotalBruto,
    decimal TotalDescuento, decimal TotalNeto);

public interface IPricingService
{
    Task<ResolverPrecioResponse> ResolverPrecioAsync(ResolverPrecioRequest req, CancellationToken ct = default);
    Task<AplicarOfertasResponse> AplicarOfertasAsync(AplicarOfertasRequest req, CancellationToken ct = default);
}
