namespace Lumyte.Pipelines;

internal static class InputDescriptions
{
    private static readonly SchemaExtent _scalarExtent = new(1, new Dictionary<string, object>());

    internal static StageInputDescriptions Fixed(int arity, long maxHostBytes)
    {
        var extents = new SchemaExtent[arity];
        Array.Fill(extents, _scalarExtent);
        return new StageInputDescriptions(extents, maxHostBytes);
    }

    internal static StageInputDescriptions Build<TInput>(Node node, Node[] nodes, StageRequirements[] requirements, long inputByteBudget)
    {
        IEnumerable<SchemaExtent> extents = node.Inputs.Select(index =>
        {
            SchemaDescriptor? schema = index == 0 ? (default(TInput) is IHostPlacement host ? host.Descriptor : null) : nodes[index - 1].HostSchema;
            long bytes = index == 0 ? inputByteBudget : requirements[index - 1].MaxHostBytes;
            return new SchemaExtent(index != 0 && requirements[index - 1].OutputMaxElements is { } elements ? elements : schema is null ? 1 : bytes / schema.ElementStride, new Dictionary<string, object> { ["UpperBound"] = true });
        });
        return new StageInputDescriptions(extents, inputByteBudget);
    }

    internal static StageInputDescriptions Minimum(int inputs)
        => new(Enumerable.Range(0, inputs).Select(_ => new SchemaExtent(0, new Dictionary<string, object> { ["LowerBound"] = true })));

    internal static StageInputDescriptions Actual(object[] inputs, long maxHostBytes)
    {
        IEnumerable<SchemaExtent> extents = inputs.Select(input =>
        {
            IHostPlacement? host = ((IValueLeaseMetadata)input).HostPlacement;
            if (host is not null && host.Extent is null)
            {
                throw new InvalidOperationException("An input contains uninitialized host placement.");
            }

            return ((IValueLeaseMetadata)input).PlacementExtent ?? _scalarExtent;
        });
        return new StageInputDescriptions(extents, maxHostBytes);
    }

    internal static void ValidateHostInputs(StageRequirements requirement, object[] inputs)
    {
        if (requirement.MaxHostInputElements is not { } limit)
        {
            return;
        }

        foreach (object input in inputs)
        {
            IHostPlacement? host = ((IValueLeaseMetadata)input).HostPlacement;
            if (host is not null && (host.Extent is null || host.Extent.ElementCount > limit))
            {
                throw new InvalidOperationException("The canonical host input exceeds its declared element ceiling.");
            }
        }
    }
}
