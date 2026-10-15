using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Provides a borrowed device and target geometry without a RenderGraph.</summary>
public sealed class RenderPreparationContext2D
{
    internal RenderPreparationContext2D(IGraphicDevice device, DrawingTargetInfo2D target, IGraphicsProgramProvider? programs = null)
    {
        Device = device;
        Target = target;
        Programs = programs ?? DrawingPrograms2D.DefaultProvider;
    }

    /// <summary>Gets the borrowed device.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the borrowed program provider, frozen for this preparation.</summary>
    public IGraphicsProgramProvider Programs { get; }

    /// <summary>Gets the target geometry normalized to Load.</summary>
    public DrawingTargetInfo2D Target { get; }
}
