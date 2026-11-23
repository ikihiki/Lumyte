using System.Collections.ObjectModel;

namespace Lumyte.Pipelines;

internal static class MetadataSnapshot
{
    internal static IReadOnlyDictionary<string, object> Copy(IReadOnlyDictionary<string, object> source)
    {
        var copy = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach ((string key, object value) in source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (value is not (string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or Guid or Type or IImmutableSchemaMetadata))
            {
                throw new ArgumentException("Metadata values must be immutable scalars or explicit immutable snapshots.", nameof(source));
            }

            copy.Add(key, value);
        }

        return new ReadOnlyDictionary<string, object>(copy);
    }
}
