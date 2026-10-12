namespace Lumyte.Graphics.ImageEffects;

/// <summary>Stores an immutable effect and copied child snapshots.</summary>
public sealed record EffectOperationNode : ImageEffectNode
{
    private EffectOperationNode(ImageEffectDefinition definition, ImageEffectNode[] inputs)
    {
        Definition = definition;
        Inputs = Array.AsReadOnly(inputs);
    }

    /// <summary>Gets the immutable effect with its fixed pass implementation.</summary>
    public ImageEffectDefinition Definition { get; }

    /// <summary>Gets the copied, read-only child node sequence.</summary>
    public IReadOnlyList<ImageEffectNode> Inputs { get; }

    internal static EffectOperationNode Create(ImageEffectDefinition definition, ImageEffectNode[] inputs) => new(definition, inputs);
}
