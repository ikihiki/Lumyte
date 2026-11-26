namespace Lumyte.Pipelines;

/// <summary>An immutable reusable typed declaration, without runtime owners.</summary>
/// <typeparam name="TInput">The input payload.</typeparam>
/// <typeparam name="TOutput">The output payload.</typeparam>
public sealed class PipelineDefinition<TInput, TOutput>
{
    private readonly PipelineDescription _description;
    private readonly SchemaDescriptor? _inputSchema = SchemaMetadata.For<TInput>();

    internal PipelineDefinition(Node[] nodes, int output)
    {
        Nodes = nodes;
        Output = output;
        _description = new PipelineDescription(nodes, typeof(TInput), output);
    }

    internal Node[] Nodes { get; }

    internal int Output { get; }

    /// <summary>Returns the same immutable topology without allocating native resources.</summary>
    /// <returns>The captured declaration topology.</returns>
    public PipelineDescription DescribeDataflow() => _description;

    internal void ValidateSchemas()
    {
        SchemaMetadata.Validate(_inputSchema, SchemaMetadata.For<TInput>());
        foreach (Node node in Nodes)
        {
            node.ValidateSchema();
        }
    }

    internal void ValidateInput(int producer, object input)
    {
        SchemaDescriptor? expected = producer == 0 ? _inputSchema : Nodes[producer - 1].HostSchema;
        var value = (IValueLeaseMetadata)input;
        SchemaMetadata.Validate(expected, value.PlacementSchema);
    }
}
