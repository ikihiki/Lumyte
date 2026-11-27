namespace Lumyte.Pipelines;

/// <summary>An immutable reusable typed declaration, without runtime owners.</summary>
/// <typeparam name="TInput">The input payload.</typeparam>
/// <typeparam name="TOutput">The output payload.</typeparam>
public sealed class PipelineDefinition<TInput, TOutput>
{
    private readonly PipelineDescription _description;
    private readonly SchemaDescriptor? _inputSchema = SchemaMetadata.For<TInput>();
    private readonly ValueShape _inputShape = ValueShape.Capture(default(TInput));

    internal PipelineDefinition(Node[] nodes, int output)
    {
        Nodes = nodes;
        Output = output;
        _description = new PipelineDescription(nodes, typeof(TInput), output);
    }

    /// <summary>Gets the selected typed output edge for a visitor.</summary>
    public PipelineEdge<TOutput> OutputEdge => new(Output);

    internal Node[] Nodes { get; }

    internal int Output { get; }

    /// <summary>Returns the same immutable topology without allocating native resources.</summary>
    /// <returns>The captured declaration topology.</returns>
    public PipelineDescription DescribeDataflow() => _description;

    /// <summary>Visits the captured typed factories in topological order.</summary>
    /// <param name="visitor">The external compiler.</param>
    public void Visit(IPipelineDefinitionVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        ValidateSchemas();
        for (int i = 0; i < Nodes.Length; i++)
        {
            Nodes[i].Visit(visitor, i + 1);
        }
    }

    internal void ValidateSchemas()
    {
        SchemaMetadata.Validate(_inputSchema, SchemaMetadata.For<TInput>());
        if (_inputShape.IsComposite)
        {
            _inputShape.Validate(ValueShape.Capture(default(TInput)));
        }

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
        if (value.PlacementSchema is not null && value.PlacementExtent is null)
        {
            throw new InvalidOperationException("Uninitialized canonical placement.");
        }

        ValueShape shape = producer == 0 ? _inputShape : Nodes[producer - 1].CapturedShape;
        if (shape.IsComposite || value.IsBundle)
        {
            value.ValidateShape(shape);
        }
    }
}
