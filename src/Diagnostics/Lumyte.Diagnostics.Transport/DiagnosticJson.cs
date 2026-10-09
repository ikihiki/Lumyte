using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.Transport;

/// <summary>Source-generated JSON with lossless Int64 strings for browser clients.</summary>
public static class DiagnosticJson
{
    /// <summary>Gets the shared immutable protocol context.</summary>
    public static DiagnosticJsonContext Context { get; } = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Converters = { new Int64Converter() },
        MaxDepth = 32,
    });

    private sealed class Int64Converter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && long.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                return value;
            }

            throw new JsonException("Int64 must be a valid decimal string.");
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
