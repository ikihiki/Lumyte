using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Provides a borrowed device and target geometry without a RenderGraph.</summary>
public sealed class RenderPreparationContext2D
{
    private readonly DrawingResourceState2D _resources;

    internal RenderPreparationContext2D(IGraphicDevice device, DrawingTargetInfo2D target, DrawingResourceState2D resources)
    {
        Device = device;
        Target = target;
        _resources = resources;
    }

    /// <summary>Gets the borrowed device.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the borrowed pipeline and optional shared sampler.</summary>
    public DrawingResources2D Resources => _resources.Resources;

    /// <summary>Gets the target geometry normalized to Load.</summary>
    public DrawingTargetInfo2D Target { get; }

    /// <summary>Retains the renderer's existing resource owner for prepared work through GPU completion.</summary>
    /// <returns>A lifetime token containing no pipeline or shader state.</returns>
    public IDisposable RetainResources() => _resources.Retain();

    /// <summary>Rejects resources whose runtime generation is no longer usable.</summary>
    public void ValidateResources() => _resources.Validate();
}
