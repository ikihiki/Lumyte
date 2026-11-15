using System.Runtime.CompilerServices;

using Lumyte.Graphics.Abstractions;

using Lumyte.Graphics.Hosting;

namespace Lumyte.Graphics.Benchmarks.Testing;

/// <summary>Managed recording test device: no rasterization, shader execution, native initialization or pixel readback.</summary>
public sealed class CpuGraphicsDevice : IGraphicDevice, IGraphicsDeviceOwner, IDisposable
{
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="CpuGraphicsDevice"/> class.Initializes a new instance of the <see cref="CpuGraphicsDevice"/> class.</summary>
    public CpuGraphicsDevice()
    {
        Queue = new CpuQueue(this);
    }

    /// <inheritdoc/>
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <inheritdoc/>
    public IGraphicDevice Device => this;

    /// <inheritdoc/>
    public GraphicsDeviceStatus Status => _disposed ? GraphicsDeviceStatus.Disposed : GraphicsDeviceStatus.Ready;

    /// <inheritdoc/>
    public GraphicsDeviceLoss? Loss => null;

    /// <inheritdoc/>
    public DeviceCaps Caps
    {
        get;
    }

 = new()
 {
     ShaderTarget = ShaderTarget.SpirV,
     MaxBufferSize = int.MaxValue,
     MaxStorageBufferBindingSize = int.MaxValue,
     MaxTextureDimension2D = 16384,
     MaxTextureArrayLayers = 2048,
     MaxColorAttachments = 8,
     MaxSampledTexturesPerStage = 4096,
     MaxSamplersPerStage = 4096,
     MaxStorageBuffersPerStage = 4096,
     MaxComputeInvocationsPerWorkgroup = 1024,
     MaxComputeWorkgroupsPerDimension = 65535,
     MaxComputeWorkgroupSizeX = 1024,
     MaxComputeWorkgroupSizeY = 1024,
     MaxComputeWorkgroupSizeZ = 64,
 }
;

    /// <inheritdoc/>
    public IGraphicsQueue Queue
    {
        get;
    }

    /// <summary>Gets the total instance count consumed by direct and indexed draw commands since reset.</summary>
    public long DrawnInstances
    {
        get;
        internal set;
    }

    /// <summary>Gets an immutable snapshot of bounded observations.</summary>
    public CpuGraphicsCounters Counters => new(Buffers, Textures, Shaders, Pipelines, Renders, Computes, DrawCount, DispatchCount, CopyCount, Barriers, Uploads, SubmitCount, Live, Hash);

    internal long Buffers
    {
        get;
        set;
    }

    internal long Textures
    {
        get;
        set;
    }

    internal long Shaders
    {
        get;
        set;
    }

    internal long Pipelines
    {
        get;
        set;
    }

    internal long Renders
    {
        get;
        set;
    }

    internal long Computes
    {
        get;
        set;
    }

    internal long DrawCount
    {
        get;
        set;
    }

    internal long DispatchCount
    {
        get;
        set;
    }

    internal long CopyCount
    {
        get;
        set;
    }

    internal long Barriers
    {
        get;
        set;
    }

    internal long Uploads
    {
        get;
        set;
    }

    internal long SubmitCount
    {
        get;
        set;
    }

    internal long Live
    {
        get;
        set;
    }

    internal ulong Hash
    {
        get;
        set;
    }

    internal ulong NextId
    {
        get;
        set;
    }

    /// <summary>Clears observations, preserving live resources and monotonic identifiers.</summary>
    public void ResetCounters()
    {
        Check();
        Buffers = Textures = Shaders = Pipelines = Renders = Computes = DrawCount = DispatchCount = CopyCount = Barriers = Uploads = SubmitCount = 0;
        Hash = 0;
        DrawnInstances = 0;
    }

    /// <inheritdoc/>
    public BufferLayout<T> GetBufferLayout<T>()

    where T : unmanaged
    {
        Check();
        ulong n = (ulong)Unsafe.SizeOf<T>();
        return new(n, n, 1, 1);
    }

