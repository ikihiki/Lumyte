using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Lumyte.Settings;

internal static class SettingsModelOverlay
{
    internal static T Read<T>(T defaults, JsonObject values, JsonTypeInfo<T> metadata)
        where T : class
    {
        if (RequiresJsonMerge(metadata))
        {
            JsonNode merged = MergeJson(JsonSerializer.SerializeToNode(defaults, metadata), values, metadata);
            return merged.Deserialize(metadata) ?? throw new JsonException("Settings cannot be null.");
        }

        T parsed = values.Deserialize(metadata) ?? throw new JsonException("Settings cannot be null.");
        return (T)Apply(defaults, parsed, values, metadata)!;
    }

    private static bool RequiresJsonMerge(JsonTypeInfo metadata) => metadata.Kind == JsonTypeInfoKind.Object &&
        (metadata.OnDeserializing is not null || metadata.OnDeserialized is not null ||
        metadata.Properties.Any(property => property.Get is not null && property.Set is null));

    private static object? Apply(object? defaults, object? parsed, JsonNode? saved, JsonTypeInfo metadata)
    {
        if (parsed is null || saved is null)
        {
            return parsed;
        }

        if (defaults is IDictionary original && parsed is IDictionary entries && metadata.Type.IsGenericType && metadata.Type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && metadata.Type.GetGenericArguments()[0] == typeof(string))
        {
            IDictionary replacement = SettingsDictionary.CreateEmptyLike(original);
            foreach (DictionaryEntry entry in entries)
            {
                replacement.Add(entry.Key, entry.Value);
            }

            return replacement;
        }

        if (metadata.Kind != JsonTypeInfoKind.Object || saved is not JsonObject savedObject || defaults is null)
        {
            return parsed;
        }

        if (RequiresJsonMerge(metadata))
        {
            return MergeJson(JsonSerializer.SerializeToNode(defaults, metadata), saved, metadata).Deserialize(metadata);
        }

        foreach ((string name, JsonNode? node) in savedObject)
        {
            JsonPropertyInfo? property = FindProperty(metadata, name);
            if (property?.Get is null || property.Set is null)
            {
                continue;
            }

            object? value = property.Get(parsed);
            if (property.CustomConverter is null)
            {
                value = Apply(property.Get(defaults), value, node, metadata.Options.GetTypeInfo(property.PropertyType));
            }

            property.Set(defaults, value);
        }

        return defaults;
    }

    private static JsonNode MergeJson(JsonNode? defaults, JsonNode saved, JsonTypeInfo metadata)
    {
        if (metadata.Kind != JsonTypeInfoKind.Object || saved is not JsonObject savedObject)
        {
            return saved.DeepClone();
        }

        JsonObject result = defaults?.DeepClone() as JsonObject ?? new JsonObject();
        foreach ((string name, JsonNode? value) in savedObject)
        {
            JsonPropertyInfo? property = FindProperty(metadata, name);
            if (property is not null)
            {
                result[property.Name] = value is null ? null : MergeJson(result[property.Name], value, metadata.Options.GetTypeInfo(property.PropertyType));
            }
        }

        return result;
    }

    private static JsonPropertyInfo? FindProperty(JsonTypeInfo metadata, string name) => metadata.Properties.FirstOrDefault(property =>
        string.Equals(property.Name, name, metadata.Options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
}
