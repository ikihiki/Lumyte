using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Preserves 64-bit integers when a browser parses JSON.</summary>
public sealed class LongJsonConverter : JsonConverter<long>
{
    /// <inheritdoc/>
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => long.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
