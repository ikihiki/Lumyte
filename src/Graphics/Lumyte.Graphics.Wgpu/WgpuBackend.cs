using System.Reflection;
using G = Lumyte.Graphics;
using Lumyte.Graphics;
namespace Lumyte.Graphics.Wgpu;

/// <summary>Composition entry point; applications use Graphics.CreateDevice.</summary>
public static class WgpuBackend
{
    public static GraphicsDevice CreateDevice() => new(new WgpuDriver(WgpuDevice.Create()));
}

internal sealed class WgpuDriver(WgpuDevice device) : IGraphicsDriver
{
    private T Get<T>(G.GpuResource resource) where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Driver, this) || resource.Handle is not T value)
        throw new ArgumentException("Resource belongs to another device or backend.", nameof(resource));
        return value;
    }
    private BufferSlice Slice(G.BufferSlice slice) => Get<Buffer>(slice.Buffer).Slice(slice.Offset, slice.Length);
    public void Dispose() => device.Dispose();
    public ulong MaxBufferSize => device.MaxBufferSize;
    public object CreateBuffer(BufferDesc desc) => device.CreateBuffer(desc);
    public object CreateTexture(TextureDesc desc) => device.CreateTexture(desc);
    public object CreateShader(Assembly assembly, string resourceName) => device.CreateShader(assembly, resourceName);
    public object CreateComputePipeline(G.ComputePipelineDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        return device.CreateComputePipeline(new ComputePipelineDesc { Shader = Get<ShaderModule>(desc.Shader), EntryPoint = desc.EntryPoint });
    }
    public object CreateGraphicsPipeline(G.GraphicsPipelineDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        return device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = Get<ShaderModule>(desc.Shader), VertexEntry = desc.VertexEntry, FragmentEntry = desc.FragmentEntry });
    }
    public object CreateReference<T>(G.BufferSlice data) where T : unmanaged => device.CreateReference<T>(Slice(data));
    public void CopyBuffer<T>(G.BufferSlice destination, ReadOnlySpan<T> values) where T : unmanaged => device.CopyBuffer(Slice(destination), values);
    public uint[] ReadBuffer(G.Buffer buffer) => device.ReadBuffer(Get<Buffer>(buffer));
    public object CreateCommandEncoder() => device.CreateCommandEncoder();
    public object Submit(G.CommandBuffer commands) => device.Submit(Get<CommandBuffer>(commands));
    public void DisposeHandle(object handle) => ((IDisposable)handle).Dispose();
    public ulong BufferSize(object handle) => ((Buffer)handle).SizeInBytes;
    public void ValidateSlice(object handle, ulong offset, ulong length) => ((Buffer)handle).Slice(offset, length);
    public uint TextureWidth(object handle) => ((Texture)handle).Width;
    public uint TextureHeight(object handle) => ((Texture)handle).Height;
    public object CreateView(object handle) => ((Texture)handle).CreateView();
    public object CreateArguments(object handle, G.GpuReference<uint> data)
    {
        if (data.Handle is not GpuReference<uint> reference) throw new ArgumentException("Invalid GPU data reference.", nameof(data));
        return ((ComputePipeline)handle).CreateArguments(reference);
    }
    public object BeginRenderPass(object handle, G.RenderPassDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        return ((CommandEncoder)handle).BeginRenderPass(new RenderPassDesc { Target = Get<TextureView>(desc.Target), Load = desc.Load, Store = desc.Store, ClearValue = desc.ClearValue });
    }
    public void Dispatch(object handle, G.ComputePipeline pipeline, G.ShaderArguments arguments, uint x, uint y, uint z) => ((CommandEncoder)handle).Dispatch(Get<ComputePipeline>(pipeline), Get<ShaderArguments>(arguments), x, y, z);
    public void RecordCopyBuffer(object handle, G.BufferSlice source, G.BufferSlice destination) => ((CommandEncoder)handle).RecordCopyBuffer(Slice(source), Slice(destination));
    public void RecordCopyTextureToBuffer(object handle, G.Texture source, G.Buffer destination, uint bytesPerRow) => ((CommandEncoder)handle).RecordCopyTextureToBuffer(Get<Texture>(source), Get<Buffer>(destination), bytesPerRow);
    public object Finish(object handle) => ((CommandEncoder)handle).Finish();
    public void SetPipeline(object handle, G.GraphicsPipeline pipeline) => ((RenderEncoder)handle).SetPipeline(Get<GraphicsPipeline>(pipeline));
    public void SetViewport(object handle, Viewport viewport) => ((RenderEncoder)handle).SetViewport(viewport);
    public void SetScissor(object handle, Scissor scissor) => ((RenderEncoder)handle).SetScissor(scissor);
    public void SetIndexBuffer(object handle, G.BufferSlice indices, IndexFormat format) => ((RenderEncoder)handle).SetIndexBuffer(Slice(indices), format);
    public void Draw(object handle, DrawDesc desc) => ((RenderEncoder)handle).Draw(desc);
    public void DrawIndexed(object handle, IndexedDrawDesc desc) => ((RenderEncoder)handle).DrawIndexed(desc);
    public void End(object handle) => ((RenderEncoder)handle).End();
    public bool IsCompleted(object handle) => ((Submission)handle).IsCompleted;
    public void Wait(object handle, CancellationToken cancellationToken) => ((Submission)handle).Wait(cancellationToken);
    public ValueTask WaitAsync(object handle, CancellationToken cancellationToken) => ((Submission)handle).WaitAsync(cancellationToken);
}
