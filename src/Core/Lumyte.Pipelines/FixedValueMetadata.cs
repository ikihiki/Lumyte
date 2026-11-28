namespace Lumyte.Pipelines;

internal static class FixedValueMetadata
{
    // Only closed runtime types that cannot supply placement or dynamic input metadata.
    internal static bool IsFixed(Type type)
        => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan);
}
