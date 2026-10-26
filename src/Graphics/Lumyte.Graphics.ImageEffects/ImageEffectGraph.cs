namespace Lumyte.Graphics.ImageEffects;

/// <summary>Builds immutable, bounded-depth effect DAGs with self-contained effect operations.</summary>
public sealed class ImageEffectGraph
{
    private readonly int _depth;

    private ImageEffectGraph(ImageEffectNode root, int depth)
    {
        if (depth > 256)
        {
            throw new ArgumentException("Effect graphs support at most 256 nested operations.");
        }

        Root = root;
        _depth = depth;
    }

    /// <summary>Gets the immutable root snapshot.</summary>
    public ImageEffectNode Root { get; }

    /// <summary>Creates a reference to the source image bound during RenderGraph expansion.</summary>
    /// <returns>The source graph.</returns>
    public static ImageEffectGraph Source() => new(new SourceEffectNode(), 1);

    /// <summary>Combines an immutable effect with copied child graphs.</summary>
    /// <param name="definition">CPU-only parameters and region mappings, retained without mutation.</param>
    /// <param name="inputs">Child graphs in definition order. Zero inputs are permitted for image generators.</param>
    /// <returns>The new effect graph.</returns>
    public static ImageEffectGraph Create(ImageEffectDefinition definition, params ImageEffectGraph[] inputs)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(inputs);
        int inputCount = definition.InputCount;
        if (inputCount < 0 || inputCount != inputs.Length)
        {
            throw new ArgumentException("The child count must match the definition's nonnegative input count.", nameof(inputs));
        }

        var nodes = new ImageEffectNode[inputs.Length];
        int depth = 1;
        for (int i = 0; i < inputs.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(inputs[i]);
            nodes[i] = inputs[i].Root;
            depth = Math.Max(depth, inputs[i]._depth + 1);
        }

        return new(EffectOperationNode.Create(definition, nodes), depth);
    }

    /// <summary>Calculates a conservative finite source region without allocating GPU resources.</summary>
    /// <param name="output">The desired output rectangle.</param>
    /// <returns>The union of all source regions required by the output.</returns>
    public EffectPixelRect GetRequiredSourceBounds(EffectPixelRect output)
    {
        if (output.IsEmpty)
        {
            return output;
        }

        var required = new Dictionary<ImageEffectNode, EffectPixelRect>(ReferenceEqualityComparer.Instance);
        var order = new List<ImageEffectNode>();
        var visited = new HashSet<ImageEffectNode>(ReferenceEqualityComparer.Instance);
        Collect(Root);
        required.Add(Root, output);
        EffectPixelRect? result = null;
        for (int i = order.Count - 1; i >= 0; i--)
        {
            ImageEffectNode node = order[i];
            if (!required.TryGetValue(node, out EffectPixelRect bounds) || bounds.IsEmpty)
            {
                continue;
            }

            switch (node)
            {
                case SourceEffectNode:
                    result = result?.Union(bounds) ?? bounds;
                    break;
                case EffectOperationNode operation:
                    for (int inputIndex = 0; inputIndex < operation.Inputs.Count; inputIndex++)
                    {
                        Add(operation.Inputs[inputIndex], operation.Definition.GetRequiredInputBounds(inputIndex, bounds));
                    }

                    break;
            }
        }

        return result ?? new(output.X, output.Y, 0, 0);

        void Add(ImageEffectNode node, EffectPixelRect bounds) => required[node] = required.TryGetValue(node, out EffectPixelRect previous) ? previous.Union(bounds) : bounds;

        void Collect(ImageEffectNode node)
        {
            if (!visited.Add(node))
            {
                return;
            }

            if (visited.Count > 4096)
            {
                throw new NotSupportedException("An effect graph may contain at most 4096 distinct operations.");
            }

            switch (node)
            {
                case EffectOperationNode operation:
                    foreach (ImageEffectNode input in operation.Inputs)
                    {
                        Collect(input);
                    }

                    break;
            }

            order.Add(node);
        }
    }

    internal static int Radius(float sigma)
    {
        if (!float.IsFinite(sigma) || sigma < 0 || Math.Ceiling(3d * sigma) > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma));
        }

        return (int)Math.Ceiling(3d * sigma);
    }
}
