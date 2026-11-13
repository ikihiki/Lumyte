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

    public DeviceCaps Caps { get; init; } = new() { MaxTextureDimension2D = 4096, MaxColorAttachments = 8, ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30 };

    public IGraphicsQueue Queue => throw new InvalidOperationException("Drawing preparation must not access the queue.");

    internal List<ArgumentTableDesc> Tables { get; } = [];

    internal Func<ArgumentTableDesc, IArgumentTable>? TableFactory { get; init; }

    internal List<DrawingInstruction> Instructions { get; } = [];

    internal int ActiveTables { get; private set; }

    internal int Writes { get; private set; }

    internal int FailWrite { get; init; }

    internal bool FailTableDisposeOnce { get; init; }

    internal int NativeBuffers { get; private set; }

    internal int Copies { get; private set; }

    internal int FailCopy { get; init; }

    internal long CopiedBytes { get; private set; }

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Drawing preparation must not wait for device retirement.");

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => new((ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), 1, 1);

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged
    {
        NativeBuffers++;
        return new Buffer<T>(this, desc);
    }

    public IGraphicsTexture CreateTexture(TextureDesc desc) => new DispatchTexture { Width = desc.Width, Height = desc.Height, MipLevels = desc.MipLevels, ArrayLayers = desc.ArrayLayers, Format = desc.Format, Usage = desc.Usage };

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => new DispatchSampler();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
    {
        Tables.Add(desc);
        if (TableFactory is not null)
        {
            return TableFactory(desc);
        }

        ActiveTables++;
        return new Table(this, desc);
    }

    public IGraphicsShader CreateShader(ShaderArtifact artifact) => new Shader(artifact);

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new DispatchCommands();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => new Pipeline(desc);

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : IShaderData
    {
        NativeBuffers++;
        return new ShaderBuffer<T>(this, count, memory);
    }

    internal static IArgumentTable Owner<T>(IGpuRef<T> reference) => ((Reference<T>)reference).Owner;

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

    private sealed class Reference<T>(IArgumentTable owner, object resource, uint slot, ulong count, ulong offset = 0, ulong size = 0, ulong registrationOffset = 0, ulong registrationSize = 0, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly) : IGpuRef<T>, IShaderReference
    {
        public IArgumentTable Owner => owner;

        public object Table => owner;

        public ShaderBufferAccess BufferAccess => access;

        public object Resource => resource;

        public uint Slot => slot;

        public ulong Count => count;

        public ulong OffsetInBytes => offset;

        public ulong SizeInBytes => size;

        public ulong RegistrationOffsetInBytes => registrationOffset;

        public ulong RegistrationSizeInBytes => registrationSize;

        public IGpuRef<T> GetElement(ulong index)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
            ulong stride = size / count;
            return new Reference<T>(owner, resource, slot, 1, offset + (index * stride), stride, registrationOffset, registrationSize, access);
        }
    }

    private sealed class Table(ProfilingDevice device, ArgumentTableDesc desc) : IArgumentTable
    {
        private bool _disposed;
        private bool _failedDispose;

        public uint TextureCapacity => desc.TextureCapacity;

        public uint SamplerCapacity => desc.SamplerCapacity;

        public uint BufferCapacity => desc.BufferCapacity;

        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => Write<IGraphicsTextureView>(slot, TextureCapacity, 1, view);

        public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => Write<IGraphicsSampler>(slot, SamplerCapacity, 1, sampler);

        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
            where T : unmanaged => Write<T>(slot, BufferCapacity, range.Count, range.Buffer, range.OffsetInBytes, range.SizeInBytes, access);

        public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
            where T : IShaderData => Write<T>(slot, BufferCapacity, range.Count, range.Buffer, range.Offset * range.Buffer.ShaderElementStrideInBytes, range.Count * range.Buffer.ShaderElementStrideInBytes);

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
            if (_disposed)
            {
                return;
            }

            if (device.FailTableDisposeOnce && !_failedDispose)
            {
                _failedDispose = true;
                throw new InvalidOperationException("Injected table disposal failure.");
            }

            _disposed = true;
            device.ActiveTables--;
        }

        private IGpuRef<T> Write<T>(uint slot, uint capacity, ulong count, object resource, ulong offset = 0, ulong size = 0, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (slot >= capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            device.Writes++;
            if (device.Writes == device.FailWrite)
            {
                throw new InvalidOperationException("Injected registration failure.");
            }

            return new Reference<T>(this, resource, slot, count, offset, size, offset, size, access);
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
        where T : IShaderData
    {
        public ulong Count => count;

        public ulong SizeInBytes => count * ShaderElementStrideInBytes;

        public ulong ShaderElementStrideInBytes => 144;

        public MemoryPreference Memory => memory;

        public bool IsMapped { get; private set; }

        public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0)
        {
            device.Copy(source.Length * 144L);
            foreach (T item in source)
            {
                if (item is DrawingInstruction instruction)
                {
                    device.Instructions.Add(instruction);
                }
            }
        }

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
