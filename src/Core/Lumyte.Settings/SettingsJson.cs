using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Lumyte.Settings;

internal static class SettingsJson
{
    internal const int MaxDocumentDepth = 64;

    internal static JsonTypeInfo<T> CreatePersistenceMetadata<T>(JsonTypeInfo<T> original)
    {
        IJsonTypeInfoResolver resolver = original.Options.TypeInfoResolver ?? throw new InvalidOperationException("Settings JSON metadata requires a type information resolver.");
        var options = new JsonSerializerOptions(original.Options)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            RespectRequiredConstructorParameters = false,
        };
        options.TypeInfoResolver = resolver.WithAddedModifier(metadata =>
        {
            if (metadata.Type == typeof(T))
            {
                CopyObjectContract(original, metadata);
            }

            if (metadata.Kind == JsonTypeInfoKind.Object)
            {
                metadata.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
                foreach (JsonPropertyInfo property in metadata.Properties)
                {
                    property.IsExtensionData = false;
                    property.IsRequired = false;
                    if (property.Get is not null)
                    {
                        property.ShouldSerialize = static (_, _) => true;
                    }
                }
            }
        });
        options.MakeReadOnly();
        return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }

    internal static byte[] SerializeDocument(JsonObject document)
    {
        using var stream = new MemoryStream();
        try
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = MaxDocumentDepth });
            document.WriteTo(writer);
        }
        catch (InvalidOperationException error)
        {
            throw new JsonException("The settings document cannot be saved in a readable JSON format.", error);
        }

        byte[] bytes = stream.ToArray();
        using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaxDocumentDepth });
        ValidateProperties(parsed.RootElement);
        return bytes;
    }

    internal static void ValidateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException($"Duplicate settings property: {property.Name}.");
                }

                ValidateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                ValidateProperties(item);
            }
        }
    }

    private static void CopyObjectContract(JsonTypeInfo source, JsonTypeInfo target)
    {
        if (source.Kind != target.Kind)
        {
            throw new InvalidOperationException("Settings JSON metadata must use the same converter as its resolver.");
        }

        if (source.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        target.CreateObject = source.CreateObject;
        target.NumberHandling = source.NumberHandling;
        target.PreferredPropertyObjectCreationHandling = source.PreferredPropertyObjectCreationHandling;
        target.OnDeserializing = source.OnDeserializing;
        target.OnDeserialized = source.OnDeserialized;
        target.OnSerializing = source.OnSerializing;
        target.OnSerialized = source.OnSerialized;
        target.Properties.Clear();
        foreach (JsonPropertyInfo property in source.Properties)
        {
            JsonPropertyInfo copy = target.CreateJsonPropertyInfo(property.PropertyType, property.Name);
            copy.AttributeProvider = property.AttributeProvider;
            copy.CustomConverter = property.CustomConverter;
            copy.Get = property.Get;
            copy.Set = property.Set;
            copy.IsGetNullable = property.IsGetNullable;
            copy.IsSetNullable = property.IsSetNullable;
            copy.IsRequired = property.IsRequired;
            copy.NumberHandling = property.NumberHandling;
            copy.ObjectCreationHandling = property.ObjectCreationHandling;
            copy.Order = property.Order;
            copy.ShouldSerialize = property.ShouldSerialize;
            target.Properties.Add(copy);
        }
    }
}