    /// <inheritdoc/>
    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)

    where T : unmanaged
    {
        Check();
        if (desc.Count == 0 || desc.Usage == 0 || checked(desc.Count * (ulong)Unsafe.SizeOf<T>()) > Caps.MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(desc));
        }

        var r = new CpuBuffer<T>(this, desc);
        Buffers++;
        return r;
    }

    /// <inheritdoc/>
    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)

    where T : struct, IShaderArguments
    {
        Check();
        if (count == 0 || T.TransferSize <= 0 || checked(count * (ulong)T.TransferSize) > Caps.MaxBufferSize || memory is not (MemoryPreference.Automatic or MemoryPreference.Upload))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var r = new CpuShaderData<T>(this, artifact, count, memory);
        Buffers++;
        return r;
    }

    /// <inheritdoc/>
    public IGraphicsBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic, BufferUsage usage = BufferUsage.ShaderRead | BufferUsage.CopyDestination)

    where T : unmanaged
        => CreateBuffer<T>(new()
        {
            Count = count,
            Memory = memory,
            Usage = memory == MemoryPreference.Upload ? BufferUsage.CopySource : usage,
        });

    /// <inheritdoc/>
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        Check();
        if (desc.Width == 0 || desc.Height == 0 || desc.Width > Caps.MaxTextureDimension2D || desc.Height > Caps.MaxTextureDimension2D || desc.ArrayLayers == 0 || desc.MipLevels == 0 || desc.Usage == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(desc));
        }

        var r = new CpuTexture(this, desc);
        Textures++;
        return r;
    }

    /// <inheritdoc/>
    public IGraphicsSampler CreateSampler(SamplerDesc desc)
    {
        Check();
        return new CpuSampler(this, desc);
    }

    /// <inheritdoc/>
    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
    {
        Check();
        return new CpuTable(this, desc);
    }

    /// <inheritdoc/>
    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        Check();
        ArgumentNullException.ThrowIfNull(artifact);
        var r = new CpuShader(this, artifact);
        Shaders++;
        return r;
    }

    /// <inheritdoc/>
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        Own(desc.VertexShader);
        if (desc.FragmentShader != null)
        {
            Own(desc.FragmentShader);
        }

        var r = new CpuPipeline(this, desc);
        Pipelines++;
        return r;
    }

    /// <inheritdoc/>
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        Own(desc.ComputeShader);
        var r = new CpuComputePipeline(this, desc);
        Pipelines++;
        return r;
    }

    /// <inheritdoc/>
    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException("CPU benchmark device does not model GPU semaphore scheduling.");

    /// <inheritdoc/>
    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        Check();
        return new CpuCommands(this);
    }

    /// <inheritdoc/>
    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format)
    {
        Check();
        return new()
        {
            BytesPerTexel = format switch
            {
                TextureFormat.R8Unorm => 1,
                TextureFormat.Rg8Unorm or TextureFormat.R16Float => 2,
                TextureFormat.Rgba16Float => 8,
                TextureFormat.Depth24Stencil8 or TextureFormat.Depth32Float => throw new NotSupportedException("Depth copies are not supported."),
                _ => 4,
            },
            BufferOffsetAlignmentInBytes = 1,
            BytesPerRowAlignment = 1,
        }
       ;
    }

    /// <inheritdoc/>
    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
    {
        Check();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (Live != 0)
        {
            throw new InvalidOperationException("Dispose all owned resources before the CPU device.");
        }

        _disposed = true;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal void Check() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal CpuResource Own(object? value)
    {
        Check();
        if (value is not CpuResource r || r.Owner != this)
        {
            throw new ArgumentException("Resource belongs to another device.");
        }

        r.Check();
        return r;
    }

    internal void Mix(ulong value) => Hash = unchecked(((Hash ^ value) * 1099511628211UL) + 1469598103934665603UL);

    // Storage copies consume every byte; bounded samples keep checksum instrumentation O(1) per transfer.
    internal void Consume(ReadOnlySpan<byte> bytes)
    {
        Mix((ulong)bytes.Length);
        if (!bytes.IsEmpty)
        {
            Mix(bytes[0]);
            Mix(bytes[bytes.Length / 2]);
            Mix(bytes[^1]);
        }
    }
}
