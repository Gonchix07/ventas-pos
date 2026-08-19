namespace Pos.Infrastructure.Adapters.Hasar;

/// <summary>
/// Guarda en disco el último número de secuencia usado contra cada impresora fiscal.
///
/// El protocolo 2G es idempotente por secuencia: si se reenvía un comando con la MISMA
/// <c>&lt;Secuencia&gt;</c> que el anterior, el equipo devuelve la respuesta que ya tenía guardada
/// en vez de volver a ejecutarlo. Eso es lo que permite reintentar sin riesgo de imprimir dos
/// veces ante un timeout de red.
///
/// La contracara es peligrosa y es la razón de que esto se persista: si el proceso reinicia y el
/// contador vuelve a arrancar en un valor que coincide con el de la última operación enviada, el
/// primer comando después del reinicio devuelve la respuesta cacheada y **el comprobante nunca se
/// emite, en silencio** — con la API convencida de que sí. Un contador sólo en memoria hace que
/// eso pase cada vez que se reinicia la API con el número justo.
/// </summary>
public class SecuenciaStore
{
    private readonly string _dir;
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _cache = new();

    public SecuenciaStore(string dir)
    {
        _dir = string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(AppContext.BaseDirectory, "hasar-estado")
            : dir;
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Devuelve el próximo número de secuencia a usar, ya persistido.</summary>
    public int Siguiente(string clave)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(clave, out var actual))
                actual = Leer(clave);

            // El protocolo admite un rango acotado; se cicla lejos del borde para no depender de
            // cuánto tolera el firmware. El ciclo es inofensivo: sólo importa que la secuencia
            // difiera de la del comando inmediatamente anterior.
            var siguiente = actual >= 999_999 ? 1 : actual + 1;
            _cache[clave] = siguiente;
            Escribir(clave, siguiente);
            return siguiente;
        }
    }

    private string Ruta(string clave) =>
        Path.Combine(_dir, $"seq-{clave.Replace(':', '_').Replace('.', '_')}.txt");

    private int Leer(string clave)
    {
        try
        {
            var f = Ruta(clave);
            if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out var v))
                return v;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return 0;
    }

    private void Escribir(string clave, int valor)
    {
        // Si esto falla no se puede seguir: perder el rastro de la secuencia es exactamente el
        // escenario que este store existe para evitar. Mejor abortar la venta que emitir a ciegas.
        File.WriteAllText(Ruta(clave), valor.ToString());
    }
}
