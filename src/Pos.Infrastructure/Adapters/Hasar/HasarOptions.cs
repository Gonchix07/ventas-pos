namespace Pos.Infrastructure.Adapters.Hasar;

/// <summary>Una impresora fiscal 2G asociada a una caja concreta.</summary>
public class HasarImpresoraOptions
{
    public int IdSucursal { get; set; }
    public int IdCaja { get; set; }
    /// <summary>IP del equipo en la LAN. La impresora es un nodo de red, no un periférico local.</summary>
    public string Host { get; set; } = "";
    public int Puerto { get; set; } = 80;
}

public class HasarOptions
{
    public List<HasarImpresoraOptions> Impresoras { get; set; } = new();

    /// <summary>Timeout de cada request HTTP contra el equipo.</summary>
    public int TimeoutMs { get; set; } = 15000;

    /// <summary>
    /// Cuánto esperar entre reintentos mientras el controlador responde <c>ControladorOcupado</c>
    /// (está imprimiendo). No es un error: es el modo normal de decir "todavía no terminé".
    /// </summary>
    public int EsperaOcupadoMs { get; set; } = 400;

    /// <summary>Techo de reintentos ante ControladorOcupado (un cierre Z tarda varios segundos).</summary>
    public int MaxReintentosOcupado { get; set; } = 60;

    /// <summary>
    /// Carpeta donde se persiste el último número de secuencia usado por cada impresora.
    /// Ver <see cref="SecuenciaStore"/> para por qué esto no puede vivir sólo en memoria.
    /// </summary>
    public string EstadoDir { get; set; } = "";

    public HasarImpresoraOptions? Resolver(int idSucursal, int idCaja) =>
        Impresoras.FirstOrDefault(i => i.IdSucursal == idSucursal && i.IdCaja == idCaja);
}
