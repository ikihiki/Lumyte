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

    /// <summary>Creates a reference to the compiler's source image.</summary>
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

    internal static int Radius(float sigma)
    {
        if (!float.IsFinite(sigma) || sigma < 0 || Math.Ceiling(3d * sigma) > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma));
        }

        return (int)Math.Ceiling(3d * sigma);
    }
}
