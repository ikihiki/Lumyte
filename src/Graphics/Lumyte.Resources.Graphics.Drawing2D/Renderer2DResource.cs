using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Contains the shared device resources from which independent 2D renderers are created.</summary>
/// <remarks>Use the CreateRenderer extension on the owning ResourceReference to retain an exact generation.</remarks>
public sealed class Renderer2DResource
{
    internal Renderer2DResource(DrawingResources2D resources)
    {
        Resources = resources;
    }

    /// <summary>Gets the physical device for this renderer generation.</summary>
    public IGraphicDevice Device => Resources.Device;

    /// <summary>Gets the borrowed drawing resources; retain the resource lease while using them.</summary>
    public DrawingResources2D Resources { get; }
}
