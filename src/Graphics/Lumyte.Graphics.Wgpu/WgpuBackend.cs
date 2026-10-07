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
    private T GetTextureResource<T>(object resource) where T : GpuResource
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource is not T value) throw new ArgumentException("Unsupported texture implementation.", nameof(resource));
        value.Check(device);
        return value;
    }
    private BufferSlice Slice(G.BufferRange slice)
    {
        if (slice.Buffer is not WgpuBuffer buffer) throw new ArgumentException("Resource belongs to another backend.");
        buffer.Check(device);
        return buffer.Slice(slice.Offset, slice.Length);
    }
    public void Dispose() => device.Dispose();
    public ulong MaxBufferSize => device.MaxBufferSize;
    public BufferLayout<T> GetBufferLayout<T>() where T : unmanaged => device.GetBufferLayout<T>();
    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc) where T : unmanaged => device.CreateBuffer(desc);
    public IGraphicsTexture CreateTexture(TextureDesc desc) => device.CreateTexture(desc);
    public object CreateSampler(SamplerDesc desc) => device.CreateSampler(desc);
    public object CreateSampledTextureReference(G.IGraphicsTextureView texture, G.Sampler sampler)
    {
        var view = GetTextureResource<TextureView>(texture); var state = Get<Sampler>(sampler);
        state.Check(device);
        if (!view.Texture.Usage.HasFlag(TextureUsage.Sampled)) throw new ArgumentException("Sampled texture usage is required.");
        return new SampledPair(view, state);
    }
    public MaterialResourceLayout GetMaterialLayout(object shader)
    {
        var module = (ShaderModule)shader; module.Check(device);
        return new(module.MaterialSchema ?? throw new NotSupportedException("Shader reflection does not declare the supported material ABI."));
    }
    public IGraphicsMaterialBindings CreateMaterialBindings(MaterialBindingsDesc desc, ReadOnlySpan<MaterialData> materials) => device.CreateMaterialBindings(desc, materials);
    public object CreateMaterialReference(G.BufferRange range)
    {
        var slice = Slice(range);
        if (!slice.Buffer.Usage.HasFlag(BufferUsage.ShaderRead) || slice.Buffer.Usage.HasFlag(BufferUsage.ShaderWrite)) throw new ArgumentException("Material reference requires shader-read-only storage.");
        return slice.Buffer.FindMaterial(slice.Offset, slice.Length) ?? throw new ArgumentException("Material range has no completed typed upload.");
    }
    public object CreateMaterialArguments(object pipeline, MaterialBufferReference materials)
    {
        if (materials.Handle is not MaterialRegion region) throw new ArgumentException("Invalid material reference.");
        return ((GraphicsPipeline)pipeline).CreateArguments(region);
    }
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
    public object CreateReference<T>(G.BufferRange data) where T : unmanaged => device.CreateReference<T>(Slice(data));
    public object CreateCommandEncoder() => device.CreateCommandEncoder();
    public object Submit(G.CommandBuffer commands) => device.Submit(Get<CommandBuffer>(commands));
    public void DisposeHandle(object handle) => ((IDisposable)handle).Dispose();
    public object CreateArguments(object handle, G.GpuReference<uint> data)
    {
        if (data.Handle is not GpuReference<uint> reference) throw new ArgumentException("Invalid GPU data reference.", nameof(data));
        return ((ComputePipeline)handle).CreateArguments(reference);
    }
    public object BeginRenderPass(object handle, G.RenderPassDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        return ((CommandEncoder)handle).BeginRenderPass(new RenderPassDesc { Target = GetTextureResource<TextureView>(desc.Target), Load = desc.Load, Store = desc.Store, ClearValue = desc.ClearValue });
    }
    public void Dispatch(object handle, G.ComputePipeline pipeline, G.ShaderArguments arguments, uint x, uint y, uint z) => ((CommandEncoder)handle).Dispatch(Get<ComputePipeline>(pipeline), Get<ShaderArguments>(arguments), x, y, z);
    public void RecordCopyBuffer(object handle, G.BufferRange source, G.BufferRange destination) => ((CommandEncoder)handle).RecordCopyBuffer(Slice(source), Slice(destination));
    public void RecordCopyBufferToTexture(object handle, G.BufferRange source, G.IGraphicsTexture destination, uint bytesPerRow) => ((CommandEncoder)handle).RecordCopyBufferToTexture(Slice(source), GetTextureResource<Texture>(destination), bytesPerRow);
    public void RecordCopyTextureToBuffer(object handle, G.IGraphicsTexture source, G.BufferRange destination, uint bytesPerRow) => ((CommandEncoder)handle).RecordCopyTextureToBuffer(GetTextureResource<Texture>(source), Slice(destination).Buffer, bytesPerRow);
    public object Finish(object handle) => ((CommandEncoder)handle).Finish();
    public void SetPipeline(object handle, G.GraphicsPipeline pipeline) => ((RenderEncoder)handle).SetPipeline(Get<GraphicsPipeline>(pipeline));
    public void SetViewport(object handle, Viewport viewport) => ((RenderEncoder)handle).SetViewport(viewport);
    public void SetScissor(object handle, Scissor scissor) => ((RenderEncoder)handle).SetScissor(scissor);
    public void SetIndexBuffer(object handle, G.BufferRange indices, IndexFormat format) => ((RenderEncoder)handle).SetIndexBuffer(Slice(indices), format);
    public void Draw(object handle, DrawDesc desc) => ((RenderEncoder)handle).Draw(desc);
    public void DrawWithArguments(object handle, G.ShaderArguments arguments, DrawDesc desc) => ((RenderEncoder)handle).Draw(Get<MaterialArguments>(arguments), desc);
    public void DrawIndexed(object handle, IndexedDrawDesc desc) => ((RenderEncoder)handle).DrawIndexed(desc);
    public void End(object handle) => ((RenderEncoder)handle).End();
    public bool IsCompleted(object handle) => ((Submission)handle).IsCompleted;
    public void Wait(object handle, CancellationToken cancellationToken) => ((Submission)handle).Wait(cancellationToken);
    public ValueTask WaitAsync(object handle, CancellationToken cancellationToken) => ((Submission)handle).WaitAsync(cancellationToken);
}
