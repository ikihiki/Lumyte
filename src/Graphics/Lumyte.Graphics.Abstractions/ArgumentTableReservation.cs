namespace Lumyte.Graphics.Abstractions;

/// <summary>Collects every direct and nested reference needed by one indivisible draw or dispatch.</summary>
public sealed class ArgumentTableReservation
{
    private readonly ArgumentTablePlan _owner;
    private readonly List<ArgumentRequest> _requests = [];
    private readonly List<Action> _collectionGuards = [];
    private readonly List<Action<object, ArgumentBufferAccess>> _inspectors = [];
    private readonly List<Action> _finalizers = [];
    private readonly List<Action<object, ulong, ulong, ArgumentBufferAccess>> _bindingInspectors = [];
    private IShaderReference[] _references = [];
    private ArgumentPartition? _partition;
    private ArgumentTableReservation? _operation;
    private ArgumentTableReservation? _tableGroup;
    private bool _materialized;

    internal ArgumentTableReservation(ArgumentTablePlan owner, uint colorAttachments)
    {
        _owner = owner;
        ColorAttachments = colorAttachments;
        Graphics = colorAttachments > 0;
    }

    /// <summary>Gets this draw's selected table after successful materialization; never mutate or dispose it directly.</summary>
    public IArgumentTable Table
    {
        get
        {
            ValidateMaterialized();
            return _partition?.Table ?? throw new InvalidOperationException("An empty reservation needs no table.");
        }
    }

    /// <summary>Gets a value indicating whether this reservation was included in the materialized execution list.</summary>
    public bool IsMaterialized => _materialized && _owner.ReferencesAvailable;

    /// <summary>Gets the number of color attachments reserved by this draw's physical layout.</summary>
    public uint ColorAttachments { get; private set; }

    internal bool Graphics { get; private set; }

    internal IReadOnlyList<ArgumentRequest> Requests => _requests;

    internal ArgumentTablePlan Owner => _owner;

    internal ArgumentTableReservation Operation => _operation?.Operation ?? this;

    internal ArgumentTableReservation TableGroup => _tableGroup?.TableGroup ?? this;

    /// <summary>Requests this exact immutable view object, without merging distinct caller-owned views.</summary>
    /// <param name="view">The borrowed sampled view.</param>
    /// <param name="resolved">Receives the real native reference after every table is installed.</param>
    public void RegisterTexture(IGraphicsTextureView view, Action<IGpuRef<IGraphicsTextureView>> resolved)
    {
        ArgumentNullException.ThrowIfNull(view);
        var key = new ArgumentKey(view, typeof(IGraphicsTextureView), 0, 0, 1, 0, ArgumentBufferAccess.Read);
        Deferred(() => new ResolvedArgument(key, (table, slot) => table.WriteTexture(slot, view)), resolved);
    }

