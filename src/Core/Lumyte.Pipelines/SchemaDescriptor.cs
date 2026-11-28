namespace Lumyte.Pipelines;

/// <summary>Captures a versioned canonical element layout.</summary>
public sealed record SchemaDescriptor
{
    /// <summary>Initializes a new instance of the <see cref="SchemaDescriptor"/> class.</summary>
    /// <param name="version">The positive schema version.</param>
    /// <param name="elementType">The canonical unmanaged element type.</param>
    /// <param name="elementStride">The positive element stride.</param>
    /// <param name="metadata">Immutable scalar domain metadata.</param>
    public SchemaDescriptor(int version, Type elementType, int elementStride, IReadOnlyDictionary<string, object> metadata)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentNullException.ThrowIfNull(elementType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementStride);
        ArgumentNullException.ThrowIfNull(metadata);
        Version = version;
        ElementType = elementType;
        ElementStride = elementStride;
        Metadata = MetadataSnapshot.Copy(metadata);
    }

    /// <summary>Gets the schema version.</summary>
    public int Version { get; }

    /// <summary>Gets the canonical element type.</summary>
    public Type ElementType { get; }

    /// <summary>Gets the canonical byte stride.</summary>
    public int ElementStride { get; }

    /// <summary>Gets the captured immutable metadata.</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }

    internal bool Matches(SchemaDescriptor other)
        => Version == other.Version && ElementType == other.ElementType && ElementStride == other.ElementStride &&
        Metadata.Count == other.Metadata.Count && Metadata.All(pair => other.Metadata.TryGetValue(pair.Key, out object? value) && Equals(pair.Value, value));
}
