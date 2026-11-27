using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;
/// <summary>Declares graphics raster state.</summary>
public sealed record GraphicsRasterState
{
    /// <summary>Gets render state.</summary>
    public required GraphicsRenderStateDesc RenderState { get; init; }

    /// <summary>Gets viewport.</summary>
    public required Viewport Viewport { get; init; }

    /// <summary>Gets scissor.</summary>
    public required ScissorRect Scissor { get; init; }

    internal GraphicsRasterState Capture() => this with
    {
        RenderState = RenderState with
        {
            ColorTargets = Array.AsReadOnly(RenderState.ColorTargets.ToArray()),
        },
    };
}
