namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides explicit initialization of the dynamic state required before drawing.</summary>
public static class RenderEncoderExtensions
{
    /// <summary>Sets viewport, scissor, blend constants and stencil reference in order without drawing.</summary>
    /// <remarks>Each setter validates its input; a later failure does not roll back earlier settings.</remarks>
    /// <param name="encoder">The active rendering pass.</param>
    /// <param name="viewport">The explicit viewport.</param>
    /// <param name="scissor">The explicit scissor.</param>
    /// <param name="blendConstant">The blend constant; defaults to zero.</param>
    /// <param name="stencilReference">The stencil reference; defaults to zero.</param>
    public static void SetDynamicState(this IRenderEncoder encoder, Viewport viewport, ScissorRect scissor, BlendConstant blendConstant = default, uint stencilReference = 0)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        encoder.SetViewport(viewport);
        encoder.SetScissor(scissor);
        encoder.SetBlendConstant(blendConstant);
        encoder.SetStencilReference(stencilReference);
    }
}
