using System.Reflection;
namespace Lumyte.Graphics;

/// <summary>Backend-independent owner of GPU resources and command submission.</summary>
public sealed class GraphicsDevice : IDisposable
{
    private readonly IGraphicsDriver _driver;
    internal GraphicsDevice(IGraphicsDriver driver) => _driver = driver;
    public ulong MaxBufferSize => _driver.MaxBufferSize;
    public BufferLayout<T> GetBufferLayout<T>() where T : unmanaged => _driver.GetBufferLayout<T>();
    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc) where T : unmanaged => _driver.CreateBuffer(desc);
    public IGraphicsTexture CreateTexture(TextureDesc desc) => _driver.CreateTexture(desc);
    public Sampler CreateSampler(SamplerDesc desc) => new(_driver, _driver.CreateSampler(desc));
    public SampledTexture2DReference CreateSampledTexture2DReference(IGraphicsTextureView texture, Sampler sampler) => new(_driver.CreateSampledTextureReference(texture, sampler));
    public IGraphicsMaterialBindings CreateMaterialBindings(MaterialBindingsDesc desc, ReadOnlySpan<MaterialData> materials) => _driver.CreateMaterialBindings(desc, materials);
    public MaterialBufferReference CreateMaterialReference(BufferSlice<byte> range) => new(_driver.CreateMaterialReference(range.Range));
    public ShaderModule CreateShader(Assembly assembly, string resourceName) => new(_driver, _driver.CreateShader(assembly, resourceName));
    public ComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => new(_driver, _driver.CreateComputePipeline(desc));
    public GraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => new(_driver, _driver.CreateGraphicsPipeline(desc));
    public GpuReference<T> CreateReference<T>(BufferSlice<T> data) where T : unmanaged => new(_driver.CreateReference<T>(data.Range));
    public CommandEncoder CreateCommandEncoder() => new(_driver, _driver.CreateCommandEncoder());
    public Submission Submit(CommandBuffer commands) => new(_driver, _driver.Submit(commands));
    public void Dispose() => _driver.Dispose();
}

public abstract class GpuResource : IDisposable
{
    internal IGraphicsDriver Driver { get; }
    internal object Handle { get; }
    internal GpuResource(IGraphicsDriver driver, object handle) => (Driver, Handle) = (driver, handle);
    public void Dispose() => Driver.DisposeHandle(Handle);
}

public sealed class ShaderModule : GpuResource
{
    internal ShaderModule(IGraphicsDriver driver, object handle) : base(driver, handle) { }
    public MaterialResourceLayout GetMaterialResourceLayout() => Driver.GetMaterialLayout(Handle);
}

public sealed class GraphicsPipeline : GpuResource
{
    internal GraphicsPipeline(IGraphicsDriver driver, object handle) : base(driver, handle) { }
    public ShaderArguments CreateArguments(MaterialBufferReference materials) => new(Driver, Driver.CreateMaterialArguments(Handle, materials));
}

public sealed class ComputePipeline : GpuResource
{
    internal ComputePipeline(IGraphicsDriver driver, object handle) : base(driver, handle) { }
    public ShaderArguments CreateArguments(GpuReference<uint> data) => new(Driver, Driver.CreateArguments(Handle, data));
}

public sealed class ShaderArguments : GpuResource
{
    internal ShaderArguments(IGraphicsDriver driver, object handle) : base(driver, handle) { }
}

public sealed class CommandBuffer : GpuResource
{
    internal CommandBuffer(IGraphicsDriver driver, object handle) : base(driver, handle) { }
}

/// <summary>Records copies, compute work and scoped rendering.</summary>
public sealed class CommandEncoder : IDisposable
{
    private readonly IGraphicsDriver _driver;
    private readonly object _handle;
    internal CommandEncoder(IGraphicsDriver driver, object handle) => (_driver, _handle) = (driver, handle);
    public RenderEncoder BeginRenderPass(RenderPassDesc desc) => new(_driver, _driver.BeginRenderPass(_handle, desc));
    public void Dispatch(ComputePipeline pipeline, ShaderArguments arguments, uint x, uint y = 1, uint z = 1) => _driver.Dispatch(_handle, pipeline, arguments, x, y, z);
    /// <summary>Records a GPU copy; execution requires Finish and explicit Submit.</summary>
    public void RecordCopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination) where TSource : unmanaged where TDestination : unmanaged => _driver.RecordCopyBuffer(_handle, source.Range, destination.Range);
    public void RecordCopyBufferToTexture(BufferSlice<byte> source, IGraphicsTexture destination, uint bytesPerRow) => _driver.RecordCopyBufferToTexture(_handle, source.Range, destination, bytesPerRow);
    /// <summary>Records a texture readback copy; records no submission.</summary>
    public void RecordCopyTextureToBuffer<T>(IGraphicsTexture source, IGraphicsBuffer<T> destination, uint bytesPerRow) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(destination);
        _driver.RecordCopyTextureToBuffer(_handle, source, destination.Slice(0, destination.Count).Range, bytesPerRow);
    }
    public CommandBuffer Finish() => new(_driver, _driver.Finish(_handle));
    public void Dispose() => _driver.DisposeHandle(_handle);
}

public sealed class RenderEncoder : IDisposable
{
    private readonly IGraphicsDriver _driver;
    private readonly object _handle;
    internal RenderEncoder(IGraphicsDriver driver, object handle) => (_driver, _handle) = (driver, handle);
    public void SetPipeline(GraphicsPipeline pipeline) => _driver.SetPipeline(_handle, pipeline);
    public void SetViewport(Viewport viewport) => _driver.SetViewport(_handle, viewport);
    public void SetScissor(Scissor scissor) => _driver.SetScissor(_handle, scissor);
    public void SetIndexBuffer<T>(BufferSlice<T> indices, IndexFormat format) where T : unmanaged
    {
        if ((format == IndexFormat.Uint16 && typeof(T) != typeof(ushort)) ||
            (format == IndexFormat.Uint32 && typeof(T) != typeof(uint)))
            throw new ArgumentException("Index element type must match the selected format.", nameof(indices));
        _driver.SetIndexBuffer(_handle, indices.Range, format);
    }
    public void Draw(uint vertexCount, uint instanceCount = 1) => Draw(new DrawDesc { VertexCount = vertexCount, InstanceCount = instanceCount });
    public void Draw(ShaderArguments arguments, DrawDesc desc) => _driver.DrawWithArguments(_handle, arguments, desc);
    public void Draw(DrawDesc desc) => _driver.Draw(_handle, desc);
    public void DrawIndexed(IndexedDrawDesc desc) => _driver.DrawIndexed(_handle, desc);
    public void End() => _driver.End(_handle);
    public void Dispose() => _driver.DisposeHandle(_handle);
}

public sealed class Submission
{
    private readonly IGraphicsDriver _driver;
    private readonly object _handle;
    internal Submission(IGraphicsDriver driver, object handle) => (_driver, _handle) = (driver, handle);
    public bool IsCompleted => _driver.IsCompleted(_handle);
    public void Wait(CancellationToken cancellationToken = default) => _driver.Wait(_handle, cancellationToken);
    public ValueTask WaitAsync(CancellationToken cancellationToken = default) => _driver.WaitAsync(_handle, cancellationToken);
}
