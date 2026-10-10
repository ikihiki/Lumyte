using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.Graphics.Browser;

internal static partial class BrowserInterop
{
    [JSImport("createDevice", "Lumyte.Graphics.Browser")]
    [return: JSMarshalAs<JSType.Promise<JSType.Object>>]
    internal static partial Task<JSObject> CreateDeviceAsync();

    [JSImport("getCapsJson", "Lumyte.Graphics.Browser")]
    internal static partial string GetCapsJson(JSObject handle);

    [JSImport("destroyDevice", "Lumyte.Graphics.Browser")]
    internal static partial void DestroyDevice(JSObject handle);

    [JSImport("createSurface", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateSurface(JSObject device, JSObject context);

    [JSImport("getSurfacePreferredFormat", "Lumyte.Graphics.Browser")]
    internal static partial int GetSurfacePreferredFormat(JSObject surface);

    [JSImport("configureSurface", "Lumyte.Graphics.Browser")]
    internal static partial void ConfigureSurface(JSObject surface, int width, int height, int format, int usage, int alphaMode);

    [JSImport("getSurfaceStatus", "Lumyte.Graphics.Browser")]
    internal static partial int GetSurfaceStatus(JSObject surface);

    [JSImport("acquireSurfaceTexture", "Lumyte.Graphics.Browser")]
    internal static partial JSObject AcquireSurfaceTexture(JSObject surface);

    [JSImport("releaseSurfaceTexture", "Lumyte.Graphics.Browser")]
    internal static partial void ReleaseSurfaceTexture(JSObject texture);

    [JSImport("unconfigureSurface", "Lumyte.Graphics.Browser")]
    internal static partial void UnconfigureSurface(JSObject surface);

    [JSImport("destroySurface", "Lumyte.Graphics.Browser")]
    internal static partial void DestroySurface(JSObject surface);

    [JSImport("createBuffer", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateBuffer(JSObject device, double size, int usage, int memory);

    [JSImport("mapBuffer", "Lumyte.Graphics.Browser")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    internal static partial Task MapBufferAsync(JSObject buffer, int memory);

    [JSImport("unmapBuffer", "Lumyte.Graphics.Browser")]
    internal static partial void UnmapBuffer(JSObject buffer);

    [JSImport("copyBufferFrom", "Lumyte.Graphics.Browser")]
    internal static partial void CopyBufferFrom(JSObject buffer, [JSMarshalAs<JSType.MemoryView>] Span<byte> source, int offset);

    [JSImport("copyBufferTo", "Lumyte.Graphics.Browser")]
    internal static partial void CopyBufferTo(JSObject buffer, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination, int offset);

    [JSImport("destroyBuffer", "Lumyte.Graphics.Browser")]
    internal static partial void DestroyBuffer(JSObject buffer);

    [JSImport("createTexture", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateTexture(JSObject device, int width, int height, int layers, int mips, int format, int usage);

    [JSImport("createTextureView", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateTextureView(JSObject texture, int dimension, int baseMip, int mipCount, int baseLayer, int layerCount);

    [JSImport("destroyTexture", "Lumyte.Graphics.Browser")]
    internal static partial void DestroyTexture(JSObject texture);

    [JSImport("createShader", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateShader(JSObject device, string code);

    [JSImport("createSampler", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateSampler(JSObject device, string descJson);

    [JSImport("createCommandEncoder", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateCommandEncoder(JSObject device);

    [JSImport("recordBufferCopy", "Lumyte.Graphics.Browser")]
    internal static partial void RecordBufferCopy(JSObject encoder, JSObject source, double sourceOffset, JSObject destination, double destinationOffset, double size);

    [JSImport("recordTextureCopy", "Lumyte.Graphics.Browser")]
    internal static partial void RecordTextureCopy(JSObject encoder, JSObject source, string sourceRegion, JSObject destination, string destinationRegion);

    [JSImport("recordBufferTextureCopy", "Lumyte.Graphics.Browser")]
    internal static partial void RecordBufferTextureCopy(JSObject encoder, JSObject buffer, double offset, double bytesPerRow, double rowsPerImage, JSObject texture, string region, bool upload);

    [JSImport("createRenderDescriptor", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateRenderDescriptor();

    [JSImport("addColorAttachment", "Lumyte.Graphics.Browser")]
    internal static partial void AddColorAttachment(JSObject desc, JSObject view, int load, int store, double red, double green, double blue, double alpha);

    [JSImport("addDepthStencilAttachment", "Lumyte.Graphics.Browser")]
    internal static partial void AddDepthStencilAttachment(JSObject desc, JSObject view, int depthLoad, int depthStore, double depthClear, bool stencil, int stencilLoad, int stencilStore, int stencilClear);

    [JSImport("setIndexBuffer", "Lumyte.Graphics.Browser")]
    internal static partial void SetIndexBuffer(JSObject pass, JSObject buffer, int format, double offset, double size);

    [JSImport("drawIndexed", "Lumyte.Graphics.Browser")]
    internal static partial void DrawIndexed(JSObject pass, double indices, double instances, double firstIndex, double baseVertex, double firstInstance);

    [JSImport("drawIndirect", "Lumyte.Graphics.Browser")]
    internal static partial void DrawIndirect(JSObject pass, JSObject buffer, double offset, bool indexed);

    [JSImport("dispatchIndirect", "Lumyte.Graphics.Browser")]
    internal static partial void DispatchIndirect(JSObject pass, JSObject buffer, double offset);

    [JSImport("beginRenderPass", "Lumyte.Graphics.Browser")]
    internal static partial JSObject BeginRenderPass(JSObject encoder, JSObject desc);

    [JSImport("beginComputePass", "Lumyte.Graphics.Browser")]
    internal static partial JSObject BeginComputePass(JSObject encoder);

    [JSImport("endRenderPass", "Lumyte.Graphics.Browser")]
    internal static partial void EndRenderPass(JSObject pass);

    [JSImport("endComputePass", "Lumyte.Graphics.Browser")]
    internal static partial void EndComputePass(JSObject pass);

    [JSImport("finishCommands", "Lumyte.Graphics.Browser")]
    internal static partial JSObject FinishCommands(JSObject encoder);

    [JSImport("createCommandList", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateCommandList();

    [JSImport("addCommand", "Lumyte.Graphics.Browser")]
    internal static partial void AddCommand(JSObject list, JSObject commands);

    [JSImport("submitCommands", "Lumyte.Graphics.Browser")]
    internal static partial JSObject SubmitCommands(JSObject device, JSObject list);

    [JSImport("getSubmissionStatus", "Lumyte.Graphics.Browser")]
    internal static partial int GetSubmissionStatus(JSObject submission);

    [JSImport("getSubmissionError", "Lumyte.Graphics.Browser")]
    internal static partial string GetSubmissionError(JSObject submission);

    [JSImport("waitSubmission", "Lumyte.Graphics.Browser")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    internal static partial Task WaitSubmissionAsync(JSObject submission);

    [JSImport("createGraphicsPipeline", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateGraphicsPipeline(JSObject device, JSObject vertex, string vertexEntry, JSObject? fragment, string fragmentEntry, string state, string formats, int depthFormat, string bindingKey = "");

    [JSImport("createComputePipeline", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateComputePipeline(JSObject device, JSObject shader, string entry, string bindingKey = "");

    [JSImport("setRenderPipeline", "Lumyte.Graphics.Browser")]
    internal static partial void SetRenderPipeline(JSObject pass, JSObject pipeline);

    [JSImport("setComputePipeline", "Lumyte.Graphics.Browser")]
    internal static partial void SetComputePipeline(JSObject pass, JSObject pipeline);

    [JSImport("setViewport", "Lumyte.Graphics.Browser")]
    internal static partial void SetViewport(JSObject pass, double x, double y, double width, double height, double min, double max);

    [JSImport("setScissor", "Lumyte.Graphics.Browser")]
    internal static partial void SetScissor(JSObject pass, double x, double y, double width, double height);

    [JSImport("setBlendConstant", "Lumyte.Graphics.Browser")]
    internal static partial void SetBlendConstant(JSObject pass, double r, double g, double b, double a);

    [JSImport("setStencilReference", "Lumyte.Graphics.Browser")]
    internal static partial void SetStencilReference(JSObject pass, int reference);

    [JSImport("draw", "Lumyte.Graphics.Browser")]
    internal static partial void Draw(JSObject pass, double vertices, double instances, double firstVertex, double firstInstance);

    [JSImport("dispatch", "Lumyte.Graphics.Browser")]
    internal static partial void Dispatch(JSObject pass, double x, double y, double z);

    [JSImport("createShaderBinding", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateShaderBinding(JSObject device, string key, bool compute, [JSMarshalAs<JSType.MemoryView>] Span<byte> root, [JSMarshalAs<JSType.MemoryView>] Span<byte> map);

    [JSImport("addShaderResource", "Lumyte.Graphics.Browser")]
    internal static partial void AddShaderResource(JSObject binding, int slot, int kind, JSObject resource, double offset, double size);

    [JSImport("finishShaderBinding", "Lumyte.Graphics.Browser")]
    internal static partial void FinishShaderBinding(JSObject device, JSObject binding);

    [JSImport("setShaderBinding", "Lumyte.Graphics.Browser")]
    internal static partial void SetShaderBinding(JSObject pass, JSObject binding);

    [JSImport("destroyShaderBinding", "Lumyte.Graphics.Browser")]
    internal static partial void DestroyShaderBinding(JSObject binding);
}
