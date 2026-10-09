using Lumyte.Graphics.Abstractions;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanRenderEncoder(VulkanCommandBuffer owner, RenderColorAttachmentDesc[] attachments) : IRenderEncoder
{
    private readonly TextureFormat[] _formats = attachments.Select(a => a.View.Info.Format).ToArray();
    private readonly (uint Width, uint Height) _size = attachments[0].View.Texture.GetMipSize(attachments[0].View.Info.BaseMipLevel);
    private VulkanGraphicsPipeline? _pipeline;
    private RenderStateSnapshot? _state;
    private bool _viewport;
    private bool _scissor;
    private bool _blend;
    private bool _stencil;

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not VulkanGraphicsPipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.");
        }

        program.ValidateAlive();
        _pipeline = program;
    }

    public void SetRenderState(GraphicsRenderStateDesc state)
    {
        owner.ValidatePass(this);
        RenderStateSnapshot snapshot = PipelineValidation.State(state);
        _state = snapshot;
    }

    public void SetViewport(Viewport viewport)
    {
        owner.ValidatePass(this);
        PipelineValidation.Viewport(viewport);
        if (viewport.X < 0 || viewport.Y < 0 || viewport.Width > _size.Width - viewport.X || viewport.Height > _size.Height - viewport.Y)
        {
            throw new ArgumentException("Viewport is outside the pass extent.");
        }

        var native = new V.Viewport(viewport.X, viewport.Y + viewport.Height, viewport.Width, -viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        owner.Owner.Api.CmdSetViewport(owner.Native, 0, 1, &native);
        _viewport = true;
    }

    public void SetScissor(ScissorRect scissor)
    {
        owner.ValidatePass(this);
        PipelineValidation.Scissor(scissor, _size.Width, _size.Height);
        var native = new V.Rect2D(new(checked((int)scissor.X), checked((int)scissor.Y)), new(scissor.Width, scissor.Height));
        owner.Owner.Api.CmdSetScissor(owner.Native, 0, 1, &native);
        _scissor = true;
    }

    public void SetBlendConstant(BlendConstant value)
    {
        owner.ValidatePass(this);
        if (!float.IsFinite(value.Red) || !float.IsFinite(value.Green) || !float.IsFinite(value.Blue) || !float.IsFinite(value.Alpha))
        {
            throw new ArgumentException("Blend constants must be finite.");
        }

        float* constants = stackalloc float[] { value.Red, value.Green, value.Blue, value.Alpha };
        owner.Owner.Api.CmdSetBlendConstants(owner.Native, constants);
        _blend = true;
    }

    public void SetStencilReference(uint reference)
    {
        owner.ValidatePass(this);
        if (reference > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }

        owner.Owner.Api.CmdSetStencilReference(owner.Native, V.StencilFaceFlags.FrontAndBack, reference);
        _stencil = true;
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        owner.ValidatePass(this);
        if (_pipeline == null || _state == null || !_viewport || !_scissor || !_blend || !_stencil)
        {
            throw new InvalidOperationException("Set a program, draw state and all dynamic state before drawing.");
        }

        _pipeline.ValidateAlive();
        PipelineValidation.Draw(_pipeline.Desc, _state, _formats, _pipeline.FragmentOutputs);
        _ = checked(firstVertex + vertexCount);
        _ = checked(firstInstance + instanceCount);
        if (vertexCount == 0 || instanceCount == 0)
        {
            return;
        }

        V.Pipeline pipeline = _pipeline.Resolve(_state, _formats);
        owner.Owner.Api.CmdBindPipeline(owner.Native, V.PipelineBindPoint.Graphics, pipeline);
        owner.Owner.Api.CmdDraw(owner.Native, vertexCount, instanceCount, firstVertex, firstInstance);
        owner.TrackProgram(_pipeline.ValidateAlive);
    }

    public void End() => owner.EndRender(this);
}
