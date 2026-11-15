using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation;

/// <summary>Adds explicit managed validation to a borrowed backend device.</summary>
/// <remarks>Disposal never disposes the backend. All created children belong to this validation domain and must be disposed first. Calls require caller synchronization.</remarks>
public sealed partial class ValidationDevice : IGraphicDevice, IDisposable
{
    private readonly Dictionary<object, IValidationObject> _objects = new(ReferenceEqualityComparer.Instance);
    private readonly ValidationQueue _queue;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ValidationDevice"/> class.Creates a validation domain borrowing the backend and its queue.</summary>
    /// <param name="backend">The borrowed backend device; never disposed by this domain.</param>
    public ValidationDevice(IGraphicDevice backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (backend is ValidationDevice)
        {
            throw new ArgumentException("Cannot decorate a validation device twice.", nameof(backend));
        }

        Backend = backend;
        _queue = new ValidationQueue(this, backend.Queue);
        Backend.DeviceLost += ForwardDeviceLost;
    }

    /// <inheritdoc/>
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost;

    /// <inheritdoc/>
    public bool ValidationEnabled => true;

    /// <inheritdoc/>
    public GraphicsDeviceStatus Status => Backend.Status;

    /// <inheritdoc/>
    public GraphicsDeviceLoss? Loss => Backend.Loss;

    /// <inheritdoc/>
    public DeviceCaps Caps
    {
        get
        {
            EnsureActive();
            return Backend.Caps;
        }
    }

    /// <inheritdoc/>
    public IGraphicsQueue Queue
    {
        get
        {
            EnsureActive();
            return _queue;
        }
    }

    internal IGraphicDevice Backend { get; }

    internal GraphicsValidation Tracker { get; } = new();

