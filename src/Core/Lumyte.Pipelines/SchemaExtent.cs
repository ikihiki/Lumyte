namespace Lumyte.Pipelines;

/// <summary>Captures a finite number of canonical elements and immutable domain metadata.</summary>
public sealed record SchemaExtent
{
    /// <summary>Initializes a new instance of the <see cref="SchemaExtent"/> class.</summary>
    /// <param name="elementCount">The nonnegative element count.</param>
    /// <param name="metadata">Immutable scalar metadata.</param>
    public SchemaExtent(long elementCount, IReadOnlyDictionary<string, object> metadata)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        ArgumentNullException.ThrowIfNull(metadata);
        ElementCount = elementCount;
        Metadata = MetadataSnapshot.Copy(metadata);
    }

    /// <summary>Gets the number of elements.</summary>
    public long ElementCount { get; }

    /// <summary>Gets the captured domain metadata.</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }
}
