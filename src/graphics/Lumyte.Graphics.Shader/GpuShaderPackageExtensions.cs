namespace Lumyte.Graphics;

/// <summary>Selects backend-ready shader IR from an asset package before entering the backend.</summary>
public static class GpuShaderPackageExtensions
{
    public static GpuRasterPipelineHandle CreateRasterPipeline(
        this IGpuBackend backend,
        GpuRasterPipelineDescription description,
        GpuShaderPackage package,
        string vertexEntryPoint,
        string pixelEntryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(package);
        GpuShaderBinary vertex = package.Select(
            backend.ShaderCodeFormat, GpuShaderStage.Vertex, vertexEntryPoint, expectedAbiHash.Span).ToBinary();
        GpuShaderBinary pixel = package.Select(
            backend.ShaderCodeFormat, GpuShaderStage.Pixel, pixelEntryPoint, expectedAbiHash.Span).ToBinary();
        return backend.CreateRasterPipeline(description, vertex, pixel);
    }

    public static GpuComputePipelineHandle CreateComputePipeline(
        this IGpuBackend backend,
        GpuShaderPackage package,
        string entryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(package);
        GpuShaderBinary compute = package.Select(
            backend.ShaderCodeFormat, GpuShaderStage.Compute, entryPoint, expectedAbiHash.Span).ToBinary();
        return backend.CreateComputePipeline(compute);
    }
}