    /// <inheritdoc/>
    /// <remarks>The validation domain must still be active, including while retiring device-loss work.</remarks>
    public async ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await Backend.WaitForRetirementAsync(cancellationToken).ConfigureAwait(false);
        if (Backend.Status == GraphicsDeviceStatus.Lost)
        {
            _queue.RetireUnknownAfterDeviceLoss();
            foreach (ValidationSwapchain swapchain in _objects.Values.OfType<ValidationSwapchain>().ToArray())
            {
                swapchain.RetireUnknownAfterDeviceLoss();
            }
        }
    }

    /// <summary>Adopts a newly-created surface as borrowed; no historical acquisition state is imported.</summary>
    /// <param name="raw">The borrowed backend surface.</param>
    /// <returns>The canonical validation surface wrapper.</returns>
    public IGraphicsSurface WrapSurface(IGraphicsSurface raw)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(raw);
        if (raw is IValidationObject)
        {
            throw new ArgumentException("Surface is already validated.", nameof(raw));
        }

        if (_objects.TryGetValue(raw, out IValidationObject? existing))
        {
            existing.ValidateAlive();
            return (IGraphicsSurface)existing;
        }

        return new ValidationSurface(this, raw);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Tracker.ValidateRelease(this);
        Tracker.Release(this);
        Backend.DeviceLost -= ForwardDeviceLost;
        _disposed = true;
    }

    /// <inheritdoc/>
    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged
    {
        EnsureActive();
        return Backend.GetBufferLayout<T>();
    }

    /// <inheritdoc/>
    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format)
    {
        EnsureActive();
        return Backend.GetTextureCopyLayout(format);
    }

    /// <inheritdoc/>
    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged
    {
        EnsureActive();
        return OwnCreated(Backend.CreateBuffer(desc), raw => new ValidationBuffer<T>(this, raw));
    }

    /// <inheritdoc/>
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        EnsureActive();
        return OwnCreated(Backend.CreateTexture(desc), raw => new ValidationTexture(this, raw, this));
    }

    /// <inheritdoc/>
    public IGraphicsSampler CreateSampler(SamplerDesc desc)
    {
        EnsureActive();
        return OwnCreated(Backend.CreateSampler(desc), raw => new ValidationSampler(this, raw));
    }

    /// <inheritdoc/>
    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
    {
        EnsureActive();
        return OwnCreated(Backend.CreateArgumentTable(desc), raw => new ValidationArgumentTable(this, raw));
    }

    /// <inheritdoc/>
    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        EnsureActive();
        return OwnCreated(Backend.CreateShader(artifact), raw => new ValidationShader(this, raw));
    }

    /// <inheritdoc/>
    public IGraphicsSemaphore CreateSemaphore()
    {
        EnsureActive();
        return OwnCreated(Backend.CreateSemaphore(), raw => new ValidationSemaphore(this, raw));
    }

    /// <inheritdoc/>
    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        EnsureActive();
        return OwnCreated(Backend.CreateCommandBuffer(desc), raw => new ValidationCommandBuffer(this, raw, desc));
    }

    /// <inheritdoc/>
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(desc);
        GraphicsPipelineDesc native = desc with { VertexShader = Unwrap(desc.VertexShader), FragmentShader = desc.FragmentShader is null ? null : Unwrap(desc.FragmentShader) };
        return OwnCreated(Backend.CreateGraphicsPipeline(native), raw => new ValidationPipeline(this, raw, desc));
    }

    /// <inheritdoc/>
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(desc);
        return OwnCreated(Backend.CreateComputePipeline(desc with { ComputeShader = Unwrap(desc.ComputeShader) }), raw => new ValidationComputePipeline(this, raw, desc));
    }

    /// <inheritdoc/>
    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderArguments
    {
        EnsureActive();
        var diagnostic = ShaderDataLayout.Data(artifact, Caps.ShaderTarget, T.ShaderTypeName);
        T diagnosticValue = default;
        diagnostic.ValidateTransferLayout(T.TransferSize, ShaderTransfer.Fields(in diagnosticValue), T.ShaderTypeName);
        diagnostic.ValidateReferenceCount(T.ReferenceCount);
        return OwnCreated(Backend.CreateShaderDataBuffer<ValidationArguments<T>>(artifact, count, memory), raw => new ValidationShaderDataBuffer<T>(this, raw, diagnostic));
    }

    /// <inheritdoc/>
    public IGraphicsBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic, BufferUsage usage = BufferUsage.ShaderRead | BufferUsage.CopyDestination)
        where T : unmanaged
    {
        EnsureActive();
        ShaderDataLayout.ValidateUnmanaged<T>(artifact, Caps.ShaderTarget);
        return OwnCreated(Backend.CreateShaderDataBuffer<T>(artifact, count, memory, usage), raw => new ValidationBuffer<T>(this, raw));
    }

    internal void EnsureActive() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal T Unwrap<T>(T wrapper)
        where T : class
        => (T)UnwrapObject(wrapper);

    internal object UnwrapObject(object wrapper)
    {
        EnsureActive();
        if (wrapper is not IValidationObject value || !ReferenceEquals(value.Device, this))
        {
            throw new ArgumentException("Validation: resource is not owned by this validation domain.", nameof(wrapper));
        }

        value.ValidateAlive();
        return value.RawObject;
    }

    internal void Register(IValidationObject wrapper, object raw, object parent, bool owned)
    {
        EnsureActive();
        if (_objects.ContainsKey(raw))
        {
            throw new InvalidOperationException("Validation: raw resource already has a wrapper.");
        }

        _objects.Add(raw, wrapper);
        try
        {
            Tracker.Register(wrapper, parent);
        }
        catch
        {
            _objects.Remove(raw);
            throw;
        }
    }

    internal TWrapper OwnCreated<TRaw, TWrapper>(TRaw raw, Func<TRaw, TWrapper> wrap, Action<TRaw>? retainAfterFailedDispose = null)
        where TRaw : class, IDisposable
    {
        if (_objects.ContainsKey(raw))
        {
            throw new InvalidOperationException("Validation: backend factory returned an already-owned resource.");
        }

        HashSet<object> existing = new(_objects.Keys, ReferenceEqualityComparer.Instance);
        try
        {
            return wrap(raw);
        }
        catch
        {
            foreach (object added in _objects.Keys.Where(key => !existing.Contains(key)).ToArray())
            {
                if (_objects.Remove(added, out IValidationObject? partial))
                {
                    Tracker.CancelRegistration(partial);
                }
            }

            try
            {
                raw.Dispose();
            }
            catch
            {
                retainAfterFailedDispose?.Invoke(raw);
            }

            throw;
        }
    }

    internal void Forget(object raw, IValidationObject wrapper)
    {
        if (_objects.TryGetValue(raw, out IValidationObject? existing) && ReferenceEquals(existing, wrapper))
        {
            _objects.Remove(raw);
        }
    }

    internal object Resolve(object raw)
    {
        EnsureActive();
        if (!_objects.TryGetValue(raw, out IValidationObject? value))
        {
            throw new ArgumentException("Validation: unknown backend resource.", nameof(raw));
        }

        value.ValidateAlive();
        return value;
    }

    internal object ResourceIdentity(object wrapper)
    {
        UnwrapObject(wrapper);
        return wrapper is IGraphicsTextureView view ? view.Texture : wrapper;
    }

    private void ForwardDeviceLost(object? sender, GraphicsDeviceLostEventArgs args) => DeviceLost?.Invoke(this, args);
}
