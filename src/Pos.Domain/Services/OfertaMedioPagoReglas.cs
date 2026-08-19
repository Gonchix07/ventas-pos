namespace Pos.Domain.Services;

/// <summary>Definición liviana de una oferta por medio de pago, para lógica pura (sin EF). Ver
/// Pos.Domain.Entities.OfertaMedioPago para el detalle de cada campo.</summary>
public record OfertaMedioPagoDef(int IdMedioPago, int? IdPlanCuota, decimal Porcentaje, decimal TopeMaximo);

/// <summary>
/// Descuento por medio de pago: % sobre el monto del pago, topeado a un máximo fijo en $. Se aplica
/// en la pantalla de cobro (no en el carrito) — ver FacturacionService.EmitirAsync. Lógica pura,
/// sin acceso a datos, misma idea que RedondeoService/MotorOfertas.
/// </summary>
public static class OfertaMedioPagoReglas
{
    public static decimal CalcularDescuento(decimal monto, decimal porcentaje, decimal topeMaximo)
    {
        if (monto <= 0 || porcentaje <= 0) return 0m;
        var descuento = Math.Round(monto * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
        return topeMaximo > 0 ? Math.Min(descuento, topeMaximo) : descuento;
    }

    /// <summary>
    /// La oferta que corresponde a un medio+plan de cuotas: la que apunta EXACTAMENTE a ese plan
    /// gana por sobre la que aplica a cualquier cantidad de cuotas del mismo medio (IdPlanCuota
    /// null). Si el pago no es con plan (no es tarjeta, o no se dio uno), solo matchea la general.
    /// </summary>
    public static OfertaMedioPagoDef? Resolver(IReadOnlyList<OfertaMedioPagoDef> ofertas, int idMedioPago, int? idPlan)
    {
        var delMedio = ofertas.Where(o => o.IdMedioPago == idMedioPago).ToList();
        return delMedio.FirstOrDefault(o => o.IdPlanCuota == idPlan)
            ?? (idPlan != null ? delMedio.FirstOrDefault(o => o.IdPlanCuota == null) : null);
    }
}
