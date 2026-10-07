using System.Reflection;
namespace Lumyte.Graphics;

/// <summary>Backend-independent owner of GPU resources and command submission.</summary>
public sealed class GraphicsDevice : IDisposable
{
    private readonly IGraphicsDriver _driver;
    internal GraphicsDevice(IGraphicsDriver driver) => _driver = driver;
    public ulong MaxBufferSize => _driver.MaxBufferSize;
    public Buffer CreateBuffer(BufferDesc desc) => new(_driver, _driver.CreateBuffer(desc));
    public Texture CreateTexture(TextureDesc desc) => new(_driver, _driver.CreateTexture(desc));
    public ShaderModule CreateShader(Assembly assembly, string resourceName) => new(_driver, _driver.CreateShader(assembly, resourceName));
    public ComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => new(_driver, _driver.CreateComputePipeline(desc));
    public GraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => new(_driver, _driver.CreateGraphicsPipeline(desc));
    public GpuReference<T> CreateReference<T>(BufferSlice data) where T : unmanaged => new(_driver.CreateReference<T>(data));
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

public sealed class Buffer : GpuResource
{
    internal IBufferBackendContract Backend { get; }
    internal Buffer(IGraphicsDriver driver, IBufferBackendContract backend) : base(driver, backend) => Backend = backend;
    public ulong SizeInBytes => Backend.SizeInBytes;
    /// <summary>Copies CPU bytes into this idle Upload buffer without recording GPU work.</summary>
    public void CopyFrom(ReadOnlySpan<byte> source) => Backend.CopyFrom(source, 0, SizeInBytes);
    public void CopyFrom<T>(ReadOnlySpan<T> values) where T : unmanaged => CopyFrom(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values));
    /// <summary>Copies this completed Readback buffer into caller-owned CPU memory.</summary>
    public void CopyTo(Span<byte> destination) => Backend.CopyTo(destination, 0, SizeInBytes);
    public BufferSlice Slice(ulong offset, ulong length)
    {
        Backend.ValidateRange(offset, length);
        return new(this, offset, length);
    }
}

public sealed class Texture : GpuResource
{
    internal Texture(IGraphicsDriver driver, object handle) : base(driver, handle) { }
    public uint Width => Driver.TextureWidth(Handle);
    public uint Height => Driver.TextureHeight(Handle);
    public TextureView CreateView() => new(Driver, Driver.CreateView(Handle));
}

public sealed class TextureView : GpuResource
{
    internal TextureView(IGraphicsDriver driver, object handle) : base(driver, handle) { }
}

public sealed class ShaderModule : GpuResource
{
    internal ShaderModule(IGraphicsDriver driver, object handle) : base(driver, handle) { }
}

public sealed class GraphicsPipeline : GpuResource
{
    internal GraphicsPipeline(IGraphicsDriver driver, object handle) : base(driver, handle) { }
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
    public void RecordCopyBuffer(BufferSlice source, BufferSlice destination) => _driver.RecordCopyBuffer(_handle, source, destination);
    /// <summary>Records a texture readback copy; records no submission.</summary>
    public void RecordCopyTextureToBuffer(Texture source, Buffer destination, uint bytesPerRow) => _driver.RecordCopyTextureToBuffer(_handle, source, destination, bytesPerRow);
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
    public void SetIndexBuffer(BufferSlice indices, IndexFormat format) => _driver.SetIndexBuffer(_handle, indices, format);
    public void Draw(uint vertexCount, uint instanceCount = 1) => Draw(new DrawDesc { VertexCount = vertexCount, InstanceCount = instanceCount });
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
