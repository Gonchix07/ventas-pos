namespace Pos.Domain.Entities;

/// <summary>
/// Marca de agua de la última sincronización exitosa contra el ERP Central, una fila por fuente
/// (<see cref="Fuente"/>). El proceso Pos.ErpSync la lee para decidir si ya toca correr de nuevo
/// (comparando <see cref="UltimaCorridaUtc"/> contra la frecuencia configurada) y desde dónde seguir
/// (<see cref="UltimoWatermarkUtc"/>) — así una corrida interrumpida retoma justo después del último
/// lote confirmado en vez de reprocesar todo o perder cambios.
/// </summary>
public class SyncCheckpoint
{
    public int IdSyncCheckpoint { get; set; }
    /// <summary>"Lookups", "Articulos" o "Clientes" — ver ErpSyncFuentes en Pos.ErpSync.</summary>
    public string Fuente { get; set; } = "";
    public DateTime? UltimoWatermarkUtc { get; set; }
    /// <summary>Id (del ERP) de la última fila procesada EN el timestamp de <see cref="UltimoWatermarkUtc"/>.
    /// Necesario porque el ERP hace touches masivos que dejan miles de filas con el mismo
    /// fecha_modificacion exacto — paginar solo por fecha pierde filas silenciosamente cuando un lote
    /// corta en el medio de ese empate (ver comentario en IErpArticuloReader). 0 = "ninguna fila
    /// todavía en ese timestamp", el valor inicial para una fuente nueva.</summary>
    public long UltimoIdErp { get; set; }
    public DateTime? UltimaCorridaUtc { get; set; }
    /// <summary>"Ok", "Error" o "Parcial" (terminó pero con filas fallidas sueltas).</summary>
    public string? UltimoResultado { get; set; }
    public int Insertados { get; set; }
    public int Actualizados { get; set; }
    public int Errores { get; set; }
}
