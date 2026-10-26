using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Groups borrowed native resources implementing the 2D paint and coverage ABI.</summary>
/// <param name="device">The physical device owning the resources.</param>
/// <param name="pipeline">The graphics pipeline, including its retained shader dependencies.</param>
/// <param name="sampler">The optional shared nearest-clamp sampler.</param>
/// <remarks>This value owns no native resources. Supply their owner to the renderer or retain them externally through GPU completion.</remarks>
public sealed class DrawingResources2D(IGraphicDevice device, IGraphicsPipeline pipeline, IGraphicsSampler? sampler = null)
{
    /// <summary>Gets the physical device.</summary>
    public IGraphicDevice Device { get; } = device ?? throw new ArgumentNullException(nameof(device));

    /// <summary>Gets the borrowed pipeline; fragment metadata comes from its shader descriptor.</summary>
    public IGraphicsPipeline Pipeline { get; } = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    /// <summary>Gets the optional borrowed shared sampler.</summary>
    public IGraphicsSampler? Sampler { get; } = sampler;
}
