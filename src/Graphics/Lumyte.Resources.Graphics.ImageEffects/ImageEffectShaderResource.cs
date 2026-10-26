using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Groups borrowed, manager-owned graphics resources used by image-effect passes.</summary>
public sealed class ImageEffectShaderResource
{
    internal ImageEffectShaderResource(GraphicsResource<IGraphicsPipeline> pipeline, GraphicsResource<IGraphicsSampler> sampler)
    {
        Device = pipeline.Device;
        Epoch = pipeline.Epoch;
        Shaders = new(pipeline.Value, sampler.Value);
    }

    /// <summary>Gets the physical device shared by the resource dependencies.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the original physical device epoch.</summary>
    public ulong Epoch { get; }

    /// <summary>Gets the borrowed shader, pipeline and sampler bundle retained by the resource lease.</summary>
    public ImageEffectShaders Shaders { get; }
}
