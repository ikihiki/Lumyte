using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumyte.Resources;

public sealed class AssetKeyJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(AssetKey<>);

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        Type resourceType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(AssetKeyJsonConverter<>).MakeGenericType(resourceType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class AssetKeyJsonConverter<T> : JsonConverter<AssetKey<T>>
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
}
