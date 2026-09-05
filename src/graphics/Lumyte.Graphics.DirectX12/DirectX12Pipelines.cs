using System.Runtime.InteropServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Device
{
    private const int MaximumShaderDescriptors = 64;

    public GpuRasterPipelineHandle CreateRasterPipeline(
        GpuRasterPipelineDescription description,
        GpuShaderPackage package,
        string vertexEntryPoint,
        string pixelEntryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(package);
        description.Validate();
        if (description.ColorTargets.Count > D3D12.SimultaneousRenderTargetCount)
        {
            throw new NotSupportedException("Direct3D 12 supports at most eight simultaneous color targets.");
        }
        if (description.SupportsDualSourceBlending)
        {
            throw new NotSupportedException("Direct3D 12 dual-source blending is not implemented by this raster slice.");
        }
        if (description.DepthFormat is { } depth && description.StencilFormat is { } stencil && depth != stencil)
        {
            throw new NotSupportedException("Direct3D 12 requires depth and stencil aspects to use one attachment format.");
        }

        GpuShaderArtifact vertex = package.Select(
            GpuShaderCodeFormat.Dxil, GpuShaderStage.Vertex, vertexEntryPoint, expectedAbiHash.Span);
        GpuShaderArtifact pixel = package.Select(
            GpuShaderCodeFormat.Dxil, GpuShaderStage.Pixel, pixelEntryPoint, expectedAbiHash.Span);
        ComPtr<ID3D12RootSignature> rootSignature = default;
        ComPtr<ID3D12PipelineState> pipeline = default;
        try
        {
            rootSignature = CreateStandardRootSignature();
            ReadOnlySpan<byte> vertexBytes = vertex.Payload.Span;
            ReadOnlySpan<byte> pixelBytes = pixel.Payload.Span;
            fixed (byte* vertexPointer = vertexBytes)
            fixed (byte* pixelPointer = pixelBytes)
            {
                var native = new GraphicsPipelineStateDesc
                {
                    PRootSignature = rootSignature.Handle,
                    VS = new(vertexPointer, checked((nuint)vertexBytes.Length)),
                    PS = new(pixelPointer, checked((nuint)pixelBytes.Length)),
                    BlendState = new(description.AlphaToCoverage, true),
                    SampleMask = uint.MaxValue,
                    RasterizerState = new(
                        FillMode.Solid,
                        ToCullMode(description.CullMode),
                        description.FrontFace == GpuFrontFace.CounterClockwise,
                        D3D12.DefaultDepthBias,
                        0,
                        0,
                        true,
                        description.SampleCount > 1,
                        false,
                        0,
                        ConservativeRasterizationMode.Off),
                    DepthStencilState = new(
                        description.DepthFormat is not null,
                        description.DepthFormat is not null ? DepthWriteMask.All : DepthWriteMask.Zero,
                        ComparisonFunc.LessEqual,
                        description.StencilFormat is not null,
                        byte.MaxValue,
                        byte.MaxValue,
                        DefaultStencilOperation(),
                        DefaultStencilOperation()),
                    PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                    NumRenderTargets = checked((uint)description.ColorTargets.Count),
                    DSVFormat = description.DepthFormat is { } depthFormat
                        ? ToDxgiFormat(depthFormat)
                        : description.StencilFormat is { } stencilFormat
                            ? ToDxgiFormat(stencilFormat)
                            : Format.FormatUnknown,
                    SampleDesc = new(description.SampleCount, 0),
                };
                for (int index = 0; index < description.ColorTargets.Count; index++)
                {
                    GpuColorTargetDescription target = description.ColorTargets[index];
                    native.RTVFormats[index] = ToDxgiFormat(target.Format);
                    native.BlendState.RenderTarget[index] = ToBlendDescription(description.EmbeddedBlend, target.WriteMask);
                }

                SilkMarshal.ThrowHResult(device.CreateGraphicsPipelineState<ID3D12PipelineState>(in native, out pipeline));
            }

            ulong id = NextHandle();
            pipelines.Add(id, new(pipeline, rootSignature, description.Topology));
            return new(id);
        }
        catch
        {
            pipeline.Dispose();
            rootSignature.Dispose();
            throw;
        }
    }

    public void DestroyRasterPipeline(GpuRasterPipelineHandle pipeline)
    {
        VerifyNotDisposed();
        if (!pipelines.Remove(pipeline.Value, out PipelineRecord? record))
        {
            throw new ArgumentException("Pipeline does not belong to this Direct3D 12 device.", nameof(pipeline));
        }
        record.Dispose();
    }

    public GpuComputePipelineHandle CreateComputePipeline(
        GpuShaderPackage package,
        string entryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(package);
        GpuShaderArtifact compute = package.Select(
            GpuShaderCodeFormat.Dxil, GpuShaderStage.Compute, entryPoint, expectedAbiHash.Span);
        ComPtr<ID3D12RootSignature> rootSignature = default;
        ComPtr<ID3D12PipelineState> pipeline = default;
        try
        {
            rootSignature = CreateStandardRootSignature();
            ReadOnlySpan<byte> bytes = compute.Payload.Span;
            fixed (byte* pointer = bytes)
            {
                var native = new ComputePipelineStateDesc
                {
                    PRootSignature = rootSignature.Handle,
                    CS = new ShaderBytecode(pointer, checked((nuint)bytes.Length)),
                };
                SilkMarshal.ThrowHResult(device.CreateComputePipelineState<ID3D12PipelineState>(in native, out pipeline));
            }

            ulong id = NextHandle();
            computePipelines.Add(id, new(pipeline, rootSignature));
            return new(id);
        }
        catch
        {
            pipeline.Dispose();
            rootSignature.Dispose();
            throw;
        }
    }

    public void DestroyComputePipeline(GpuComputePipelineHandle pipeline)
    {
        VerifyNotDisposed();
        if (!computePipelines.Remove(pipeline.Value, out ComputePipelineRecord? record))
        {
            throw new ArgumentException("Pipeline does not belong to this Direct3D 12 device.", nameof(pipeline));
        }
        record.Dispose();
    }

    private ComPtr<ID3D12RootSignature> CreateStandardRootSignature()
    {
        DescriptorRange* ranges = stackalloc DescriptorRange[5];
        ranges[0] = new(
            DescriptorRangeType.Srv,
            MaximumShaderDescriptors,
            0,
            0,
            D3D12.DescriptorRangeOffsetAppend);
        ranges[1] = new(
            DescriptorRangeType.Sampler,
            MaximumShaderDescriptors,
            0,
            1,
            D3D12.DescriptorRangeOffsetAppend);
        ranges[2] = new(
            DescriptorRangeType.Srv,
            MaximumShaderDescriptors,
            0,
            2,
            D3D12.DescriptorRangeOffsetAppend);
        ranges[3] = new(
            DescriptorRangeType.Uav,
            MaximumShaderDescriptors,
            0,
            3,
            D3D12.DescriptorRangeOffsetAppend);
        ranges[4] = new(
            DescriptorRangeType.Uav,
            MaximumShaderDescriptors,
            0,
            4,
            D3D12.DescriptorRangeOffsetAppend);
        RootParameter* parameters = stackalloc RootParameter[6];
        parameters[0] = new(
            RootParameterType.TypeDescriptorTable, null, ShaderVisibility.All,
            new RootDescriptorTable(1, &ranges[0]), null, null);
        parameters[1] = new(
            RootParameterType.TypeDescriptorTable, null, ShaderVisibility.All,
            new RootDescriptorTable(1, &ranges[1]), null, null);
        parameters[2] = new(
            RootParameterType.TypeDescriptorTable, null, ShaderVisibility.All,
            new RootDescriptorTable(1, &ranges[2]), null, null);
        parameters[3] = new(
            RootParameterType.TypeDescriptorTable, null, ShaderVisibility.All,
            new RootDescriptorTable(1, &ranges[3]), null, null);
        parameters[4] = new(
            RootParameterType.TypeDescriptorTable, null, ShaderVisibility.All,
            new RootDescriptorTable(1, &ranges[4]), null, null);
        parameters[5] = new(
            RootParameterType.Type32BitConstants, null, ShaderVisibility.All,
            null, new RootConstants(0, 0, GpuShaderBindingConvention.RootDataSize / sizeof(uint)), null);
        var description = new RootSignatureDesc(
            6,
            parameters,
            0,
            null,
            RootSignatureFlags.AllowInputAssemblerInputLayout);
        ComPtr<ID3D10Blob> serialized = default;
        ComPtr<ID3D10Blob> errors = default;
        ComPtr<ID3D12RootSignature> root = default;
        try
        {
            int result = api.SerializeRootSignature(
                in description,
                D3DRootSignatureVersion.Version1,
                ref serialized,
                ref errors);
            if (result < 0)
            {
                string message = errors.Handle is null
                    ? "Root signature serialization failed."
                    : Marshal.PtrToStringAnsi(
                        (nint)errors.Handle->GetBufferPointer(),
                        checked((int)errors.Handle->GetBufferSize())) ?? "Root signature serialization failed.";
                throw new InvalidOperationException(message);
            }
            SilkMarshal.ThrowHResult(device.CreateRootSignature<ID3D12RootSignature>(
                0,
                serialized.Handle->GetBufferPointer(),
                serialized.Handle->GetBufferSize(),
                out root));
            return root;
        }
        finally
        {
            errors.Dispose();
            serialized.Dispose();
        }
    }

    private static CullMode ToCullMode(GpuCullMode mode) => mode switch
    {
        GpuCullMode.None => CullMode.None,
        GpuCullMode.Front => CullMode.Front,
        GpuCullMode.Back => CullMode.Back,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static RenderTargetBlendDesc ToBlendDescription(
        GpuBlendDescription? description,
        GpuColorWriteMask targetMask)
    {
        if (description is null)
        {
            return new(
                false, false,
                Blend.One, Blend.Zero, BlendOp.Add,
                Blend.One, Blend.Zero, BlendOp.Add,
                LogicOp.Noop, (byte)targetMask);
        }

        return new(
            true, false,
            ToBlend(description.SourceColorFactor),
            ToBlend(description.DestinationColorFactor),
            ToBlendOperation(description.ColorOperation),
            ToBlend(description.SourceAlphaFactor),
            ToBlend(description.DestinationAlphaFactor),
            ToBlendOperation(description.AlphaOperation),
            LogicOp.Noop,
            (byte)(description.ColorWriteMask & targetMask));
    }

    private static Blend ToBlend(GpuBlendFactor factor) => factor switch
    {
        GpuBlendFactor.Zero => Blend.Zero,
        GpuBlendFactor.One => Blend.One,
        GpuBlendFactor.SourceColor => Blend.SrcColor,
        GpuBlendFactor.OneMinusSourceColor => Blend.InvSrcColor,
        GpuBlendFactor.DestinationColor => Blend.DestColor,
        GpuBlendFactor.OneMinusDestinationColor => Blend.InvDestColor,
        GpuBlendFactor.SourceAlpha => Blend.SrcAlpha,
        GpuBlendFactor.OneMinusSourceAlpha => Blend.InvSrcAlpha,
        GpuBlendFactor.DestinationAlpha => Blend.DestAlpha,
        GpuBlendFactor.OneMinusDestinationAlpha => Blend.InvDestAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor)),
    };

    private static BlendOp ToBlendOperation(GpuBlendOperation operation) => operation switch
    {
        GpuBlendOperation.Add => BlendOp.Add,
        GpuBlendOperation.Subtract => BlendOp.Subtract,
        GpuBlendOperation.ReverseSubtract => BlendOp.RevSubtract,
        GpuBlendOperation.Minimum => BlendOp.Min,
        GpuBlendOperation.Maximum => BlendOp.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static DepthStencilopDesc DefaultStencilOperation() => new(
        StencilOp.Keep,
        StencilOp.Keep,
        StencilOp.Keep,
        ComparisonFunc.Always);

    private sealed class PipelineRecord(
        ComPtr<ID3D12PipelineState> pipeline,
        ComPtr<ID3D12RootSignature> rootSignature,
        GpuPrimitiveTopology topology) : IDisposable
    {
        public ComPtr<ID3D12PipelineState> Pipeline = pipeline;
        public ComPtr<ID3D12RootSignature> RootSignature = rootSignature;
        public GpuPrimitiveTopology Topology { get; } = topology;
        public void Dispose()
        {
            Pipeline.Dispose();
            RootSignature.Dispose();
        }
    }

    private sealed class ComputePipelineRecord(
        ComPtr<ID3D12PipelineState> pipeline,
        ComPtr<ID3D12RootSignature> rootSignature) : IDisposable
    {
        public ComPtr<ID3D12PipelineState> Pipeline = pipeline;
        public ComPtr<ID3D12RootSignature> RootSignature = rootSignature;
        public void Dispose()
        {
            Pipeline.Dispose();
            RootSignature.Dispose();
        }
    }
}
