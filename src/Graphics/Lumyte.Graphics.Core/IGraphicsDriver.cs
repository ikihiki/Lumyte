using System.Reflection;
namespace Lumyte.Graphics;

// Internal backend protocol; no backend types enter the public API.
internal interface IGraphicsDriver : IDisposable, IBufferBackendContract, ICommandBufferBackendContract
{
    object CreateTexture(TextureDesc desc);
    object CreateShader(Assembly assembly, string resourceName);
    object CreateComputePipeline(ComputePipelineDesc desc);
    object CreateGraphicsPipeline(GraphicsPipelineDesc desc);
    void DisposeHandle(object handle);
    uint TextureWidth(object handle);
    uint TextureHeight(object handle);
    object CreateView(object handle);
    object CreateArguments(object handle, GpuReference<uint> data);
}
