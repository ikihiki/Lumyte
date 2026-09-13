using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private sealed partial class CommandRecord
    {
        private Pipeline rasterPipeline;
        private bool rendering;

        public override void SetPipeline(NativeGpuRasterPipelineHandle pipeline)
        {
            VerifyRecording();
            rasterPipeline = Owner.RequireRasterPipeline(pipeline).Pipeline;
            Owner.vk.CmdBindPipeline(Current, PipelineBindPoint.Graphics, rasterPipeline);
        }

        public override void SetViewport(NativeGpuViewport viewport)
        {
            VerifyRecording();
            Viewport native = RasterViewport(viewport);
            Owner.vk.CmdSetViewport(Current, 0, 1, &native);
        }

        public override void SetScissor(NativeGpuScissorRect scissor)
        {
            VerifyRecording();
            Rect2D native = new(new(scissor.X, scissor.Y), new(scissor.Width, scissor.Height));
            Owner.vk.CmdSetScissor(Current, 0, 1, &native);
        }

        public override void SetDepthStencilState(NativeGpuDepthStencilState state)
        {
            VerifyRecording();
            // Convert every enum before touching native state, so a failed conversion is atomic.
            CompareOp depth = RasterCompare(state.DepthCompare);
            StencilOpState front = StencilState(state.Front, state.StencilReadMask, state.StencilWriteMask);
            StencilOpState back = StencilState(state.Back, state.StencilReadMask, state.StencilWriteMask);
            Owner.vk.CmdSetDepthTestEnable(Current, state.DepthTest);
            Owner.vk.CmdSetDepthWriteEnable(Current, state.DepthWrite);
            Owner.vk.CmdSetDepthCompareOp(Current, depth);
            Owner.vk.CmdSetStencilTestEnable(Current, state.StencilTest);
            SetStencilFace(StencilFaceFlags.FaceFrontBit, front);
            SetStencilFace(StencilFaceFlags.FaceBackBit, back);
        }

        private static StencilOpState StencilState(NativeGpuStencilFaceState state, uint readMask, uint writeMask) => new()
        {
            CompareOp = RasterCompare(state.Compare), FailOp = RasterStencilOperation(state.FailOp),
            DepthFailOp = RasterStencilOperation(state.DepthFailOp), PassOp = RasterStencilOperation(state.PassOp),
            CompareMask = readMask, WriteMask = writeMask, Reference = state.Reference,
        };

        private void SetStencilFace(StencilFaceFlags face, StencilOpState state)
        {
            Owner.vk.CmdSetStencilOp(Current, face, state.FailOp, state.PassOp, state.DepthFailOp, state.CompareOp);
            Owner.vk.CmdSetStencilCompareMask(Current, face, state.CompareMask);
            Owner.vk.CmdSetStencilWriteMask(Current, face, state.WriteMask);
            Owner.vk.CmdSetStencilReference(Current, face, state.Reference);
        }

        public override void Draw(ReadOnlySpan<byte> rootData, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            VerifyInsideRendering();
            PushRoot(rootData);
            Owner.vk.CmdDraw(Current, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public override void DrawIndexed(ReadOnlySpan<byte> rootData, NativeGpuRange indices, NativeGpuIndexFormat format,
            uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        {
            VerifyInsideRendering();
            BindIndices(indices, format);
            PushRoot(rootData);
            Owner.vk.CmdDrawIndexed(Current, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
        }

        public override void DrawIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
        {
            VerifyInsideRendering();
            NativeDrawIndirectInfo info = DrawArguments(arguments, 16);
            PushRoot(rootData);
            Owner.drawIndirect(Current, &info);
        }

        public override void DrawIndexedIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange indices, NativeGpuIndexFormat format, NativeGpuRange arguments)
        {
            VerifyInsideRendering();
            NativeDrawIndirectInfo info = DrawArguments(arguments, 20);
            BindIndices(indices, format);
            PushRoot(rootData);
            Owner.drawIndexedIndirect(Current, &info);
        }

        private NativeDrawIndirectInfo DrawArguments(NativeGpuRange arguments, ulong size)
        {
            Owner.RequireCommandRange(arguments);
            if (arguments.Size < size) { throw new ArgumentException("The logical range must cover one draw argument record.", nameof(arguments)); }
            return new()
            {
                SType = (StructureType)1000318009,
                AddressRange = new() { Address = arguments.GpuAddress, Size = arguments.Size, Stride = size },
                AddressFlags = LinearAddressFlags, DrawCount = 1,
            };
        }

        private void BindIndices(NativeGpuRange indices, NativeGpuIndexFormat format)
        {
            Owner.RequireCommandRange(indices);
            NativeBindIndexBufferInfo info = new()
            {
                SType = (StructureType)1000318007,
                AddressRange = new() { Address = indices.GpuAddress, Size = indices.Size },
                AddressFlags = LinearAddressFlags, IndexType = RasterIndexType(format),
            };
            Owner.bindIndexBuffer(Current, &info);
        }

        private void VerifyInsideRendering()
        {
            VerifyRecording();
            if (!rendering) { throw new InvalidOperationException("A rendering scope is required."); }
        }

        private void VerifyOutsideRendering()
        {
            VerifyRecording();
            if (rendering) { throw new InvalidOperationException("This command requires the rendering scope to end first."); }
        }
    }
}
