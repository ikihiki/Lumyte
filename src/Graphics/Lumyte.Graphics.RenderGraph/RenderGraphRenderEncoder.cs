using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Records graph shader operations within one callback-scoped render pass.</summary>
public sealed class RenderGraphRenderEncoder : IDisposable
{
    private readonly RenderGraphContext _context;
    private readonly IRenderEncoder _native;
    private readonly IReadOnlyList<RenderGraphTexture> _colorAttachments;
    private readonly RenderGraphTexture? _depthAttachment;
    private IGraphicsPipeline? _pipeline;
    private bool _ended;

    internal RenderGraphRenderEncoder(RenderGraphContext context, IRenderEncoder native, RenderGraphTexture[] colorAttachments, RenderGraphTexture? depthAttachment)
    {
        _context = context;
        _native = native;
        _colorAttachments = Array.AsReadOnly((RenderGraphTexture[])colorAttachments.Clone());
        _depthAttachment = depthAttachment;
    }

    /// <summary>Selects the graphics pipeline.</summary>
    /// <param name="pipeline">The pipeline value.</param>
    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(pipeline);
        _native.SetPipeline(pipeline);
        _pipeline = pipeline;
    }

    /// <summary>Snapshots validated draw state.</summary>
    /// <param name="state">The state value.</param>
    public void SetRenderState(GraphicsRenderStateDesc state)
    {
        EnsureActive();
        _native.SetRenderState(state);
    }

    /// <summary>Sets the viewport.</summary>
    /// <param name="viewport">The viewport value.</param>
    public void SetViewport(Viewport viewport)
    {
        EnsureActive();
        _native.SetViewport(viewport);
    }

    /// <summary>Sets the scissor rectangle.</summary>
    /// <param name="scissor">The scissor value.</param>
    public void SetScissor(ScissorRect scissor)
    {
        EnsureActive();
        _native.SetScissor(scissor);
    }

    /// <summary>Sets blend constants.</summary>
    /// <param name="value">The value value.</param>
    public void SetBlendConstant(BlendConstant value)
    {
        EnsureActive();
        _native.SetBlendConstant(value);
    }

    /// <summary>Sets the stencil reference.</summary>
    /// <param name="reference">The reference value.</param>
    public void SetStencilReference(uint reference)
    {
        EnsureActive();
        _native.SetStencilReference(reference);
    }

    /// <summary>Selects a native sixteen-bit index range.</summary>
    /// <param name="indices">The indices value.</param>
    public void SetIndexBuffer(BufferSlice<ushort> indices)
    {
        EnsureActive();
        _native.SetIndexBuffer(indices);
    }

    /// <summary>Selects a native thirty-two-bit index range.</summary>
    /// <param name="indices">The indices value.</param>
    public void SetIndexBuffer(BufferSlice<uint> indices)
    {
        EnsureActive();
        _native.SetIndexBuffer(indices);
    }

    /// <summary>Validates logical resources, snapshots current values and records Draw.</summary>
    /// <param name="resources">The shader inputs declared for this graph pass.</param>
    /// <param name="vertexCount">The vertexCount value.</param>
    /// <param name="instanceCount">The instanceCount value.</param>
    /// <param name="firstVertex">The firstVertex value.</param>
    /// <param name="firstInstance">The firstInstance value.</param>
    public void Draw(RenderGraphShaderResources resources, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        Validate(resources);
        resources.Core.Draw(_native, resources.Snapshot(), vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>Validates logical resources, snapshots current values and records DrawIndexed.</summary>
    /// <param name="resources">The shader inputs declared for this graph pass.</param>
    /// <param name="indexCount">The indexCount value.</param>
    /// <param name="instanceCount">The instanceCount value.</param>
    /// <param name="firstIndex">The firstIndex value.</param>
    /// <param name="baseVertex">The baseVertex value.</param>
    /// <param name="firstInstance">The firstInstance value.</param>
    public void DrawIndexed(RenderGraphShaderResources resources, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        Validate(resources);
        resources.Core.DrawIndexed(_native, resources.Snapshot(), indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    /// <summary>Validates logical resources, snapshots current values and records DrawIndirect.</summary>
    /// <param name="resources">The shader inputs declared for this graph pass.</param>
    /// <param name="arguments">The arguments value.</param>
    public void DrawIndirect(RenderGraphShaderResources resources, BufferSlice<DrawIndirectArguments> arguments)
    {
        Validate(resources);
        resources.Core.DrawIndirect(_native, resources.Snapshot(), arguments);
    }

    /// <summary>Validates logical resources, snapshots current values and records DrawIndexedIndirect.</summary>
    /// <param name="resources">The shader inputs declared for this graph pass.</param>
    /// <param name="arguments">The arguments value.</param>
    public void DrawIndexedIndirect(RenderGraphShaderResources resources, BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        Validate(resources);
        resources.Core.DrawIndexedIndirect(_native, resources.Snapshot(), arguments);
    }

    /// <summary>Ends this pass once without submitting or releasing GPU resources.</summary>
    public void End()
    {
        EnsureActive();
        _ended = true;
        try
        {
            _native.End();
        }
        finally
        {
            _context.EncoderEnded(this);
        }
    }

    /// <summary>Ends this pass if it has not already ended.</summary>
    public void Dispose()
    {
        if (!_ended)
        {
            try
            {
                End();
            }
            catch (Exception cleanupError)
            {
                _context.DeferEncoderCleanup(cleanupError);
            }
        }
    }

    private void Validate(RenderGraphShaderResources resources)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resources);
        IGraphicsPipeline pipeline = _pipeline ?? throw new InvalidOperationException("Select a pipeline before recording shader operations.");
        resources.ValidateRender(_context, pipeline, _colorAttachments, _depthAttachment);
    }

    private void EnsureActive()
    {
        _context.EnsureActive();
        if (_ended)
        {
            throw new InvalidOperationException("The graph encoder has already ended.");
        }
    }
}
