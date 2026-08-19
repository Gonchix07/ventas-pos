using Pos.Domain.Enums;

namespace Pos.Domain.Services;

public record MovimientoPagoPlano(int IdMedioPago, decimal Total, decimal Redondeo);
public record AcumuladoMedioPago(int IdMedioPago, decimal Total, decimal Redondeo);

/// <summary>Acumula los movimientos de pago del lote por medio de pago (base del arqueo X / cierre Z).</summary>
public static class AcumuladorPagos
{
    public static IReadOnlyList<AcumuladoMedioPago> Acumular(IEnumerable<MovimientoPagoPlano> movimientos) =>
        movimientos
            .GroupBy(m => m.IdMedioPago)
            .Select(g => new AcumuladoMedioPago(g.Key,
                Math.Round(g.Sum(x => x.Total), 4, MidpointRounding.AwayFromZero),
                Math.Round(g.Sum(x => x.Redondeo), 4, MidpointRounding.AwayFromZero)))
            .OrderBy(a => a.IdMedioPago)
            .ToList();
}

public record ResultadoDiferencia(decimal Diferencia, bool RequiereMotivo);

/// <summary>
/// Compara lo declarado por el cajero al cierre contra lo esperado según el sistema.
/// SRS: "Carga de justificación de diferencias y observaciones" — sólo si hay diferencia real.
/// </summary>
public static class DiferenciaCierreReglas
{
    private const decimal Tolerancia = 0.01m;

    public static ResultadoDiferencia Evaluar(decimal declarado, decimal esperado)
    {
        var diferencia = Math.Round(declarado - esperado, 4, MidpointRounding.AwayFromZero);
        return new ResultadoDiferencia(diferencia, Math.Abs(diferencia) > Tolerancia);
    }
}

/// <summary>
/// Regla SRS: "Confirmación de cierre, no se puede anular" — el cierre Z es irreversible.
/// Un lote sólo puede cerrarse si está Abierto; una vez Cerrado, no admite otro cierre ni reapertura.
/// </summary>
public static class CierreLoteReglas
{
    public static bool PuedeCerrarse(EstadoLote estado) => estado == EstadoLote.Abierto;

    /// <summary>
    /// Un lote "pendiente" es el que quedó Abierto en un día ANTERIOR al actual. El cajero ya no
    /// puede cerrarlo — arqueo X y cierre Z solo operan sobre el lote de hoy (ver FASE-5) — así que
    /// queda sin cierre Z para siempre a menos que lo regularice Tesorería/Administración.
    /// El lote del día en curso se excluye a propósito: ese lo cierra su cajero con el Z normal.
    /// </summary>
    public static bool EsLotePendienteDeDiaAnterior(EstadoLote estado, DateTime fechaAperturaUtc, DateTime ahoraUtc) =>
        estado == EstadoLote.Abierto && fechaAperturaUtc.Date < ahoraUtc.Date;
}
