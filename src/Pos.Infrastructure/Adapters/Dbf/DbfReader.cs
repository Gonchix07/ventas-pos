using System.Text;

namespace Pos.Infrastructure.Adapters.Dbf;

/// <summary>Un campo del header de un DBF (dBase III/FoxPro), con el offset dentro del registro.</summary>
public sealed record DbfField(string Name, char Type, int Offset, int Length, int Decimals);

/// <summary>
/// Lector mínimo de DBF (dBase III/Visual FoxPro), de solo lectura, sin dependencias externas —
/// suficiente para leer archivos "planos" (sin memo .FPT) como pedidos.dbf de la app legacy
/// Mayorista_Release. Solo decodifica los campos pedidos por nombre (más rápido que un parser
/// genérico tipo dbfread cuando el archivo tiene 50+ columnas y solo interesan 10).
/// Codepage fija en Windows-1252 (Latin1): es la que usa Visual FoxPro en instalaciones en español.
/// </summary>
public sealed class DbfReader : IDisposable
{
    private static readonly Encoding Cp1252 = Encoding.GetEncoding(1252);

    private readonly FileStream _stream;
    private readonly int _recordLength;
    private readonly int _headerLength;
    private readonly int _recordCount;

    public IReadOnlyList<DbfField> Fields { get; }

    public DbfReader(string path)
    {
        // FileShare.ReadWrite: el archivo lo sigue escribiendo la app VFP a diario, no podemos
        // pedir acceso exclusivo.
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var header = new byte[32];
        ReadFully(header, 32);
        _recordCount = BitConverter.ToInt32(header, 4);
        _headerLength = BitConverter.ToInt16(header, 8);
        _recordLength = BitConverter.ToInt16(header, 10);

        var fields = new List<DbfField>();
        int offset = 1; // el registro arranca con 1 byte de flag de borrado
        var fieldBuf = new byte[32];
        while (true)
        {
            int first = _stream.ReadByte();
            if (first < 0 || first == 0x0D) break; // 0x0D = terminador del header de campos
            fieldBuf[0] = (byte)first;
            ReadFully(fieldBuf.AsSpan(1, 31));
            string name = Cp1252.GetString(fieldBuf, 0, 11).TrimEnd('\0', ' ');
            char type = (char)fieldBuf[11];
            int length = fieldBuf[16];
            int decimals = fieldBuf[17];
            fields.Add(new DbfField(name, type, offset, length, decimals));
            offset += length;
        }
        Fields = fields;

        _stream.Seek(_headerLength, SeekOrigin.Begin);
    }

    private void ReadFully(Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = _stream.Read(buffer[total..]);
            if (read == 0) throw new EndOfStreamException("DBF truncado o inaccesible.");
            total += read;
        }
    }

    private void ReadFully(byte[] buffer, int count) => ReadFully(buffer.AsSpan(0, count));

    /// <summary>
    /// Recorre todos los registros no borrados, devolviendo solo los campos pedidos (por nombre,
    /// case-insensitive) ya decodificados como texto (trim de espacios de relleno). El llamador
    /// convierte a número/fecha/etc. según necesite.
    /// </summary>
    public IEnumerable<IReadOnlyDictionary<string, string>> ReadRecords(IReadOnlySet<string> fieldNames)
    {
        var wanted = Fields.Where(f => fieldNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var buffer = new byte[_recordLength];
        for (int i = 0; i < _recordCount; i++)
        {
            int read = _stream.Read(buffer, 0, _recordLength);
            if (read < _recordLength) yield break; // EOF antes de lo esperado por el header: cortamos, no rompemos

            if (buffer[0] == (byte)'*') continue; // registro marcado como borrado

            var row = new Dictionary<string, string>(wanted.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var f in wanted)
                row[f.Name] = Cp1252.GetString(buffer, f.Offset, f.Length).Trim();
            yield return row;
        }
    }

    public void Dispose() => _stream.Dispose();
}
