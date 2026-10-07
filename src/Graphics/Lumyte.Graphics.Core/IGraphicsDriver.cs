using System.Reflection;
namespace Lumyte.Graphics;

// Internal backend protocol; no backend types enter the public API.
internal interface IGraphicsDriver : IDisposable, ICommandBufferBackendContract
{
    ulong MaxBufferSize { get; }
    BufferLayout<T> GetBufferLayout<T>() where T : unmanaged;
    IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc) where T : unmanaged;
    object CreateReference<T>(BufferRange data) where T : unmanaged;
    IGraphicsTexture CreateTexture(TextureDesc desc);
    object CreateSampler(SamplerDesc desc);
    object CreateSampledTextureReference(IGraphicsTextureView texture, Sampler sampler);
    MaterialResourceLayout GetMaterialLayout(object shader);
    IGraphicsMaterialBindings CreateMaterialBindings(MaterialBindingsDesc desc, ReadOnlySpan<MaterialData> materials);
    object CreateMaterialReference(BufferRange range);
    object CreateMaterialArguments(object pipeline, MaterialBufferReference materials);
    object CreateShader(Assembly assembly, string resourceName);
    object CreateComputePipeline(ComputePipelineDesc desc);
    object CreateGraphicsPipeline(GraphicsPipelineDesc desc);
    void DisposeHandle(object handle);
    object CreateArguments(object handle, GpuReference<uint> data);
}
