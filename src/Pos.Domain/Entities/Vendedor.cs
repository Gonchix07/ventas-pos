using Pos.Domain.Common;

namespace Pos.Domain.Entities;

/// <summary>
/// Vendedor de la app legacy VFP "Mayorista" (operator.dbf, S:\appvfp\Mayorista\Mayorista_Release\Datos),
/// importado a SQL para el módulo Preventa Mayorista — cruza contra PVENTISTA/PVentista de
/// pedidos.dbf (ver PreventaMayoristaService). Solo se traen los operadores que son vendedores
/// "reales" de venta mayorista: CARGO=6, PROV="00MAY", ESPECIAL=1, INACTIVO=0 (criterio dado por el
/// usuario). Se reimporta bajo demanda desde el DBF (ver VendedorService.ImportarAsync), no hay
/// sincronización automática.
/// </summary>
public class Vendedor : AuditableEntity
{
    public int IdVendedor { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
}
