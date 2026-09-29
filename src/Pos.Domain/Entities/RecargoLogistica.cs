using Pos.Domain.Common;

namespace Pos.Domain.Entities;

/// <summary>
/// Tramo de recargo logístico importado de recargo_logistica.dbf (app legacy VFP "Mayorista",
/// S:\Mayorista\Datos) — tabla de tramos por rango de importe: un pedido cuyo importe cae entre
/// <see cref="Inicio"/> y <see cref="Fin"/> paga <see cref="Porcentaje"/>% de recargo logístico.
/// <see cref="Fin"/> = -1 significa "sin límite superior" (mismo sentinel que usa el DBF de origen).
/// Se reimporta bajo demanda desde el DBF (ver RecargoLogisticaService.ImportarAsync) — no hay
/// sincronización automática, es una tabla de tarifas que cambia poco.
/// </summary>
public class RecargoLogistica : AuditableEntity
{
    public int IdRecargoLogistica { get; set; }
    public decimal Inicio { get; set; }
    public decimal Fin { get; set; }
    public decimal Porcentaje { get; set; }
}
