using Pos.Domain.Common;

namespace Pos.Domain.Entities;

/// <summary>
/// Historial de una corrección retroactiva sobre los datos de cupón de un MovimientoPago de tarjeta
/// (número de cupón, número de lote de tarjeta, plan de cuotas) — el cajero puede tipear mal alguno
/// al cobrar, y esto se corrige después desde Tesorería/Supervisor sin perder rastro de qué decía
/// antes. Guarda una FOTO completa de los 3 campos antes y después (no fila por campo): estos tres
/// datos casi siempre se corrigen juntos y una foto completa es más simple de leer que reconstruir
/// el estado anterior cruzando varias filas.
///
/// Deliberadamente no toca MovimientoPago directamente desde acá: CuponesService actualiza el
/// MovimientoPago Y agrega esta fila en la misma operación (ver CuponesService.CorregirAsync).
/// </summary>
public class CorreccionCupon : AuditableEntity
{
    public long IdCorreccionCupon { get; set; }
    public long IdMovPagos { get; set; }

    public string? NumeroCuponAnterior { get; set; }
    public string? NumeroLoteAnterior { get; set; }
    public int? IdPlanCuotaAnterior { get; set; }

    public string? NumeroCuponNuevo { get; set; }
    public string? NumeroLoteNuevo { get; set; }
    public int? IdPlanCuotaNuevo { get; set; }

    public int IdUsuario { get; set; }
    public DateTime Fecha { get; set; }
    public string Motivo { get; set; } = "";
}
