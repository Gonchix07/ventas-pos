using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pos.Api.Common;

/// <summary>
/// Fuerza que todo <see cref="DateTime"/> se serialice como UTC (con sufijo "Z"). Bug real detectado
/// (2026-08-25, Tesorería): SQL Server (columnas datetime2, leídas vía EF Core) NO guarda el
/// <see cref="DateTimeKind"/> — al leer, EF siempre devuelve <see cref="DateTimeKind.Unspecified"/>
/// aunque el valor se haya escrito con <c>DateTime.UtcNow</c> (convención de todo el proyecto, ver
/// docs). Sin este converter, System.Text.Json serializa el ISO 8601 SIN sufijo de zona
/// (ej. "2026-08-25T16:48:09"), y el navegador (<c>new Date(...)</c> + <c>toLocaleString()</c>) lo
/// interpreta como HORA LOCAL en vez de UTC — mostraba la hora de Argentina desfasada por el offset
/// completo (UTC-3): un lote cerrado hace unos minutos aparecía con una hora vieja en Tesorería.
///
/// Se asume que TODO DateTime persistido en esta app es UTC (no hay ningún campo "hora local de
/// pared" en el esquema) — es la convención documentada del proyecto desde el hardening pre-piloto.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc);
    }
}

/// <summary>Misma lógica que <see cref="UtcDateTimeConverter"/> para <c>DateTime?</c>.</summary>
public class UtcNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        var utc = value.Value.Kind == DateTimeKind.Utc ? value.Value : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        writer.WriteStringValue(utc);
    }
}
