namespace Lumyte.Graphics.Pipelines.RenderGraph;

/// <summary>Captures complete immutable associations for all external manifest slots.</summary>
public sealed class PipelineGraphBindings
{
    /// <summary>Initializes a new instance of the <see cref="PipelineGraphBindings"/> class.</summary>
    /// <param name="textures">All exact texture associations.</param>
    /// <param name="buffers">All closed typed buffer associations.</param>
    public PipelineGraphBindings(IReadOnlyList<PipelineGraphTextureBinding> textures, IReadOnlyList<PipelineGraphBufferBinding> buffers)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(buffers);
        if (textures.Any(binding => binding is null) || buffers.Any(binding => binding is null))
        {
            throw new ArgumentException("Bindings cannot contain null entries.");
        }

        Textures = Array.AsReadOnly(textures.ToArray());
        Buffers = Array.AsReadOnly(buffers.ToArray());
    }

    /// <summary>Gets immutable texture associations.</summary>
    public IReadOnlyList<PipelineGraphTextureBinding> Textures { get; }

    /// <summary>Gets immutable typed buffer associations.</summary>
    public IReadOnlyList<PipelineGraphBufferBinding> Buffers { get; }
}
