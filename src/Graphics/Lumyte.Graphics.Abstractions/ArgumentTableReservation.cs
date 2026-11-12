namespace Lumyte.Graphics.Abstractions;

/// <summary>Collects every direct and nested reference needed by one indivisible draw or dispatch.</summary>
public sealed class ArgumentTableReservation
{
    private readonly ArgumentTablePlan _owner;
    private readonly List<ArgumentRequest> _requests = [];
    private readonly List<Action<object, ArgumentBufferAccess>> _inspectors = [];
    private readonly Dictionary<ArgumentKey, object> _known = [];
    private ArgumentPartition? _partition;
    private bool _materialized;

    internal ArgumentTableReservation(ArgumentTablePlan owner, uint colorAttachments)
    {
        _owner = owner;
        ColorAttachments = colorAttachments;
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
    public uint ColorAttachments { get; }

    internal IReadOnlyList<ArgumentRequest> Requests => _requests;

    internal ArgumentTablePlan Owner => _owner;

    internal long ReusedTextures { get; private set; }

    internal long ReusedSamplers { get; private set; }

    internal long ReusedBuffers { get; private set; }

    /// <summary>Requests this exact immutable view object, without merging distinct caller-owned views.</summary>
    /// <param name="view">The borrowed sampled view.</param>
    /// <returns>A reservation-local pending handle.</returns>
    public ArgumentRegistration<IGraphicsTextureView> RegisterTexture(IGraphicsTextureView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var key = new ArgumentKey(view, typeof(IGraphicsTextureView), 0, 0, 1, 0, ArgumentBufferAccess.Read);
        return Known<IGraphicsTextureView>(key, () => new ResolvedArgument(key, (table, slot) => table.WriteTexture(slot, view)));
    }

    /// <summary>Requests a view resolved exactly once during materialization, before any native table is created.</summary>
    /// <param name="resolve">The owner-scoped resource resolver.</param>
    /// <returns>A reservation-local pending handle.</returns>
    public ArgumentRegistration<IGraphicsTextureView> RegisterTexture(Func<IGraphicsTextureView> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return Deferred<IGraphicsTextureView>(() =>
        {
            IGraphicsTextureView view = resolve() ?? throw new InvalidOperationException("The view resolver returned null.");
            var key = new ArgumentKey(view, typeof(IGraphicsTextureView), 0, 0, 1, 0, ArgumentBufferAccess.Read);
            return new(key, (table, slot) => table.WriteTexture(slot, view));
        });
    }

    /// <summary>Requests an immutable sampler by object identity, not descriptor value equality.</summary>
    /// <param name="sampler">The borrowed sampler.</param>
    /// <returns>A reservation-local pending handle.</returns>
    public ArgumentRegistration<IGraphicsSampler> RegisterSampler(IGraphicsSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        var key = new ArgumentKey(sampler, typeof(IGraphicsSampler), 1, 0, 1, 0, ArgumentBufferAccess.Read);
        return Known<IGraphicsSampler>(key, () => new ResolvedArgument(key, (table, slot) => table.WriteSampler(slot, sampler)));
    }

    /// <summary>Requests an exact typed raw range and its actual shader binding uses.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="range">The nonempty borrowed range.</param>
    /// <param name="access">Read, write, or both descriptor uses in this draw.</param>
    /// <returns>A reservation-local pending handle.</returns>
    public ArgumentRegistration<T> RegisterBuffer<T>(BufferSlice<T> range, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        ResolvedArgument request = Raw(range, access);
        return Known<T>(request.Key, () => request);
    }

    /// <summary>Requests an exact raw range resolved at the owner's materialization boundary.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="resolve">Resolves the final resource and range.</param>
    /// <param name="access">Actual descriptor uses in this draw.</param>
    /// <returns>A reservation-local pending handle.</returns>
    public ArgumentRegistration<T> RegisterBuffer<T>(Func<BufferSlice<T>> resolve, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return Deferred<T>(() => Raw(resolve(), access));
    }

    /// <summary>Requests an exact shader-data range, independently of raw and writable reference kinds.</summary>
    /// <typeparam name="T">The logical element type.</typeparam>
    /// <param name="range">The borrowed device-memory range.</param>
    /// <returns>A reservation-local pending handle.</returns>
    public ArgumentRegistration<T> RegisterBuffer<T>(ShaderDataSlice<T> range)
        where T : struct, IShaderData
    {
        ResolvedArgument request = Shader(range);
        return Known<T>(request.Key, () => request);
    }

    /// <summary>Requests a shader-data range resolved after graph resources exist.</summary>
    /// <typeparam name="T">The logical element type.</typeparam>
    /// <param name="resolve">Resolves the final shader-data range.</param>
    /// <returns>A reservation-local pending handle.</returns>
    public ArgumentRegistration<T> RegisterShaderData<T>(Func<ShaderDataSlice<T>> resolve)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return Deferred<T>(() => Shader(resolve()));
    }

    /// <summary>Adds an owner-specific declared-resource check, run before any native table is created.</summary>
    /// <param name="inspect">Validates resource identity and actual descriptor accesses without GPU commands.</param>
    public void InspectResources(Action<object, ArgumentBufferAccess> inspect)
    {
        _owner.ValidateCollecting();
        ArgumentNullException.ThrowIfNull(inspect);
        _inspectors.Add(inspect);
    }

    internal void Inspect(ArgumentKey key)
    {
        foreach (Action<object, ArgumentBufferAccess> inspector in _inspectors)
        {
            inspector(key.Resource, key.Access);
        }
    }

    internal void Select(ArgumentPartition? partition)
    {
        _partition = partition;
        _materialized = true;
    }

    internal void ValidateMaterialized()
    {
        _owner.ValidateReferences();
        if (!_materialized)
        {
            throw new InvalidOperationException("The reservation was culled or not materialized.");
        }
    }

    internal void ClearRequests()
    {
        _requests.Clear();
        _known.Clear();
        _inspectors.Clear();
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
        where T : struct, IShaderData
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

    private ArgumentRegistration<T> Known<T>(ArgumentKey key, Func<ResolvedArgument> resolve)
    {
        _owner.ValidateCollecting();
        if (_known.TryGetValue(key, out object? previous))
        {
            switch (key.Kind)
            {
                case 0:
                    ReusedTextures++;
                    break;
                case 1:
                    ReusedSamplers++;
                    break;
                default:
                    ReusedBuffers++;
                    break;
            }

            return (ArgumentRegistration<T>)previous;
        }

        ArgumentRegistration<T> registration = Deferred<T>(resolve);
        _known.Add(key, registration);
        return registration;
    }

    private ArgumentRegistration<T> Deferred<T>(Func<ResolvedArgument> resolve)
    {
        _owner.ValidateCollecting();
        var registration = new ArgumentRegistration<T>(this);
        _requests.Add(new(resolve, registration.Resolve));
        return registration;
    }
}
