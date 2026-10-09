using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Lumyte.Settings;

internal sealed class JsonSettingsDefinition<T> : ISettingsDefinition<T>
    where T : class, new()
{
    private readonly Func<JsonObject, int, JsonObject>? _upgrade;

    public JsonSettingsDefinition()
        : this(CreateReflectionMetadata())
    {
    }

    public JsonSettingsDefinition(JsonTypeInfo<T> metadata, int schemaVersion = 1, Func<JsonObject, int, JsonObject>? upgrade = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        metadata.Options.MakeReadOnly();
        metadata.MakeReadOnly();
        JsonTypeInfo = metadata;
        SchemaVersion = schemaVersion;
        _upgrade = upgrade;
        ValidateMetadata(metadata, new HashSet<Type>());
    }

    public int SchemaVersion { get; }

    public JsonTypeInfo<T> JsonTypeInfo { get; }

    public bool Matches(JsonTypeInfo<T> metadata, int schemaVersion, Func<JsonObject, int, JsonObject>? upgrade) =>
        ReferenceEquals(JsonTypeInfo, metadata) && SchemaVersion == schemaVersion && _upgrade == upgrade;

    public T DeepClone(T value) => (T)Copy(value, JsonTypeInfo, new Dictionary<object, object>(ReferenceEqualityComparer.Instance))!;

    public JsonObject Upgrade(JsonObject values, int sourceVersion)
    {
        if (sourceVersion == SchemaVersion)
        {
            return values;
        }

        return _upgrade is null ? throw new NotSupportedException("No migration is registered for this settings version.") : _upgrade(values, sourceVersion);
    }

    private static JsonTypeInfo<T> CreateReflectionMetadata()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            throw new InvalidOperationException("Reflection serialization is disabled. Register source-generated metadata with UseJsonTypeInfo.");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }

    private static bool IsScalar(Type type) => (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr)) || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
        type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(DateOnly) || type == typeof(TimeOnly);

    private static void ValidateMetadata(JsonTypeInfo metadata, HashSet<Type> visited)
    {
        Type type = metadata.Type;
        if (!visited.Add(type) || IsScalar(type))
        {
            return;
        }

        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            ValidateMetadata(metadata.Options.GetTypeInfo(nullable), visited);
            return;
        }

        if (type == typeof(byte[]))
        {
            return;
        }

        if (metadata.PolymorphismOptions is not null)
        {
            throw Unsupported(type);
        }

        if (type.IsArray && type.GetArrayRank() == 1 && metadata.Kind == JsonTypeInfoKind.Enumerable)
        {
            ValidateMetadata(metadata.Options.GetTypeInfo(type.GetElementType()!), visited);
            return;
        }

        if (metadata.Kind == JsonTypeInfoKind.Dictionary && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string) && metadata.CreateObject is not null)
        {
            ValidateMetadata(metadata.Options.GetTypeInfo(type.GetGenericArguments()[1]), visited);
            return;
        }

        if (metadata.Kind == JsonTypeInfoKind.Enumerable && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && metadata.CreateObject is not null)
        {
            ValidateMetadata(metadata.Options.GetTypeInfo(type.GetGenericArguments()[0]), visited);
            return;
        }

        if (metadata.Kind != JsonTypeInfoKind.Object || type.IsValueType || metadata.CreateObject is null)
        {
            throw Unsupported(type);
        }

        foreach (JsonPropertyInfo property in metadata.Properties)
        {
            if (property.Get is null || property.Set is null || property.CustomConverter is not null)
            {
                throw Unsupported(property.PropertyType);
            }

            ValidateMetadata(metadata.Options.GetTypeInfo(property.PropertyType), visited);
        }
    }

    private static object? Copy(object? value, JsonTypeInfo metadata, Dictionary<object, object> copies)
    {
        if (value is null || IsScalar(metadata.Type))
        {
            return value;
        }

        Type? nullable = Nullable.GetUnderlyingType(metadata.Type);
        if (nullable is not null)
        {
            return Copy(value, metadata.Options.GetTypeInfo(nullable), copies);
        }

        if (value.GetType() != metadata.Type)
        {
            throw Unsupported(value.GetType());
        }

        if (copies.TryGetValue(value, out object? existing))
        {
            return existing;
        }

        if (value is Array array)
        {
            var result = (Array)array.Clone();
            copies.Add(value, result);
            if (value is byte[])
            {
                return result;
            }

            JsonTypeInfo element = metadata.Options.GetTypeInfo(metadata.Type.GetElementType()!);
            for (int index = 0; index < array.Length; index++)
            {
                result.SetValue(Copy(array.GetValue(index), element, copies), index);
            }

            return result;
        }

        object copy = metadata.CreateObject!();
        copies.Add(value, copy);
        if (value is IDictionary dictionary)
        {
            JsonTypeInfo element = metadata.Options.GetTypeInfo(metadata.Type.GetGenericArguments()[1]);
            foreach (DictionaryEntry entry in dictionary)
            {
                ((IDictionary)copy).Add(entry.Key, Copy(entry.Value, element, copies));
            }
        }
        else if (value is IList list)
        {
            JsonTypeInfo element = metadata.Options.GetTypeInfo(metadata.Type.GetGenericArguments()[0]);
            foreach (object? item in list)
            {
                ((IList)copy).Add(Copy(item, element, copies));
            }
        }
        else
        {
            foreach (JsonPropertyInfo property in metadata.Properties)
            {
                if (property.Get is not null && property.Set is not null)
                {
                    property.Set(copy, Copy(property.Get(value), metadata.Options.GetTypeInfo(property.PropertyType), copies));
                }
            }
        }

        return copy;
    }

    private static InvalidOperationException Unsupported(Type type) => new($"Automatic settings copying does not support {type}. Use writable JSON properties, scalar values, arrays, List<T> and Dictionary<string, T>, or register a custom definition with UseJsonDefinition.");
}
