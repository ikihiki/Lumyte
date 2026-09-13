using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed record BeginRenderingCommand(P.GpuColorAttachment[] Colors, P.GpuDepthStencilAttachment? DepthStencil) : RecordedCommand;
    private sealed record EndRenderingCommand : RecordedCommand;
    private sealed record RasterPipelineCommand(P.GpuRasterPipelineHandle Pipeline) : RecordedCommand;
    private sealed record ViewportScissorCommand(P.GpuViewport Viewport, P.GpuScissorRect Scissor) : RecordedCommand;
    private sealed record StencilReferenceCommand(uint Reference) : RecordedCommand;
    private sealed record BlendConstantCommand(P.GpuClearColor Color) : RecordedCommand;
    private sealed record RasterBindingsCommand(uint Group, P.GpuBindingsHandle Bindings, uint[] DynamicOffsets) : RecordedCommand;
    private sealed record RasterRootCommand(byte[] Bytes) : RecordedCommand;
    private sealed record DrawCommand(uint VertexCount, uint InstanceCount, uint FirstVertex, uint FirstInstance) : RecordedCommand;
    private sealed record DrawIndexedCommand(P.GpuBufferRange Indices, P.GpuIndexFormat Format, uint IndexCount,
        uint InstanceCount, uint FirstIndex, int BaseVertex, uint FirstInstance) : RecordedCommand;
    private sealed record DrawIndirectCommand(P.GpuBufferRange Arguments) : RecordedCommand;
    private sealed record DrawIndexedIndirectCommand(P.GpuBufferRange Indices, P.GpuIndexFormat Format, P.GpuBufferRange Arguments) : RecordedCommand;

    private sealed partial class CommandRecording
    {
        private void RequireRendering()
        {
            RequireRecording();
            if (!rendering) { throw new InvalidOperationException("The command requires an open render scope."); }
        }

        public override void BeginRendering(ReadOnlySpan<P.GpuColorAttachment> colors, P.GpuDepthStencilAttachment? depthStencil = null)
        {
            lock (Queue.Owner.gate)
            {
                RequireOutsidePass();
                foreach (P.GpuColorAttachment color in colors)
                {
                    Queue.Owner.RequireTexture(color.View.Texture);
                    if (color.ResolveTarget is { } resolve) { Queue.Owner.RequireTexture(resolve.Texture); }
                }
                if (depthStencil is { } depth) { Queue.Owner.RequireTexture(depth.View.Texture); }
                Commands.Add(new BeginRenderingCommand(colors.ToArray(), depthStencil));
                rendering = true;
            }
        }

        public override void EndRendering()
        {
            lock (Queue.Owner.gate)
            {
                RequireRendering();
                Commands.Add(new EndRenderingCommand());
                rendering = false;
            }
        }

        public override void SetPipeline(P.GpuRasterPipelineHandle pipeline)
        {
            lock (Queue.Owner.gate)
            {
                RequireRendering();
                Queue.Owner.RequireRasterPipeline(pipeline);
                Commands.Add(new RasterPipelineCommand(pipeline));
            }
        }

        public override void SetViewportAndScissor(P.GpuViewport viewport, P.GpuScissorRect scissor)
        {
            lock (Queue.Owner.gate) { RequireRendering(); Commands.Add(new ViewportScissorCommand(viewport, scissor)); }
        }

        public override void SetStencilReference(uint reference)
        {
            lock (Queue.Owner.gate) { RequireRendering(); Commands.Add(new StencilReferenceCommand(reference)); }
        }

        public override void SetBlendConstant(P.GpuClearColor color)
        {
            lock (Queue.Owner.gate) { RequireRendering(); Commands.Add(new BlendConstantCommand(color)); }
        }

        public override void SetBindings(uint group, P.GpuBindingsHandle bindings, ReadOnlySpan<uint> dynamicOffsets = default)
        {
            lock (Queue.Owner.gate)
            {
                RequireRendering();
                Queue.Owner.RequireBindings(bindings);
                Commands.Add(new RasterBindingsCommand(group, bindings, dynamicOffsets.ToArray()));
            }
        }

        public override void SetRootData(ReadOnlySpan<byte> bytes)
        {
            lock (Queue.Owner.gate) { RequireRendering(); Commands.Add(new RasterRootCommand(bytes.ToArray())); }
        }

        public override void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            lock (Queue.Owner.gate)
            {
                RequireRendering();
                Commands.Add(new DrawCommand(vertexCount, instanceCount, firstVertex, firstInstance));
            }
        }

        public override void DrawIndexed(P.GpuBufferRange indices, P.GpuIndexFormat format, uint indexCount,
            uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        {
            lock (Queue.Owner.gate)
            {
                RequireRendering();
                Commands.Add(new DrawIndexedCommand(ResolveIndices(indices), format, indexCount, instanceCount, firstIndex, baseVertex, firstInstance));
            }
        }

        public override void DrawIndirect(P.GpuBufferRange arguments)
        {
            lock (Queue.Owner.gate)
            {
                RequireRendering();
                Commands.Add(new DrawIndirectCommand(ResolveDrawArguments(arguments, 16)));
            }
        }

        public override void DrawIndexedIndirect(P.GpuBufferRange indices, P.GpuIndexFormat format, P.GpuBufferRange arguments)
        {
            lock (Queue.Owner.gate)
            {
                RequireRendering();
                Commands.Add(new DrawIndexedIndirectCommand(ResolveIndices(indices), format, ResolveDrawArguments(arguments, 20)));
            }
        }

        private P.GpuBufferRange ResolveIndices(P.GpuBufferRange indices)
        {
            P.GpuBufferRange range = Queue.Owner.ResolveCommandRange(indices);
            if (range.Length == ulong.MaxValue)
            { throw new ArgumentOutOfRangeException(nameof(indices), "The index range length cannot use WebGPU's whole-size sentinel."); }
            return range;
        }

        private P.GpuBufferRange ResolveDrawArguments(P.GpuBufferRange arguments, uint minimumLength)
        {
            P.GpuBufferRange range = Queue.Owner.ResolveCommandRange(arguments);
            if (range.Length < minimumLength)
            { throw new ArgumentOutOfRangeException(nameof(arguments), $"The logical argument range must cover {minimumLength} bytes."); }
            return range;
        }

        internal void RetainAttachmentView(TextureViewLease view)
        {
            try { attachmentViews.Add(view); }
            catch { Queue.Owner.ReleaseTextureView(view); throw; }
        }
    }
}
