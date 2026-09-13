using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private sealed partial class NativeRecording
    {
        public override void Draw(ReadOnlySpan<byte> rootData, uint vertexCount, uint instanceCount = 1,
            uint firstVertex = 0, uint firstInstance = 0)
        {
            VerifyInsideRendering();
            NativeGpuRasterPipelineHandle pipeline = SelectedRasterPipeline();
            NativeGpuDepthStencilState state = depthStencil;
            byte[] root = CopyRootData(rootData);
            operations.Add(commands =>
            {
                BindRaster(commands, pipeline, state, root);
                commands.DrawInstanced(vertexCount, instanceCount, firstVertex, firstInstance);
            });
        }

        public override void DrawIndexed(ReadOnlySpan<byte> rootData, NativeGpuRange indices, NativeGpuIndexFormat format,
            uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        {
            VerifyInsideRendering();
            NativeGpuRasterPipelineHandle pipeline = SelectedRasterPipeline();
            Owner.Owner.RequireLinear(indices.Region);
            NativeGpuDepthStencilState state = depthStencil;
            byte[] root = CopyRootData(rootData);
            operations.Add(commands =>
            {
                BindRaster(commands, pipeline, state, root);
                BindIndices(commands, indices, format);
                commands.DrawIndexedInstanced(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
            });
        }

        public override void DrawIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
            => RecordIndirectDraw(rootData, arguments, null, default);

        public override void DrawIndexedIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange indices,
            NativeGpuIndexFormat format, NativeGpuRange arguments)
            => RecordIndirectDraw(rootData, arguments, indices, format);

        private void RecordIndirectDraw(ReadOnlySpan<byte> rootData, NativeGpuRange arguments,
            NativeGpuRange? indices, NativeGpuIndexFormat format)
        {
            VerifyInsideRendering();
            NativeGpuRasterPipelineHandle pipeline = SelectedRasterPipeline();
            Owner.Owner.RequireLinear(arguments.Region);
            if (indices.HasValue) { Owner.Owner.RequireLinear(indices.Value.Region); }
            if (arguments.Size < (indices.HasValue ? 20ul : 16ul))
            {
                throw new ArgumentException("The argument range does not contain one complete native draw record.", nameof(arguments));
            }
            NativeGpuDepthStencilState state = depthStencil;
            byte[] root = CopyRootData(rootData);
            operations.Add(commands =>
            {
                LinearRecord argumentsBuffer = Owner.Owner.RequireLinear(arguments.Region);
                BindRaster(commands, pipeline, state, root);
                if (indices.HasValue) { BindIndices(commands, indices.Value, format); }
                commands.ExecuteIndirect(indices.HasValue ? Owner.Owner.drawIndexedSignature : Owner.Owner.drawSignature,
                    1, argumentsBuffer.Resource, arguments.Offset, (ID3D12Resource*)null, 0);
            });
        }

        private NativeGpuRasterPipelineHandle SelectedRasterPipeline()
            => rasterPipeline ?? throw new InvalidOperationException("Select a raster pipeline before drawing.");

        private void BindRaster(ComPtr<ID3D12GraphicsCommandList8> commands,
            NativeGpuRasterPipelineHandle pipeline, NativeGpuDepthStencilState state, byte[] root)
        {
            RasterPipelineRecord record = Owner.Owner.RequireRasterPipeline(pipeline);
            commands.SetPipelineState(Owner.Owner.ResolveRasterPipeline(record, state));
            commands.SetGraphicsRootSignature(Owner.Owner.computeRootSignature);
            if (record.Vertex is not null) { commands.IASetPrimitiveTopology(record.Description.Topology switch
            {
                NativeGpuPrimitiveTopology.TriangleList => D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist,
                NativeGpuPrimitiveTopology.TriangleStrip => D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglestrip,
                _ => throw new ArgumentOutOfRangeException(nameof(pipeline)),
            }); }
            commands.OMSetFrontAndBackStencilRef(state.Front.Reference, state.Back.Reference);
            if (root.Length != 0)
            {
                fixed (byte* data = root)
                { commands.SetGraphicsRoot32BitConstants(0, checked((uint)root.Length / 4), data, 0); }
            }
        }

        private void BindIndices(ComPtr<ID3D12GraphicsCommandList8> commands, NativeGpuRange indices, NativeGpuIndexFormat format)
        {
            LinearRecord linear = Owner.Owner.RequireLinear(indices.Region);
            var view = new IndexBufferView(checked(linear.GpuAddress + indices.Offset), checked((uint)indices.Size), format switch
            {
                NativeGpuIndexFormat.Uint16 => Format.FormatR16Uint,
                NativeGpuIndexFormat.Uint32 => Format.FormatR32Uint,
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            });
            commands.IASetIndexBuffer(in view);
        }
    }
}