    /// <summary>Requests a view resolved exactly once during materialization, before any native table is created.</summary>
    /// <param name="resolve">The owner-scoped resource resolver.</param>
    /// <param name="resolved">Receives the real native reference after every table is installed.</param>
    public void RegisterTexture(Func<IGraphicsTextureView> resolve, Action<IGpuRef<IGraphicsTextureView>> resolved)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        Deferred(
            () =>
            {
                IGraphicsTextureView view = resolve() ?? throw new InvalidOperationException("The view resolver returned null.");
                var key = new ArgumentKey(view, typeof(IGraphicsTextureView), 0, 0, 1, 0, ArgumentBufferAccess.Read);
                return new(key, (table, slot) => table.WriteTexture(slot, view));
            },
            resolved);
    }

    /// <summary>Requests an immutable sampler by object identity, not descriptor value equality.</summary>
    /// <param name="sampler">The borrowed sampler.</param>
    /// <param name="resolved">Receives the real native reference after every table is installed.</param>
    public void RegisterSampler(IGraphicsSampler sampler, Action<IGpuRef<IGraphicsSampler>> resolved)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        var key = new ArgumentKey(sampler, typeof(IGraphicsSampler), 1, 0, 1, 0, ArgumentBufferAccess.Read);
        Deferred(() => new ResolvedArgument(key, (table, slot) => table.WriteSampler(slot, sampler)), resolved);
    }

    /// <summary>Requests an exact typed raw range and its actual shader binding uses.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="range">The nonempty borrowed range.</param>
    /// <param name="resolved">Receives the real native reference after every table is installed.</param>
    /// <param name="access">Read, write, or both descriptor uses in this draw.</param>
    public void RegisterBuffer<T>(BufferSlice<T> range, Action<IGpuRef<T>> resolved, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        ResolvedArgument request = Raw(range, access);
        Deferred(() => request, resolved);
    }

    /// <summary>Requests an exact raw range resolved at the owner's materialization boundary.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="resolve">Resolves the final resource and range.</param>
    /// <param name="resolved">Receives the real native reference after every table is installed.</param>
    /// <param name="access">Actual descriptor uses in this draw.</param>
    public void RegisterBuffer<T>(Func<BufferSlice<T>> resolve, Action<IGpuRef<T>> resolved, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(resolve);
        Deferred(() => Raw(resolve(), access), resolved);
    }

    /// <summary>Requests an exact shader-data range, independently of raw and writable reference kinds.</summary>
    /// <typeparam name="T">The logical element type.</typeparam>
    /// <param name="range">The borrowed device-memory range.</param>
    /// <param name="resolved">Receives the real native reference after every table is installed.</param>
    public void RegisterBuffer<T>(ShaderDataSlice<T> range, Action<IGpuRef<T>> resolved)
        where T : IShaderData
    {
        ResolvedArgument request = Shader(range);
        Deferred(() => request, resolved);
    }

    /// <summary>Requests a shader-data range resolved after graph resources exist.</summary>
    /// <typeparam name="T">The logical element type.</typeparam>
    /// <param name="resolve">Resolves the final shader-data range.</param>
    /// <param name="resolved">Receives the real native reference after every table is installed.</param>
    public void RegisterShaderData<T>(Func<ShaderDataSlice<T>> resolve, Action<IGpuRef<T>> resolved)
        where T : IShaderData
    {
        ArgumentNullException.ThrowIfNull(resolve);
        Deferred(() => Shader(resolve()), resolved);
    }

    /// <summary>Adds an owner-specific declared-resource check, run before any native table is created.</summary>
    /// <param name="inspect">Validates resource identity and actual descriptor accesses without GPU commands.</param>
    public void InspectResources(Action<object, ArgumentBufferAccess> inspect)
    {
        ValidateCollecting();
        ArgumentNullException.ThrowIfNull(inspect);
        _inspectors.Add(inspect);
    }

    internal void ConfigureExecution(uint colorAttachments, bool graphics)
    {
        ValidateCollecting();
        ColorAttachments = colorAttachments;
        Graphics = graphics;
    }

    internal void WhenReady(Action finalize)
    {
        ValidateCollecting();
        ArgumentNullException.ThrowIfNull(finalize);
        _finalizers.Add(finalize);
    }

    internal void FinalizeResources()
    {
        foreach (Action finalize in _finalizers)
        {
            finalize();
            _owner.ValidateReferences();
        }
    }

    internal void AddCollectionGuard(Action validate) => _collectionGuards.Add(validate);

    internal void ValidateCollecting()
    {
        foreach (Action validate in _collectionGuards)
        {
            validate();
        }

        _owner.ValidateCollecting();
        _owner.ValidateScope(this);
    }

    internal void JoinOperation(ArgumentTableReservation other)
    {
        JoinTable(other);
        if (!ReferenceEquals(Operation, other.Operation))
        {
            Operation._operation = other.Operation;
        }
    }

    internal void JoinTable(ArgumentTableReservation other)
    {
        ValidateCollecting();
        other.ValidateCollecting();
        if (!ReferenceEquals(_owner, other._owner))
        {
            throw new ArgumentException("Execution coherence requires the same allocator.");
        }

        if (!ReferenceEquals(TableGroup, other.TableGroup))
        {
            TableGroup._tableGroup = other.TableGroup;
        }
    }

    internal bool ContainsReference(IShaderReference reference)
    {
        ValidateMaterialized();
        return _references.Any(registered =>
            (ReferenceEquals(registered, reference) ||
             (ReferenceEquals(registered.Table, reference.Table) && ReferenceEquals(registered.Resource, reference.Resource) &&
              registered.Slot == reference.Slot && registered.RegistrationOffsetInBytes == reference.RegistrationOffsetInBytes &&
              registered.RegistrationSizeInBytes == reference.RegistrationSizeInBytes &&
              reference.OffsetInBytes >= registered.OffsetInBytes && reference.OffsetInBytes - registered.OffsetInBytes <= registered.SizeInBytes &&
              reference.SizeInBytes <= registered.SizeInBytes - (reference.OffsetInBytes - registered.OffsetInBytes))));
    }

    internal IArgumentTable? RecordingTable(bool graphics)
    {
        ValidateMaterialized();
        _owner.ValidateRecording();
        if (Graphics != graphics)
        {
            throw new InvalidOperationException("The resource execution shape does not match this encoder.");
        }

        return _partition?.Table;
    }

    internal void InspectBindingRanges(Action<object, ulong, ulong, ArgumentBufferAccess> inspect) => _bindingInspectors.Add(inspect);

    internal void Inspect(ArgumentKey key)
    {
        foreach (Action<object, ulong, ulong, ArgumentBufferAccess> inspect in _bindingInspectors)
        {
            inspect(key.Resource, key.Offset, key.Size, key.Access);
        }

        foreach (Action<object, ArgumentBufferAccess> inspector in _inspectors)
        {
            inspector(key.Resource, key.Access);
        }
    }

    internal void Select(ArgumentPartition? partition)
    {
        _partition = partition;
        _references = _requests.Select(request => request.Reference).OfType<IShaderReference>().ToArray();
        _materialized = true;
    }

    internal void ValidateMaterialized()
    {
        _owner.ValidateReferences();
        _owner.ValidateScope(this);
        if (!_materialized)
        {
            throw new InvalidOperationException("The reservation was culled or not materialized.");
        }
    }

    internal void ClearRequests()
    {
        _requests.Clear();
        _inspectors.Clear();
        _bindingInspectors.Clear();
        _finalizers.Clear();
    }

    private static ResolvedArgument Raw<T>(BufferSlice<T> range, ArgumentBufferAccess access)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(range.Buffer);
        if (access is not (ArgumentBufferAccess.Read or ArgumentBufferAccess.Write or ArgumentBufferAccess.ReadWrite))
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }

        if (((access & ArgumentBufferAccess.Read) != 0 && (range.Buffer.Usage & BufferUsage.ShaderRead) == 0)
            || ((access & ArgumentBufferAccess.Write) != 0 && (range.Buffer.Usage & BufferUsage.ShaderWrite) == 0))
        {
            throw new ArgumentException("The buffer usage does not permit the reserved shader access.", nameof(range));
        }

        var key = new ArgumentKey(range.Buffer, typeof(T), 2, range.OffsetInBytes, range.Count, range.Buffer.Layout.ElementStrideInBytes, access);
        return new(key, (table, slot) => table.WriteBuffer(slot, range));
    }

    private static ResolvedArgument Shader<T>(ShaderDataSlice<T> range)
        where T : IShaderData
    {
        ArgumentNullException.ThrowIfNull(range.Buffer);
        if (range.Buffer.Memory != MemoryPreference.Automatic)
        {
            throw new ArgumentException("Only device-memory shader data can be registered.", nameof(range));
        }

        ulong stride = range.Buffer.ShaderElementStrideInBytes;
        var key = new ArgumentKey(range.Buffer, typeof(T), 3, checked(range.Offset * stride), range.Count, stride, ArgumentBufferAccess.Read);
        return new(key, (table, slot) => table.WriteBuffer(slot, range));
    }

    private void Deferred<T>(Func<ResolvedArgument> resolve, Action<IGpuRef<T>> resolved)
    {
        ValidateCollecting();
        ArgumentNullException.ThrowIfNull(resolved);
        _requests.Add(new(resolve, reference => resolved((IGpuRef<T>)reference)));
    }
}
