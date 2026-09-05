using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumyte.Resources;

/// <summary>Converts one closed asset key type without runtime generic construction.</summary>
public sealed class AssetKeyJsonConverter<T> : JsonConverter<AssetKey<T>>
{
    public override AssetKey<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Asset keys must be JSON strings.");
        }

        string? text = reader.GetString();
        if (text is null)
        {
            throw new JsonException("Asset keys cannot be null.");
        }

        try
        {
            return AssetKey<T>.Parse(text);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new JsonException("The asset key is invalid.", exception);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        AssetKey<T> value,
        JsonSerializerOptions options)
    {
        if (!value.IsValid)
        {
            throw new JsonException("Default asset keys cannot be serialized.");
        }

        writer.WriteStringValue(value.CanonicalText);
    }
}
