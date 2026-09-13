using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    public NativeGpuRasterPipelineHandle CreateRasterPipeline(NativeGpuRasterPipelineDescription description,
        NativeGpuShaderProgram program)
    {
        VerifyAvailable();
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(program);
        if (program.Mesh is not null && !meshLimits.HasValue)
        { throw new NotSupportedException("This Direct3D 12 device does not support mesh shaders."); }
        if (program.Vertex is null && program.Mesh is null)
        { throw new ArgumentException("A raster pipeline requires a vertex or mesh program.", nameof(program)); }
        if (program.Vertex is not null && (description.Topology is null || description.MeshOutputTopology is not null))
        {
            throw new ArgumentException("A vertex program requires an input topology and no mesh output topology.", nameof(description));
        }
        if (program.Mesh is not null && (description.Topology is not null || description.MeshOutputTopology is null))
        { throw new ArgumentException("A mesh program requires a mesh output topology and no input topology.", nameof(description)); }
        ArgumentNullException.ThrowIfNull(description.ColorTargets);
        // Native PSOs first need these inputs at Submit, so keep owned copies.
        // Copy the values now; no shader validation, PSO generation, or global cache occurs here.
        return new RasterPipelineRecord(this, description with { ColorTargets = description.ColorTargets.ToArray() },
            Copy(program.Vertex), Copy(program.Pixel), Copy(program.Mesh), Copy(program.Amplification));

        static NativeGpuShaderCode? Copy(NativeGpuShaderCode? code)
            => code is null ? null : code with { Code = code.Code.ToArray() };
    }

    public void DestroyRasterPipeline(NativeGpuRasterPipelineHandle pipeline)
    {
        VerifyNotDisposed();
        RasterPipelineRecord record = RequireRasterPipeline(pipeline);
        record.Disposed = true;
        foreach (ComPtr<ID3D12PipelineState> variant in record.Variants.Values) { variant.Dispose(); }
        record.Variants.Clear();
    }

    private RasterPipelineRecord RequireRasterPipeline(NativeGpuRasterPipelineHandle pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not RasterPipelineRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The raster pipeline belongs to another device.", nameof(pipeline));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, pipeline);
        return record;
    }

    // A narrow test seam for the per-pipeline lifetime/performance contract; no public cache API.
    internal int RasterPipelineVariantCount(NativeGpuRasterPipelineHandle pipeline) => RequireRasterPipeline(pipeline).Variants.Count;

    private ComPtr<ID3D12PipelineState> ResolveRasterPipeline(RasterPipelineRecord pipeline, NativeGpuDepthStencilState state)
    {
        RasterDepthStencilKey key = RasterDepthStencilKey.From(state);
        if (pipeline.Variants.TryGetValue(key, out ComPtr<ID3D12PipelineState> existing)) { return existing; }
        NativeGpuRasterPipelineDescription description = pipeline.Description;
        ComPtr<ID3D12PipelineState> native = default;
        try
        {
            ReadOnlySpan<byte> pixel = pipeline.Pixel is { } pixelShader ? pixelShader.Code.Span : default;
            ReadOnlySpan<byte> vertex = pipeline.Vertex is { } vertexShader ? vertexShader.Code.Span : default;
            fixed (byte* vertexCode = vertex)
            fixed (byte* pixelCode = pixel)
            {
                var pso = new GraphicsPipelineStateDesc
                {
                    PRootSignature = computeRootSignature.Handle,
                    VS = new(vertexCode, checked((nuint)vertex.Length)),
                    PS = new(pixelCode, checked((nuint)pixel.Length)),
                    BlendState = new(false, true),
                    SampleMask = uint.MaxValue,
                    RasterizerState = new(FillMode.Solid, RasterCullMode(description.CullMode),
                        description.FrontFace switch
                        {
                            NativeGpuFrontFace.Clockwise => false,
                            NativeGpuFrontFace.CounterClockwise => true,
                            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), "The pipeline has an unrepresentable front face."),
                        },
                        0, 0, 0, true, description.SampleCount > 1, false, 0, ConservativeRasterizationMode.Off),
                    DepthStencilState = RasterDepthDescription(key),
                    PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                    NumRenderTargets = checked((uint)description.ColorTargets.Length),
                    DSVFormat = description.DepthStencilFormat is { } format ? TextureFormat(format, false) : Format.FormatUnknown,
                    SampleDesc = new(description.SampleCount, 0),
                };
                // The native PSO contains an inline array of eight slots. Protect this host
                // structure while leaving format/sample/linkage legality to native creation.
                if (description.ColorTargets.Length > 8)
                {
                    throw new ArgumentException("The color targets do not fit the native PSO's inline array.", nameof(pipeline));
                }
                for (int index = 0; index < description.ColorTargets.Length; index++)
                {
                    NativeGpuColorTargetDescription color = description.ColorTargets[index];
                    pso.RTVFormats[index] = TextureFormat(color.Format, false);
                    pso.BlendState.RenderTarget[index] = RasterBlendDescription(color);
                }
                if (pipeline.Mesh is not null) { native = CreateMeshPipeline(pipeline, pso); }
                else { Check(device.CreateGraphicsPipelineState(in pso, out native), "CreateGraphicsPipelineState(Native raster)"); }
            }
            pipeline.Variants.Add(key, native);
            return native;
        }
        catch { native.Dispose(); throw; }
    }

    private sealed class RasterPipelineRecord(DirectX12Backend owner, NativeGpuRasterPipelineDescription description,
        NativeGpuShaderCode? vertex, NativeGpuShaderCode? pixel, NativeGpuShaderCode? mesh,
        NativeGpuShaderCode? amplification) : NativeGpuRasterPipelineHandle
    {
        public DirectX12Backend Owner { get; } = owner;
        public NativeGpuRasterPipelineDescription Description { get; } = description;
        public NativeGpuShaderCode? Vertex { get; } = vertex;
        public NativeGpuShaderCode? Pixel { get; } = pixel;
        public NativeGpuShaderCode? Mesh { get; } = mesh;
        public NativeGpuShaderCode? Amplification { get; } = amplification;
        public Dictionary<RasterDepthStencilKey, ComPtr<ID3D12PipelineState>> Variants { get; } = [];
        public bool Disposed;
    }
}
