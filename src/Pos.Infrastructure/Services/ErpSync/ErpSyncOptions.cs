namespace Pos.Infrastructure.Services.ErpSync;

public class ErpSyncOptions
{
    /// <summary>Cada cuánto corresponde correr cada fuente. Pos.ErpSync se dispara seguido (vía
    /// Task Scheduler) pero cada fuente se salta a sí misma si todavía no pasó este tiempo desde su
    /// última corrida — así la frecuencia se ajusta acá, sin tocar el disparador del sistema operativo.</summary>
    public int FrecuenciaMinutos { get; set; } = 15;
    /// <summary>Filas por lote al leer del ERP. Cada lote se procesa y confirma (watermark incluido)
    /// como una unidad: si el proceso se cae a mitad de una corrida, el próximo disparo retoma desde
    /// el último lote confirmado.</summary>
    public int LoteSize { get; set; } = 500;
}

/// <summary>
/// Nombres de fuente usados como clave en <see cref="Pos.Domain.Entities.SyncCheckpoint"/>. Artículos,
/// Presentaciones y CodBarras van separados (no comparten un único watermark) porque cada tabla del
/// ERP tiene su propia fecha_modificacion: un cambio de código de barras no toca
/// T_Articulos.fecha_modificacion, así que un watermark compartido lo perdería. Se corren en este
/// orden porque Presentaciones necesita el Articulo ya sincronizado (para resolver la FK) y CodBarras
/// necesita la Presentacion ya sincronizada.
/// </summary>
public static class ErpSyncFuentes
{
    public const string Lookups = "Lookups";
    public const string Articulos = "Articulos";
    public const string Presentaciones = "Presentaciones";
    public const string CodBarras = "CodBarras";
    public const string Clientes = "Clientes";
}
