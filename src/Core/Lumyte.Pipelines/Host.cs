namespace Lumyte.Pipelines;

/// <summary>An immutable host placement; values can only be published by a preparation scope.</summary>
/// <typeparam name="TS">The canonical schema.</typeparam>
public readonly struct Host<TS> : IHostPlacement
    where TS : ISchema
{
    internal Host(SchemaData<TS> data)
    {
        Data = data;
    }

    /// <inheritdoc/>
    SchemaDescriptor IHostPlacement.Descriptor => Data?.Descriptor ?? TS.Descriptor;

    /// <inheritdoc/>
    SchemaExtent? IHostPlacement.Extent => Data?.Extent;

    /// <inheritdoc/>
    object? IHostPlacement.Invocation => Data?.Invocation;

    /// <summary>Gets the captured immutable extent of this initialized host snapshot.</summary>
    public SchemaExtent Extent => InitializedData.Extent;

    /// <summary>Gets the captured schema descriptor, independent of subsequent static schema changes.</summary>
    public SchemaDescriptor Descriptor => InitializedData.Descriptor;

    internal SchemaData<TS>? Data { get; }

    private SchemaData<TS> InitializedData => Data ?? throw new InvalidOperationException("Uninitialized host placement.");

    /// <summary>Reads the canonical managed snapshot without exposing writable storage.</summary>
    /// <typeparam name="T">The exact captured unmanaged element type.</typeparam>
    /// <returns>A read-only view of the independently retained managed bytes.</returns>
    /// <remarks>The snapshot is not pooled or native storage. A copied Host value keeps its managed bytes alive.</remarks>
    public ReadOnlySpan<T> ReadElements<T>()
        where T : unmanaged
        => InitializedData.ReadElements<T>();
}
