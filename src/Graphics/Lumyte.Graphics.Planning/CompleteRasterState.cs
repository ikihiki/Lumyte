using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>All immutable raster and dynamic state required by a structured raster operation.</summary>
public sealed record CompleteRasterState
{
    /// <summary>Gets the render state, viewport and scissor.</summary>
    public required GraphicsRasterState Raster { get; init; }

    /// <summary>Gets explicit blend constants.</summary>
    public BlendConstant BlendConstant { get; init; }

    /// <summary>Gets the shared eight-bit stencil reference.</summary>
    public uint StencilReference { get; init; }

    internal CompleteRasterState Capture() => this with { Raster = Raster.Capture() };

    internal void Record(IRenderEncoder encoder)
    {
        encoder.SetRenderState(Raster.RenderState);
        encoder.SetViewport(Raster.Viewport);
        encoder.SetScissor(Raster.Scissor);
        encoder.SetBlendConstant(BlendConstant);
        encoder.SetStencilReference(StencilReference);
    }
}
