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
    public DateTime? UltimaCorridaUtc { get; set; }
    /// <summary>"Ok", "Error" o "Parcial" (terminó pero con filas fallidas sueltas).</summary>
    public string? UltimoResultado { get; set; }
    public int Insertados { get; set; }
    public int Actualizados { get; set; }
    public int Errores { get; set; }
}
