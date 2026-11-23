namespace Lumyte.Pipelines;

/// <summary>TA passive typed edge owned by one declaration graph.</summary>
/// <typeparam name="TV">The edge value type.</typeparam>
public sealed class Port<TV>
{
    private readonly GraphStorage _graph;

    internal Port(GraphStorage graph, int index)
    {
        _graph = graph;
        Index = index;
    }

    internal int Index { get; }

    internal GraphStorage Graph => _graph;

    /// <summary>Adds one stage consuming this edge; equivalent to Stage.</summary>
    /// <typeparam name="TO">The output value type.</typeparam>
    /// <param name="implementation">The captured stage implementation.</param>
    /// <returns>The new output edge.</returns>
    public Port<TO> Then<TO>(IStage<TV, TO> implementation)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _graph.Validate(this);
        return _graph.Add<TO>(new StageNode<TV, TO>(implementation.Factory, [Index], implementation.GetType()), [this]);
    }
}
