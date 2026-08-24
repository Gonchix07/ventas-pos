namespace Pos.Application.Facturacion;

/// <summary>
/// CAEA cargado a mano para poder facturar en contingencia (ARCA inaccesible al momento de la
/// venta). El valor se obtiene con conexión (FECAEASolicitar, pedido con antelación por
/// quincena) — este ABM solo lo guarda, nunca lo pide él mismo.
/// </summary>
public record CaeaCargadoDto(int IdCaea, int IdEmpresa, int Anio, int Mes, int Orden,
    string Valor, DateTime VigenciaDesde, DateTime VigenciaHasta, bool VigenteHoy);

public record CaeaCargadoInput(int IdEmpresa, int Anio, int Mes, int Orden,
    string Valor, DateTime VigenciaDesde, DateTime VigenciaHasta);

public interface ICaeaCargadoService
{
    Task<IReadOnlyList<CaeaCargadoDto>> GetAsync(int? idEmpresa = null, CancellationToken ct = default);
    Task<int> CreateAsync(CaeaCargadoInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, CaeaCargadoInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>El CAEA cargado vigente para esa empresa en esa fecha, o null si no hay ninguno
    /// (contingencia sin nada precargado — la venta Electrónica no puede seguir). Usado por la
    /// saga de facturación cuando WSFEv1 (CAE) no responde.</summary>
    Task<CaeaCargadoDto?> BuscarVigenteAsync(int idEmpresa, DateTime fecha, CancellationToken ct = default);
}
