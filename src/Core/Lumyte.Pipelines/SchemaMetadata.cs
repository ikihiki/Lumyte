namespace Lumyte.Pipelines;

internal static class SchemaMetadata
{
    internal static SchemaDescriptor? For<TValue>()
        => default(TValue) is IHostPlacement host ? host.Descriptor : default(TValue) is INonCpuReadablePlacement extension ? extension.Descriptor : null;

    internal static void Validate(SchemaDescriptor? captured, SchemaDescriptor? actual)
    {
        if ((captured is null) != (actual is null) || (captured is not null && actual is not null && !captured.Matches(actual)))
        {
            throw new InvalidOperationException("Schema version, layout or immutable metadata changed after declaration capture.");
        }
    }
}
