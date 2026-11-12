using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Provides a borrowed device and target geometry without a RenderGraph.</summary>
public sealed class RenderPreparationContext2D
{
    private readonly DrawingResourceState2D _resources;

    internal RenderPreparationContext2D(IGraphicDevice device, DrawingTargetInfo2D target, DrawingResourceState2D resources, DrawingArgumentPlan2D? argumentPlan = null)
    {
        Device = device;
        Target = target;
        _resources = resources;
        ArgumentPlan = argumentPlan ?? new(device);
        Clips = new(target);
    }

    /// <summary>Gets the borrowed device.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the borrowed pipeline and optional shared sampler.</summary>
    public DrawingResources2D Resources => _resources.Resources;

    /// <summary>Gets the borrowed preparation-local registration plan; custom renderers participate explicitly.</summary>
    public DrawingArgumentPlan2D ArgumentPlan { get; }

    /// <summary>Gets the target geometry normalized to Load.</summary>
    public DrawingTargetInfo2D Target { get; }

    internal ClipNormalizationCache2D Clips { get; }

    /// <summary>Retains the renderer's existing resource owner for prepared work through GPU completion.</summary>
    /// <returns>A lifetime token containing no pipeline or shader state.</returns>
    public IDisposable RetainResources() => _resources.Retain();

    /// <summary>Rejects resources whose runtime generation is no longer usable.</summary>
    public void ValidateResources() => _resources.Validate();

    /// <summary>Creates a borrowed context using another same-device argument owner, preserving target and resource lifetime.</summary>
    /// <param name="argumentPlan">The borrowed registration and default-resource owner.</param>
    /// <returns>The context for explicitly participating preparation.</returns>
    public RenderPreparationContext2D WithArgumentPlan(DrawingArgumentPlan2D argumentPlan)
    {
        ArgumentNullException.ThrowIfNull(argumentPlan);
        if (!ReferenceEquals(Device, argumentPlan.Device))
        {
            throw new ArgumentException("The argument plan belongs to another device.", nameof(argumentPlan));
        }

        return new(Device, Target, _resources, argumentPlan);
    }

    internal void CleanupAfterFailure(IDisposable resource, Exception preparationFailure) => _resources.CleanupAfterFailure(resource, preparationFailure);

    internal SolidRectangleResources2D? RectangleResources() => _resources.Rectangles();
}
