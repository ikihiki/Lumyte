using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private ComPtr<ID3D12PipelineState> CreateMeshPipeline(RasterPipelineRecord pipeline, GraphicsPipelineStateDesc graphics)
    {
        ReadOnlySpan<byte> amplification = pipeline.Amplification is { } shader ? shader.Code.Span : default;
        fixed (byte* meshCode = pipeline.Mesh!.Code.Span)
        fixed (byte* amplificationCode = amplification)
        {
            // D3D12 stream subobjects start at pointer-aligned addresses. Their payload
            // follows the enum with the payload's native alignment, including trailing padding.
            Span<byte> stream = stackalloc byte[1024];
            stream.Clear();
            var builder = new PipelineStreamBuilder(stream);
            builder.Append(PipelineStateSubobjectType.RootSignature, (nint)graphics.PRootSignature, IntPtr.Size);
            builder.Append(PipelineStateSubobjectType.MS, new ShaderBytecode(meshCode, (nuint)pipeline.Mesh.Code.Length), IntPtr.Size);
            if (pipeline.Amplification is not null)
            { builder.Append(PipelineStateSubobjectType.As, new ShaderBytecode(amplificationCode, (nuint)amplification.Length), IntPtr.Size); }
            if (pipeline.Pixel is not null) { builder.Append(PipelineStateSubobjectType.PS, graphics.PS, IntPtr.Size); }
            builder.Append(PipelineStateSubobjectType.Blend, graphics.BlendState);
            builder.Append(PipelineStateSubobjectType.SampleMask, graphics.SampleMask);
            builder.Append(PipelineStateSubobjectType.Rasterizer, graphics.RasterizerState);
            builder.Append(PipelineStateSubobjectType.DepthStencil, graphics.DepthStencilState);
            builder.Append(PipelineStateSubobjectType.PrimitiveTopology, pipeline.Description.MeshOutputTopology switch
            {
                NativeGpuMeshOutputTopology.Line => PrimitiveTopologyType.Line,
                NativeGpuMeshOutputTopology.Triangle => PrimitiveTopologyType.Triangle,
                _ => throw new ArgumentOutOfRangeException(nameof(pipeline)),
            });
            var formats = new RTFormatArray { NumRenderTargets = graphics.NumRenderTargets };
            for (int index = 0; index < graphics.NumRenderTargets; index++) { formats.RTFormats[index] = graphics.RTVFormats[index]; }
            builder.Append(PipelineStateSubobjectType.RenderTargetFormats, formats);
            builder.Append(PipelineStateSubobjectType.DepthStencilFormat, graphics.DSVFormat);
            builder.Append(PipelineStateSubobjectType.SampleDesc, graphics.SampleDesc);
            fixed (byte* pointer = stream)
            {
                var description = new PipelineStateStreamDesc((nuint)builder.Size, pointer);
                ComPtr<ID3D12PipelineState> result = default;
                try
                {
                    Check(device10.CreatePipelineState(in description, out result), "CreatePipelineState(Native mesh)");
                    return result;
                }
                catch { result.Dispose(); throw; }
            }
        }
    }

    private ref struct PipelineStreamBuilder(Span<byte> stream)
    {
        private readonly Span<byte> stream = stream;
        public int Size { get; private set; }

        public void Append<T>(PipelineStateSubobjectType type, T value, int alignment = 4) where T : unmanaged
        {
            int payloadOffset = (sizeof(PipelineStateSubobjectType) + alignment - 1) & -alignment;
            int length = (payloadOffset + sizeof(T) + IntPtr.Size - 1) & -IntPtr.Size;
            Span<byte> subobject = stream.Slice(Size, length);
            MemoryMarshal.Write(subobject, in type);
            MemoryMarshal.Write(subobject[payloadOffset..], in value);
            Size += length;
        }
    }
}
