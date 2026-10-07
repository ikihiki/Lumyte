namespace Lumyte.Graphics;

// Command recording and submission; owns GPU copy commands and command scopes.
internal interface ICommandBufferBackendContract
{
    object CreateCommandEncoder();
    object Submit(CommandBuffer commands);
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
