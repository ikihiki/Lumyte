using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Contains the shared device resources from which independent 2D renderers are created.</summary>
/// <remarks>Use the CreateRenderer extension on the owning ResourceReference to retain an exact generation.</remarks>
public sealed class Renderer2DResource
{
    internal Renderer2DResource(GraphicsProgramResource program)
    {
        Program = program;
    }

    /// <summary>Gets the physical device for this renderer generation.</summary>
    public IGraphicDevice Device => Program.Device;

    /// <summary>Gets the borrowed program; retain its resource lease while using it.</summary>
    public GraphicsProgramResource Program { get; }
}
