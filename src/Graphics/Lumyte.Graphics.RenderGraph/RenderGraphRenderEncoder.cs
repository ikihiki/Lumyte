using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Records graph shader operations within one callback-scoped render pass.</summary>
public sealed class RenderGraphRenderEncoder : IDisposable
{
    private readonly RenderGraphContext _context;
    private readonly IRenderEncoder _native;
    private readonly IReadOnlyList<RenderGraphTexture> _colorAttachments;
    private readonly RenderGraphTexture? _depthAttachment;
    private IGraphicsPipeline? _pipeline;
    private RenderGraphShaderResources? _arguments;
    private object? _argumentPipeline;
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

    /// <summary>Captures logical numeric values once and selects arguments for the current pipeline.</summary>
    /// <param name="resources">The shader inputs declared for this graph pass.</param>
    public void SetArguments(RenderGraphShaderResources resources)
    {
        Validate(resources);
        ShaderValueSnapshot snapshot = resources.Snapshot();
        _native.SetArguments(snapshot);
        _arguments = resources;
        _argumentPipeline = _pipeline;
    }

    /// <summary>Records Draw with the previously selected arguments.</summary>
    /// <param name="vertexCount">The vertexCount value.</param>
    /// <param name="instanceCount">The instanceCount value.</param>
    /// <param name="firstVertex">The firstVertex value.</param>
    /// <param name="firstInstance">The firstInstance value.</param>
    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        ValidateExecution();
        _native.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>Records DrawIndexed with the previously selected arguments.</summary>
    /// <param name="indexCount">The indexCount value.</param>
    /// <param name="instanceCount">The instanceCount value.</param>
    /// <param name="firstIndex">The firstIndex value.</param>
    /// <param name="baseVertex">The baseVertex value.</param>
    /// <param name="firstInstance">The firstInstance value.</param>
    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        ValidateExecution();
        _native.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    /// <summary>Records DrawIndirect with the previously selected arguments.</summary>
    /// <param name="arguments">The arguments value.</param>
    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        ValidateExecution();
        _native.DrawIndirect(arguments);
    }

    /// <summary>Records DrawIndexedIndirect with the previously selected arguments.</summary>
    /// <param name="arguments">The arguments value.</param>
    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        ValidateExecution();
        _native.DrawIndexedIndirect(arguments);
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

    private void ValidateExecution()
    {
        EnsureActive();
        if (_pipeline is null)
        {
            throw new InvalidOperationException("Select a pipeline before recording shader operations.");
        }

        // Borrowed graphs without a device cannot create logical resources; native execution validates its own root contract.
        if (_context.DeviceTarget is not { } target)
        {
            return;
        }

        if (!(ShaderDataLayout.HasRoot(_pipeline.Desc.VertexShader.Artifact.GetTarget(target)) || (_pipeline.Desc.FragmentShader is { } fragment && ShaderDataLayout.HasRoot(fragment.Artifact.GetTarget(target)))))
        {
            return;
        }

        RenderGraphShaderResources resources = _arguments ?? throw new InvalidOperationException("Set arguments before recording shader operations.");
        if (!ReferenceEquals(_argumentPipeline, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        Validate(resources);
        resources.Bind(_native);
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
