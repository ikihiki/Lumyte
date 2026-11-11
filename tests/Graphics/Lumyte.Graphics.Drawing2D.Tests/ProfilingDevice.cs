using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class ProfilingDevice : IGraphicDevice
{
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add
        {
        }

        remove
        {
        }
    }

    public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

    public GraphicsDeviceLoss? Loss => null;

    public DeviceCaps Caps { get; init; } = new() { MaxTextureDimension2D = 4096, ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30 };

    public IGraphicsQueue Queue => throw new InvalidOperationException("Drawing preparation must not access the queue.");

    internal int Copies { get; private set; }

    internal int FailCopy { get; init; }

    internal long CopiedBytes { get; private set; }

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Drawing preparation must not wait for device retirement.");

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => new((ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), 1, 1);

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => new Buffer<T>(this, desc);

    public IGraphicsTexture CreateTexture(TextureDesc desc) => new DispatchTexture();

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => new DispatchSampler();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => new Table();

    public IGraphicsShader CreateShader(ShaderArtifact artifact) => new Shader(artifact);

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new DispatchCommands();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => new Pipeline(desc);

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData => new ShaderBuffer<T>(this, count, memory);

    private static async ValueTask MapAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Copy(long bytes)
    {
        Copies++;
        if (Copies == FailCopy)
        {
            throw new InvalidOperationException("Injected host copy failure.");
        }

        CopiedBytes += bytes;
    }

    private sealed class Shader(ShaderArtifact artifact) : IGraphicsShader
    {
        public ShaderArtifact Artifact => artifact;

        public void Dispose()
        {
        }
    }

    private sealed class Pipeline(GraphicsPipelineDesc desc) : IGraphicsPipeline
    {
        public GraphicsPipelineDesc Desc => desc;

        public void Dispose()
        {
        }
    }

    private sealed class Reference<T>(ulong count) : IGpuRef<T>
    {
        public ulong Count => count;

        public IGpuRef<T> GetElement(ulong index) => new Reference<T>(1);
    }

    private sealed class Table : IArgumentTable
    {
        public uint TextureCapacity => 2048;

        public uint SamplerCapacity => 2048;

        public uint BufferCapacity => 2;

        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => new Reference<IGraphicsTextureView>(1);

        public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => new Reference<IGraphicsSampler>(1);

        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
            where T : unmanaged => new Reference<T>(range.Count);

        public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
            where T : struct, IShaderData => new Reference<T>(range.Count);

        public void ReleaseTexture(uint slot)
        {
        }

        public void ReleaseSampler(uint slot)
        {
        }

        public void ReleaseBuffer(uint slot)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Buffer<T>(ProfilingDevice device, BufferDesc<T> desc) : IGraphicsBuffer<T>
        where T : unmanaged
    {
        public ulong SizeInBytes => Layout.GetSizeInBytes(Count);

        public BufferLayout<T> Layout => device.GetBufferLayout<T>();

        public ulong Count => desc.Count;

        public BufferUsage Usage => desc.Usage;

        public MemoryPreference Memory => desc.Memory;

        public bool IsMapped { get; private set; }

        public void ValidateByteRange(ulong offset, ulong length)
        {
            if (offset > SizeInBytes || length > SizeInBytes - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
        }

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => device.Copy(source.Length);

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

        public ValueTask MapAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsMapped = true;
            return ProfilingDevice.MapAsync(cancellationToken);
        }

        public void Unmap() => IsMapped = false;

        public BufferSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<T> source) => device.Copy((long)Layout.GetSizeInBytes((ulong)source.Length));

        public void CopyTo(Span<T> destination) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class ShaderBuffer<T>(ProfilingDevice device, ulong count, MemoryPreference memory) : IGraphicsShaderDataBuffer<T>
        where T : struct, IShaderData
    {
        public ulong Count => count;

        public ulong SizeInBytes => count * ShaderElementStrideInBytes;

        public ulong ShaderElementStrideInBytes => 144;

        public MemoryPreference Memory => memory;

        public bool IsMapped { get; private set; }

        public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0) => device.Copy(source.Length * 144L);

        public ValueTask MapAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsMapped = true;
            return ProfilingDevice.MapAsync(cancellationToken);
        }

        public void Unmap() => IsMapped = false;

        public ShaderDataSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void Dispose()
        {
        }
    }
}
