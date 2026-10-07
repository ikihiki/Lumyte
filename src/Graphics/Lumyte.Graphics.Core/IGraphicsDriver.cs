using System.Reflection;
namespace Lumyte.Graphics;

// Internal backend protocol; no backend types enter the public API.
internal interface IGraphicsDriver : IDisposable
{
    ulong MaxBufferSize { get; }
    object CreateBuffer(BufferDesc desc);
    object CreateTexture(TextureDesc desc);
    object CreateShader(Assembly assembly, string resourceName);
    object CreateComputePipeline(ComputePipelineDesc desc);
    object CreateGraphicsPipeline(GraphicsPipelineDesc desc);
    object CreateReference<T>(BufferSlice data) where T : unmanaged;
    void CopyBuffer<T>(BufferSlice destination, ReadOnlySpan<T> values) where T : unmanaged;
    uint[] ReadBuffer(Buffer buffer);
    object CreateCommandEncoder();
    object Submit(CommandBuffer commands);
    void DisposeHandle(object handle);
    ulong BufferSize(object handle);
    void ValidateSlice(object handle, ulong offset, ulong length);
    uint TextureWidth(object handle);
    uint TextureHeight(object handle);
    object CreateView(object handle);
    object CreateArguments(object handle, GpuReference<uint> data);
    object BeginRenderPass(object handle, RenderPassDesc desc);
    void Dispatch(object handle, ComputePipeline pipeline, ShaderArguments arguments, uint x, uint y, uint z);
    void RecordCopyBuffer(object handle, BufferSlice source, BufferSlice destination);
    void RecordCopyTextureToBuffer(object handle, Texture source, Buffer destination, uint bytesPerRow);
    object Finish(object handle);
    void SetPipeline(object handle, GraphicsPipeline pipeline);
    void SetViewport(object handle, Viewport viewport);
    void SetScissor(object handle, Scissor scissor);
    void SetIndexBuffer(object handle, BufferSlice indices, IndexFormat format);
    void Draw(object handle, DrawDesc desc);
    void DrawIndexed(object handle, IndexedDrawDesc desc);
    void End(object handle);
    bool IsCompleted(object handle);
    void Wait(object handle, CancellationToken cancellationToken);
    ValueTask WaitAsync(object handle, CancellationToken cancellationToken);
}
