namespace Pos.Application.Pricing;

// ---- Precio ----
public record ResolverPrecioRequest(int IdSucursal, int IdPresentacion, int? IdCliente);
/// <param name="TieneConvenio">
/// El convenio del cliente se APLICÓ a este precio. Es false cuando el cliente tiene convenio pero el
/// precio sale de una lista de folder, que no acumula con convenios.
/// </param>
public record ResolverPrecioResponse(bool Encontrado, decimal PrecioVigente, decimal ImpuestoInterno,
    decimal PrecioConvenio, bool TieneConvenio, int? IdListaPrecio = null);

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
