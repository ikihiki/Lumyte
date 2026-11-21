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

    internal SchemaData<TS>? Data { get; }
}
