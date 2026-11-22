namespace Lumyte.Pipelines;

/// <summary>The immutable declaration topology, without allocations or execution.</summary>
public sealed class PipelineDescription
{
    internal PipelineDescription(Node[] nodes, Type inputType, int output)
    {
        InputType = inputType;
        Output = output;
        Nodes = Array.AsReadOnly(nodes.Select((node, index) => new PipelineNodeDescription(index + 1, node.ImplementationType, node.OutputType, Array.AsReadOnly((int[])node.Inputs.Clone()), node.HostSchema, node.Placement)).ToArray());
    }

    /// <summary>Gets the arbitrary input type.</summary>
    public Type InputType { get; }

    /// <summary>Gets the output producer index, or zero for the input.</summary>
    public int Output { get; }

    /// <summary>Gets the immutable nodes.</summary>
    public IReadOnlyList<PipelineNodeDescription> Nodes { get; }
}
