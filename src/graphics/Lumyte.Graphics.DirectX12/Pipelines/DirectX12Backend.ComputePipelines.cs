using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    public NativeGpuComputePipelineHandle CreateComputePipeline(NativeGpuShaderProgram program)
    {
        VerifyAvailable();
        ArgumentNullException.ThrowIfNull(program);
        NativeGpuShaderCode code = program.Compute
            ?? throw new ArgumentException("A compute pipeline requires a compute program.", nameof(program));
        ComPtr<ID3D12PipelineState> pipeline = default;
        try
        {
            fixed (byte* pointer = code.Code.Span)
            {
                // DXIL contains its compiled entry point. The D3D12 PSO API does not
                // accept another entry-point selector or require retained source bytes.
                var description = new ComputePipelineStateDesc
                {
                    PRootSignature = computeRootSignature.Handle,
                    CS = new ShaderBytecode(pointer, checked((nuint)code.Code.Length)),
                };
                Check(device.CreateComputePipelineState(in description, out pipeline), "CreateComputePipelineState");
            }
            return new ComputePipelineRecord(this, pipeline);
        }
        catch { pipeline.Dispose(); throw; }
    }

    public void DestroyComputePipeline(NativeGpuComputePipelineHandle pipeline)
    {
        VerifyNotDisposed();
        ComputePipelineRecord record = RequireComputePipeline(pipeline);
        record.Disposed = true;
        record.Pipeline.Dispose();
    }

    private ComputePipelineRecord RequireComputePipeline(NativeGpuComputePipelineHandle pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not ComputePipelineRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The compute pipeline belongs to another device.", nameof(pipeline));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, pipeline);
        return record;
    }

    private void CreateComputeSupport()
    {
        var parameter = new RootParameter(RootParameterType.Type32BitConstants, null,
            ShaderVisibility.All, null, new RootConstants(0, 0, 64), null);
        var description = new RootSignatureDesc(1, &parameter, 0, null,
            RootSignatureFlags.CbvSrvUavHeapDirectlyIndexed | RootSignatureFlags.SamplerHeapDirectlyIndexed);
        ComPtr<ID3D10Blob> serialized = default;
        ComPtr<ID3D10Blob> errors = default;
        try
        {
            int result = api.SerializeRootSignature(in description, D3DRootSignatureVersion.Version1,
                ref serialized, ref errors);
            if (result < 0)
            {
                string diagnostic = errors.Handle is null ? "No serialization diagnostic." :
                    Marshal.PtrToStringAnsi((nint)errors.GetBufferPointer(), checked((int)errors.GetBufferSize()))
                    ?? "No serialization diagnostic.";
                throw new NativeGpuException($"SerializeRootSignature failed: {diagnostic}", result);
            }
            Check(device.CreateRootSignature(0, serialized.GetBufferPointer(), serialized.GetBufferSize(),
                out computeRootSignature), "CreateRootSignature(Native compute)");
            var argument = new IndirectArgumentDesc { Type = IndirectArgumentType.Dispatch };
            var signature = new CommandSignatureDesc(12, 1, &argument, 0);
            Check(device.CreateCommandSignature<ID3D12RootSignature, ID3D12CommandSignature>(&signature, default,
                out dispatchSignature), "CreateCommandSignature(Dispatch)");
        }
        finally { errors.Dispose(); serialized.Dispose(); }
    }

    private void DisposeComputeSupport()
    {
        dispatchSignature.Dispose();
        computeRootSignature.Dispose();
    }

    private sealed class ComputePipelineRecord(DirectX12Backend owner,
        ComPtr<ID3D12PipelineState> pipeline) : NativeGpuComputePipelineHandle
    {
        public DirectX12Backend Owner { get; } = owner;
        public ComPtr<ID3D12PipelineState> Pipeline = pipeline;
        public bool Disposed;
    }
}
