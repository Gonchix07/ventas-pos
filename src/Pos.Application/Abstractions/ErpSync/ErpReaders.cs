namespace Pos.Application.Abstractions.ErpSync;

// DTOs de lectura del ERP Central (DATA_PREV). Son un espejo mínimo de las columnas que el sync
// necesita de cada tabla — no representan el modelo completo del ERP, que tiene mucho más de lo
// que a pos-mayorista le importa.

public record ErpSectorRow(string Codigo, string Descripcion);
public record ErpLineaRow(string Codigo, string Descripcion);
public record ErpFamiliaRow(string Codigo, string Descripcion, string? CodigoSectorErp);
public record ErpModoIvaRow(string Codigo, string Descripcion, decimal Alicuota, decimal Percepcion);
public record ErpCondicionIvaRow(string Codigo, string Descripcion);

/// <summary>Snapshot completo de los lookups chicos del ERP (Sectores/Líneas/Familias/ModoIVA/CondIVA).
/// Se traen enteros en cada corrida — no hay watermark, no vale la pena para tablas de este tamaño.</summary>
public interface IErpLookupReader
{
    Task<IReadOnlyList<ErpSectorRow>> GetSectoresAsync(CancellationToken ct);
    Task<IReadOnlyList<ErpLineaRow>> GetLineasAsync(CancellationToken ct);
    Task<IReadOnlyList<ErpFamiliaRow>> GetFamiliasAsync(CancellationToken ct);
    Task<IReadOnlyList<ErpModoIvaRow>> GetModosIvaAsync(CancellationToken ct);
    Task<IReadOnlyList<ErpCondicionIvaRow>> GetCondicionesIvaAsync(CancellationToken ct);
}

public record ErpArticuloRow(
    long IdErp, string Codigo, string DescripcionFull, int Estado, DateTime FechaModificacion,
    string CodigoSectorErp, string CodigoLineaErp, string? CodigoFamiliaErp, string CodigoModoIvaErp,
    decimal UnidadBulto);

public record ErpPresentacionRow(
    long IdErp, long IdArticuloErp, string? Codigo, decimal Fraccion, bool Baja, DateTime FechaModificacion);

public record ErpCodBarraRow(
    long IdErp, long IdPresentacionErp, string Codigo, string TipoCodigo, DateTime FechaModificacion);

/// <summary>Artículos + presentaciones + códigos de barra del ERP, filtrados por fecha_modificacion.
/// Las tres tablas comparten el mismo watermark de corrida (ver ErpSyncFuentes.Articulos): un cambio
/// en cualquiera de las tres cuenta como "el artículo cambió".
/// <para>
/// El watermark es compuesto (fecha, id), no solo fecha: el ERP hace touches masivos que dejan miles
/// de filas con el MISMO fecha_modificacion exacto (visto en producción: 38.750 artículos con el
/// mismo timestamp). Paginar solo por fecha con `TOP N ... WHERE fecha > @watermark` es un bug real
/// — SQL Server no garantiza qué subconjunto de esas filas empatadas devuelve cada corrida, así que
/// avanzar el watermark a esa fecha después de procesar solo ALGUNAS de ellas las pierde para
/// siempre (las que quedaron afuera nunca vuelven a matchear "> watermark"). Por eso cada método
/// recibe también el id de la última fila ya procesada EN ESE MISMO timestamp, y ordena/filtra por
/// el par (fecha, id) en vez de fecha sola.
/// </para></summary>
public interface IErpArticuloReader
{
    Task<IReadOnlyList<ErpArticuloRow>> GetArticulosModificadosAsync(DateTime watermark, long ultimoIdErp, int loteSize, CancellationToken ct);
    Task<IReadOnlyList<ErpPresentacionRow>> GetPresentacionesModificadasAsync(DateTime watermark, long ultimoIdErp, int loteSize, CancellationToken ct);
    Task<IReadOnlyList<ErpCodBarraRow>> GetCodBarrasModificadosAsync(DateTime watermark, long ultimoIdErp, int loteSize, CancellationToken ct);
}

public record ErpClienteRow(
    long IdErp, string Codigo, string RazonSocial, string? NombreFantasia, string? Domicilio,
    string? Localidad, string? Cuit, int Estado, string? Email, string? Telefono, string CodigoCondIvaErp,
    DateTime FechaActualizacion);

/// <summary>
/// El maestro legacy T_Clientes no tiene fecha de modificación propia (ver memoria del proyecto), así
/// que el "qué cambió" se detecta en la tabla normalizada cli.cliente (fecha_actualizacion) cruzando
/// por mig.map_cliente para volver al código de T_Clientes de donde sale el resto de los datos.
/// Mismo watermark compuesto (fecha, id) que <see cref="IErpArticuloReader"/> y por el mismo motivo:
/// cli.cliente también trae touches masivos con timestamps repetidos.
/// </summary>
public interface IErpClienteReader
{
    Task<IReadOnlyList<ErpClienteRow>> GetClientesModificadosAsync(DateTime watermark, long ultimoIdErp, int loteSize, CancellationToken ct);
}
