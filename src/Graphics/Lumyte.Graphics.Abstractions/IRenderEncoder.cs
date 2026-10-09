namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides a non-owning recording scope.</summary>
public interface IRenderEncoder
{
    /// <summary>Selects a program without changing draw state.</summary>
    /// <param name="pipeline">The same-device program.</param>
    void SetPipeline(IGraphicsPipeline pipeline);

    /// <summary>Replaces draw state with a validated immutable snapshot.</summary>
    /// <param name="state">The state to snapshot.</param>
    void SetRenderState(GraphicsRenderStateDesc state);

    /// <summary>Sets an explicit viewport.</summary>
    /// <param name="viewport">The finite positive viewport.</param>
    void SetViewport(Viewport viewport);

    /// <summary>Sets an explicit scissor within the pass extent.</summary>
    /// <param name="scissor">The nonempty rectangle.</param>
    void SetScissor(ScissorRect scissor);

    /// <summary>Sets finite blend constants independently of the native pipeline.</summary>
    /// <param name="value">The constants.</param>
    void SetBlendConstant(BlendConstant value);

    /// <summary>Sets the shared eight bit stencil reference.</summary>
    /// <param name="reference">The reference value.</param>
    void SetStencilReference(uint reference);

    /// <summary>Records a direct draw after validating all explicit state.</summary>
    /// <param name="vertexCount">The vertex count.</param>
    /// <param name="instanceCount">The instance count.</param>
    /// <param name="firstVertex">The first vertex index.</param>
    /// <param name="firstInstance">The first instance index.</param>
    void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);

    /// <summary>Ends this pass once without submitting or waiting.</summary>
    void End();
}
