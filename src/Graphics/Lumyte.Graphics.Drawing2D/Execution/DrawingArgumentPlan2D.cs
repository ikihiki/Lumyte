using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Owns Drawing2D defaults and payload finalization around a graph-independent table allocator.</summary>
public sealed class DrawingArgumentPlan2D : IDisposable
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _owned = [];
    private readonly List<Upload> _uploads = [];
    private readonly List<ShaderResources> _scopes = [];
    private readonly Dictionary<object, object> _legacyHandles = new(ReferenceEqualityComparer.Instance);
    private readonly IGraphicDevice _device;
    private readonly Func<ArgumentTablePlan>? _registrationFactory;
    private readonly Func<ShaderResources>? _resourceFactory;
    private readonly Dictionary<ShaderResources, List<Action<IDrawingResourceDeclarations2D>>> _preparedResources = new(ReferenceEqualityComparer.Instance);
    private readonly Func<ShaderResources, ArgumentRegistration<IGraphicsTextureView>>? _graphFallback;
    private readonly IGraphicsSampler? _sharedSampler;
    private readonly Func<IGraphicsSampler>? _samplerFactory;
    private ArgumentTablePlan? _registrations;
    private IDisposable? _tableOwner;
    private ShaderResources? _legacy;
    private IGraphicsSampler? _sampler;
    private IGraphicsTexture? _fallback;
    private IGraphicsTextureView? _fallbackView;
    private int _users = 1;
    private bool _returned;
    private bool _fallbackRecorded;
    private bool _closed;

    /// <summary>Initializes a new instance of the <see cref="DrawingArgumentPlan2D"/> class.</summary>
    /// <param name="device">The borrowed physical device.</param>
    /// <param name="registrations">An optional borrowed direct allocator.</param>
    /// <param name="graphFallback">A graph-owned fallback registration factory; null selects private direct defaults.</param>
    /// <param name="sharedSampler">An optional graph-owned default nearest sampler.</param>
    /// <param name="samplerFactory">An optional lazy graph-owned sampler factory.</param>
    /// <param name="registrationFactory">An optional lazy direct allocator, created only if preparation participates.</param>
    /// <param name="resourceFactory">An optional opaque graph-owned resource-set factory.</param>
    public DrawingArgumentPlan2D(IGraphicDevice device, ArgumentTablePlan? registrations = null, Func<ShaderResources, ArgumentRegistration<IGraphicsTextureView>>? graphFallback = null, IGraphicsSampler? sharedSampler = null, Func<IGraphicsSampler>? samplerFactory = null, Func<ArgumentTablePlan>? registrationFactory = null, Func<ShaderResources>? resourceFactory = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (registrations is not null && !ReferenceEquals(registrations.Device, device))
        {
            throw new ArgumentException("The argument owner belongs to another device.", nameof(registrations));
        }

        _device = device;
        _registrations = registrations;
        _registrationFactory = registrationFactory;
        _resourceFactory = resourceFactory;
        _tableOwner = resourceFactory is null ? registrations?.Retain() : null;
        (_graphFallback, _sharedSampler, _samplerFactory) = (graphFallback, sharedSampler, samplerFactory);
    }

    /// <summary>Gets the borrowed physical device.</summary>
    public IGraphicDevice Device => _device;

    /// <summary>Gets the direct-only low-level allocator.</summary>
    public ArgumentTablePlan Registrations
    {
        get
        {
            ObjectDisposedException.ThrowIf(_users == 0, this);
            if (_resourceFactory is not null)
            {
                throw new InvalidOperationException("Graph resource sets have no direct allocator.");
            }

            if (_registrations is null)
            {
                ArgumentTablePlan created = _registrationFactory?.Invoke() ?? new(_device);
                if (!ReferenceEquals(created.Device, _device))
                {
                    throw new ArgumentException("The lazy argument owner belongs to another device.");
                }

                _tableOwner = _registrationFactory is null ? created : created.Retain();
                _registrations = created;
            }

            return _registrations;
        }
    }

    /// <summary>Gets opaque draw resource sets created through this preparation wrapper.</summary>
    public IReadOnlyList<ShaderResources> Reservations => _scopes.AsReadOnly();

    /// <summary>Gets a value indicating whether payload maps are prepared before graph materialization.</summary>
    public bool DeferredMaterialization => _resourceFactory is not null || _graphFallback is not null;

    /// <summary>Gets the single table for legacy single-reservation consumers; multiple partitions are deliberately ambiguous.</summary>
    public IArgumentTable Table => Registrations.Tables.Count == 1 ? Registrations.Tables[0] : throw new InvalidOperationException("Bind the draw reservation's table.");

    internal long EstimatedBytes => _resourceFactory is not null || _registrations is null
        ? checked(256 + ((long)_scopes.Count * 256))
        : checked(256 + ((_registrations.Statistics.Textures + _registrations.Statistics.Samplers + _registrations.Statistics.Buffers) * 256));

    /// <summary>Starts a whole draw reservation in painter order.</summary>
    /// <returns>The draw-local opaque resource set.</returns>
    public ShaderResources Reserve()
    {
        ObjectDisposedException.ThrowIf(_users == 0, this);
        if (_closed)
        {
            throw new InvalidOperationException("Argument payload collection is closed.");
        }

        ShaderResources scope = _resourceFactory is null ? Registrations.CreateShaderResources() : _resourceFactory();
        if (_resourceFactory is not null && _tableOwner is null)
        {
            _tableOwner = scope.Retain();
        }

        _scopes.Add(scope);
        return scope;
    }

    /// <summary>Retains tables and owned defaults, but not caller-owned image or buffer resources.</summary>
    /// <returns>An independently retryable lifetime token.</returns>
    public IDisposable Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_users == 0, this);
            _users++;
            return new Use(this);
        }
    }

    /// <summary>Registers a texture in one compatibility reservation; multi-draw callers use Reserve.</summary>
    /// <param name="view">The borrowed view.</param>
    /// <returns>The pending native reference.</returns>
    public DrawingArgumentRegistration2D<IGraphicsTextureView> RegisterTexture(IGraphicsTextureView view) => LegacyHandle(Legacy().RegisterTexture(view));

    /// <summary>Registers a sampler in one compatibility reservation.</summary>
    /// <param name="sampler">The borrowed sampler.</param>
    /// <returns>The pending native reference.</returns>
    public DrawingArgumentRegistration2D<IGraphicsSampler> RegisterSampler(IGraphicsSampler sampler) => LegacyHandle(Legacy().RegisterSampler(sampler));

    /// <summary>Registers an exact typed range in one compatibility reservation.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="range">The borrowed range.</param>
    /// <returns>The pending native reference.</returns>
    public DrawingArgumentRegistration2D<T> RegisterBuffer<T>(BufferSlice<T> range)
        where T : unmanaged => LegacyHandle(Legacy().RegisterBuffer(range));

    /// <summary>Registers an exact shader-data range in one compatibility reservation.</summary>
    /// <typeparam name="T">The shader-data type.</typeparam>
    /// <param name="range">The borrowed range.</param>
    /// <returns>The pending native reference.</returns>
    public DrawingArgumentRegistration2D<T> RegisterBuffer<T>(ShaderDataSlice<T> range)
        where T : struct, IShaderData => LegacyHandle(Legacy().RegisterBuffer(range));

    /// <summary>Schedules a compatibility finalizer after all native registrations succeed.</summary>
    /// <param name="upload">The host payload operation.</param>
    public void DeferUpload(Func<CancellationToken, ValueTask> upload) => DeferUpload(Legacy(), upload);

    /// <summary>Schedules one draw or same-pass batch payload after complete materialization.</summary>
    /// <param name="trigger">An opaque set live whenever this payload is needed.</param>
    /// <param name="upload">The direct asynchronous map/pack/unmap operation.</param>
    /// <param name="prepareMapping">An optional graph append-time asynchronous map operation.</param>
    /// <param name="uploadMapped">A graph-time synchronous pack/unmap operation.</param>
    public void DeferUpload(ShaderResources trigger, Func<CancellationToken, ValueTask> upload, Func<CancellationToken, ValueTask>? prepareMapping = null, Action? uploadMapped = null)
    {
        if (_closed)
        {
            throw new InvalidOperationException("Argument payload collection is closed.");
        }

        ArgumentNullException.ThrowIfNull(upload);
        if (!_scopes.Contains(trigger))
        {
            throw new ArgumentException("The resource set belongs to another drawing preparation.", nameof(trigger));
        }

        Upload pending = new(trigger, upload, prepareMapping, uploadMapped);
        _uploads.Add(pending);
        if (DeferredMaterialization)
        {
            trigger.WhenReady(() => FinalizeUpload(pending));
        }
    }

    /// <summary>Materializes a direct preparation and awaits its payload uploads before publication.</summary>
    /// <param name="cancellationToken">Cancels without publishing incomplete work.</param>
    /// <returns>The finalization completion.</returns>
    public async ValueTask SealAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            throw new InvalidOperationException("Argument payload collection is closed.");
        }

        if (DeferredMaterialization)
        {
            throw new InvalidOperationException("Graph resource preparation is owned by the graph.");
        }

        _closed = true;
        using var profile = DrawingProfileScope.Start(13);
        try
        {
            Registrations.Materialize(cancellationToken: cancellationToken);
            foreach (Upload upload in _uploads)
            {
                if (upload.Trigger.IsReady)
                {
                    await upload.Async(cancellationToken).ConfigureAwait(false);
                }
            }

            profile?.Success();
        }
        catch (Exception error)
        {
            Registrations.Invalidate();
            profile?.Failure(error);
            throw;
        }
        finally
        {
            ArgumentTablePlanStatistics stats = Registrations.Statistics;
            profile?.Counter(15, stats.Tables);
            profile?.Counter(16, stats.Textures);
            profile?.Counter(17, stats.Samplers);
            profile?.Counter(18, stats.Buffers);
            profile?.Counter(19, stats.ReusedTextures);
            profile?.Counter(20, stats.ReusedSamplers);
            profile?.Counter(21, stats.ReusedBuffers);
            profile?.Counter(22, Registrations.IsMaterialized ? 1 : 0);
            profile?.Counter(23, stats.Rollovers);
            _uploads.Clear();
            _legacyHandles.Clear();
        }
    }

    /// <summary>Maps staging before synchronous graph Record, without constructing or publishing payload references.</summary>
    /// <param name="cancellationToken">Cancels append-time mapping.</param>
    /// <returns>The mapping completion.</returns>
    public async ValueTask PrepareMappingsAsync(CancellationToken cancellationToken = default)
    {
        foreach (Upload upload in _uploads)
        {
            if (!upload.Prepared)
            {
                if (upload.PrepareMapping is not null)
                {
                    await upload.PrepareMapping(cancellationToken).ConfigureAwait(false);
                }
                else if (DeferredMaterialization && upload.Mapped is null)
                {
                    throw new NotSupportedException("Graph payload finalizers require a synchronous materialized operation.");
                }

                upload.Prepared = true;
            }
        }
    }

    /// <summary>Checks that graph-live payloads were finalized by their opaque preparation callbacks.</summary>
    public void FinalizeMapped()
    {
        if (_uploads.Any(upload => upload.Trigger.IsReady && !upload.Completed))
        {
            throw new InvalidOperationException("Graph payloads must finalize during resource preparation.");
        }
    }

    /// <summary>Declares only builtin GPU roots allocated for the selected prepared draws.</summary>
    /// <param name="resources">The draw-local opaque sets used by this batch.</param>
    /// <param name="declarations">The module-owned resource declaration visitor.</param>
    public void DeclarePreparedResources(IReadOnlyList<ShaderResources> resources, IDrawingResourceDeclarations2D declarations)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(declarations);
        ObjectDisposedException.ThrowIf(_users == 0, this);
        HashSet<ShaderResources> visited = new(ReferenceEqualityComparer.Instance);
        foreach (ShaderResources resource in resources)
        {
            if (visited.Add(resource) && _preparedResources.TryGetValue(resource, out List<Action<IDrawingResourceDeclarations2D>>? marks))
            {
                foreach (Action<IDrawingResourceDeclarations2D> declare in marks)
                {
                    declare(declarations);
                }
            }
        }
    }

    /// <summary>Returns the owner token; native cleanup failures remain retryable.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (!_returned)
            {
                Release();
                _returned = true;
            }
        }
    }

    internal (int Scopes, int Uploads) Checkpoint() => (_scopes.Count, _uploads.Count);

    internal void Rollback((int Scopes, int Uploads) checkpoint)
    {
        foreach (ShaderResources resources in _scopes.Skip(checkpoint.Scopes))
        {
            resources.Cancel();
            _preparedResources.Remove(resources);
        }

        _scopes.RemoveRange(checkpoint.Scopes, _scopes.Count - checkpoint.Scopes);
        _uploads.RemoveRange(checkpoint.Uploads, _uploads.Count - checkpoint.Uploads);
    }

    internal void MarkPreparedBuffer<T>(ShaderResources resources, IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged => PreparedMarks(resources).Add(declarations => declarations.UseBuffer(buffer, scope));

    internal void MarkPreparedShaderData<T>(ShaderResources resources, IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderData => PreparedMarks(resources).Add(declarations => declarations.UseShaderDataBuffer(buffer, scope));

    internal IGraphicsSampler Sampler(DrawingResources2D resources) => _sampler ??= resources.Sampler ?? _sharedSampler ?? _samplerFactory?.Invoke() ?? Own(Device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 }));

    internal ArgumentRegistration<IGraphicsTextureView> Fallback(ShaderResources reservation)
    {
        if (_graphFallback is not null)
        {
            return _graphFallback(reservation);
        }

        _fallback ??= Own(Device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled }));
        _fallbackView ??= Own(_fallback.CreateView());
        return reservation.RegisterTexture(_fallbackView);
    }

    internal void InitializeFallback(IGraphicsCommandBuffer commands)
    {
        if (DeferredMaterialization || _fallbackRecorded || _fallback is null)
        {
            return;
        }

        var write = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var read = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        commands.Barrier(new TextureBarrierDesc { Texture = _fallback, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = write });
        IRenderEncoder clear = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = _fallbackView!, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        clear.End();
        commands.Barrier(new TextureBarrierDesc { Texture = _fallback, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Sampled, Before = write, After = read });
        _fallbackRecorded = true;
    }

    private void FinalizeUpload(Upload upload)
    {
        if (upload.Completed)
        {
            return;
        }

        _closed = true;
        if (!upload.Trigger.IsReady || !upload.Prepared || upload.Mapped is null)
        {
            throw new InvalidOperationException("Map graph staging during append before materialization.");
        }

        upload.Mapped();
        upload.Completed = true;
    }

    private List<Action<IDrawingResourceDeclarations2D>> PreparedMarks(ShaderResources resources)
    {
        if (!_scopes.Contains(resources))
        {
            throw new ArgumentException("The resource set belongs to another drawing preparation.", nameof(resources));
        }

        if (!_preparedResources.TryGetValue(resources, out List<Action<IDrawingResourceDeclarations2D>>? marks))
        {
            marks = [];
            _preparedResources.Add(resources, marks);
        }

        return marks;
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private ShaderResources Legacy() => _legacy ??= Reserve();

    private DrawingArgumentRegistration2D<T> LegacyHandle<T>(ArgumentRegistration<T> reference)
    {
        if (!_legacyHandles.TryGetValue(reference, out object? handle))
        {
            handle = new DrawingArgumentRegistration2D<T>(reference);
            _legacyHandles.Add(reference, handle);
        }

        return (DrawingArgumentRegistration2D<T>)handle;
    }

    private void Release()
    {
        if (_users == 1)
        {
            _tableOwner?.Dispose();
            while (_owned.Count != 0)
            {
                _owned[^1].Dispose();
                _owned.RemoveAt(_owned.Count - 1);
            }

            _uploads.Clear();
            _legacyHandles.Clear();
            _preparedResources.Clear();
        }

        _users--;
    }

    private sealed class Upload(ShaderResources trigger, Func<CancellationToken, ValueTask> async, Func<CancellationToken, ValueTask>? prepareMapping, Action? mapped)
    {
        internal ShaderResources Trigger { get; } = trigger;

        internal Func<CancellationToken, ValueTask> Async { get; } = async;

        internal Func<CancellationToken, ValueTask>? PrepareMapping { get; } = prepareMapping;

        internal Action? Mapped { get; } = mapped;

        internal bool Prepared { get; set; }

        internal bool Completed { get; set; }
    }

    private sealed class Use(DrawingArgumentPlan2D owner) : IDisposable
    {
        private bool _returned;

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (!_returned)
                {
                    owner.Release();
                    _returned = true;
                }
            }
        }
    }
}
