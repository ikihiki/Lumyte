using System.Diagnostics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Builds a single-queue render graph with explicit outputs and plan-owned temporary resources.</summary>
/// <remarks>The caller manages thread synchronization, imported resource lifetime and submission.</remarks>
public sealed class RenderGraph : IDisposable
{
    private readonly HashSet<object> _allocations = new(ReferenceEqualityComparer.Instance);
    private readonly List<RenderGraphResource> _resources = [];
    private readonly List<RenderGraphPass> _passes = [];
    private readonly HashSet<RenderGraphResource> _outputs = [];
    private readonly Dictionary<RenderGraphResource, RenderGraphUse> _exports = [];
    private readonly RenderGraphExtensions? _extensions;
    private Dictionary<RuntimeTypeHandle, object?>? _extensionStates;
    private List<IRenderGraphLifetime>? _lifetimes;
    private int _creatingExtension;
    private bool _closed;
    private bool _faulted;
    private bool _compiling;
    private bool _transferred;
    private bool _disposing;

    /// <summary>Initializes a new instance of the <see cref="RenderGraph"/> class.</summary>
    /// <param name="device">The same device used for recording; required for temporary allocations.</param>
    /// <param name="name">A diagnostic name of at most 256 characters.</param>
    /// <param name="observer">An optional diagnostic adapter.</param>
    /// <param name="extensions">Optional shared extension factories, frozen when this graph attaches.</param>
    public RenderGraph(IGraphicDevice? device = null, string name = "render-graph", IRenderGraphObserver? observer = null, RenderGraphExtensions? extensions = null)
    {
        ValidateName(name);
        Device = device;
        Name = name;
        Observer = observer;
        extensions?.Attach();
        _extensions = extensions;
    }

    /// <summary>Gets the graph's diagnostic name.</summary>
    public string Name { get; }

    /// <summary>Gets the stable builder identity used in diagnostic runs.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets the borrowed device used by graph extensions and temporary allocations, or null for import-only graphs.</summary>
    public IGraphicDevice? Device { get; }

    /// <summary>Gets a value indicating whether this graph accepts preparation, ownership and pass declarations.</summary>
    public bool IsBuilding => !_closed && !_faulted && !_compiling;

    internal GraphShaderResourceOwner? ShaderResourceOwner { get; set; }

    internal IRenderGraphObserver? Observer { get; }

    /// <summary>Gets the cached state created by an explicitly registered extension factory.</summary>
    /// <typeparam name="T">The exact registered state type.</typeparam>
    /// <returns>The state shared by all uses of this extension in this graph.</returns>
    public T GetExtension<T>()
        where T : class => ResolveExtension<T>(null);

    /// <summary>Gets cached state, using the supplied factory only when no factory is registered for its type.</summary>
    /// <typeparam name="T">The exact state type.</typeparam>
    /// <param name="fallback">The module's default factory; called at most once for this graph.</param>
    /// <returns>The cached extension state.</returns>
    public T GetOrAddExtension<T>(Func<RenderGraph, T> fallback)
        where T : class => ResolveExtension<T>(fallback);

    /// <summary>Transfers ownership before preparation and then atomically into a successful compiled plan.</summary>
    /// <param name="lifetime">The owner validated before recording and released in reverse registration order.</param>
    /// <remarks>After successful registration, only this graph or its compiled plan should dispose the owner.</remarks>
    public void Retain(IRenderGraphLifetime lifetime)
    {
        EnsureBuilding();
        lifetime.Validate();
        EnsureBuilding();
        (_lifetimes ??= []).Add(lifetime);
    }

    /// <summary>Imports a whole texture without transferring ownership.</summary>
    /// <param name="texture">The allocation or borrowed surface image.</param>
    /// <param name="initialState">The preceding state; Undefined requires initialization.</param>
    /// <param name="initialScope">The preceding accesses.</param>
    /// <param name="name">The optional diagnostic name.</param>
    /// <returns>A graph-local handle.</returns>
    public RenderGraphTexture ImportTexture(IGraphicsTexture texture, TextureState initialState, BarrierScope initialScope, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var resource = new RenderGraphTexture(this, texture, initialState, initialScope, ResourceName(name));
        Register(resource);
        return resource;
    }

    /// <summary>Imports a whole typed buffer without transferring ownership.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The allocation, initialized if read before a write.</param>
    /// <param name="initialScope">The preceding accesses.</param>
    /// <param name="name">The optional diagnostic name.</param>
    /// <returns>A graph-local handle.</returns>
    public RenderGraphBuffer<T> ImportBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope initialScope, string? name = null)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var resource = new RenderGraphBuffer<T>(this, buffer, initialScope, ResourceName(name));
        Register(resource);
        return resource;
    }

    /// <summary>Imports shader data with its explicit transfer and dependency metadata.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="buffer">The caller-owned allocation.</param>
    /// <param name="initialScope">The preceding accesses.</param>
    /// <param name="name">The optional diagnostic name.</param>
    /// <returns>A graph-local handle.</returns>
    public RenderGraphShaderDataBuffer<T> ImportShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope initialScope, string? name = null)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var resource = new RenderGraphShaderDataBuffer<T>(this, buffer, initialScope, ResourceName(name));
        Register(resource);
        return resource;
    }

    /// <summary>Reuses a typed imported allocation or imports it without transferring ownership.</summary>
    /// <typeparam name="T">The storage element.</typeparam>
    /// <param name="buffer">The borrowed allocation.</param>
    /// <param name="initialScope">Preceding accesses, used only for a new import.</param>
    /// <param name="name">The optional name of a new import.</param>
    /// <returns>The unique graph handle; an existing import retains its initial scope.</returns>
    public RenderGraphBuffer<T> GetOrImportBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope initialScope, string? name = null)
        where T : unmanaged
    {
        EnsureBuilding();
        return _resources.OfType<RenderGraphBuffer<T>>().FirstOrDefault(resource => ReferenceEquals(resource.Allocation, buffer)) ?? ImportBuffer(buffer, initialScope, name);
    }

    /// <summary>Reuses imported shader data or imports it without changing transfer provenance.</summary>
    /// <typeparam name="T">The logical element.</typeparam>
    /// <param name="buffer">The borrowed allocation.</param>
    /// <param name="initialScope">Preceding accesses, used only for a new import.</param>
    /// <param name="name">The optional name of a new import.</param>
    /// <returns>The unique graph handle; an existing import retains its initial scope.</returns>
    public RenderGraphShaderDataBuffer<T> GetOrImportShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope initialScope, string? name = null)
        where T : struct, IShaderData
    {
        EnsureBuilding();
        return _resources.OfType<RenderGraphShaderDataBuffer<T>>().FirstOrDefault(resource => ReferenceEquals(resource.Allocation, buffer)) ?? ImportShaderDataBuffer(buffer, initialScope, name);
    }

    /// <summary>Declares an uninitialized temporary texture, allocated only if used by live work.</summary>
    /// <param name="desc">The exact allocation requirements.</param>
    /// <param name="name">The optional diagnostic name.</param>
    /// <returns>A handle whose allocation is owned by each compiled plan.</returns>
    public RenderGraphTexture CreateTexture(TextureDesc desc, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(desc);
        RequireDevice();
        if (desc.Width == 0 || desc.Height == 0 || desc.MipLevels == 0 || desc.ArrayLayers == 0)
        {
            throw new ArgumentException("Temporary textures must have positive dimensions.", nameof(desc));
        }

        var resource = new RenderGraphTexture(this, desc, ResourceName(name));
        Register(resource);
        return resource;
    }

    /// <summary>Declares an uninitialized temporary typed buffer.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="desc">The exact allocation requirements.</param>
    /// <param name="name">The optional diagnostic name.</param>
    /// <returns>A handle whose allocation is owned by each compiled plan.</returns>
    public RenderGraphBuffer<T> CreateBuffer<T>(BufferDesc<T> desc, string? name = null)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(desc);
        RequireDevice();
        if (desc.Count == 0)
        {
            throw new ArgumentException("Temporary buffers must be nonempty.", nameof(desc));
        }

        var resource = new RenderGraphBuffer<T>(this, desc, ResourceName(name));
        Register(resource);
        return resource;
    }

    /// <summary>Declares graph-owned, uninitialized Automatic shader data, allocated only for live uses.</summary>
    /// <typeparam name="T">The logical shader data type.</typeparam>
    /// <param name="artifact">The target ABI and element schema.</param>
    /// <param name="count">The positive logical element count.</param>
    /// <param name="name">The optional diagnostic name.</param>
    /// <returns>A temporary whose first read requires an explicit graph copy-write producer.</returns>
    public RenderGraphShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, string? name = null)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentOutOfRangeException.ThrowIfZero(count);
        RequireDevice();
        var resource = new RenderGraphShaderDataBuffer<T>(this, artifact, count, ResourceName(name));
        Register(resource);
        return resource;
    }

    /// <summary>Retains the last producer of a resource as an observable graph output.</summary>
    /// <param name="resource">The graph-local resource.</param>
    /// <returns>This graph.</returns>
    public RenderGraph MarkOutput(RenderGraphResource resource)
    {
        EnsureBuilding();
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.LocalPassOwner is not null)
        {
            throw new ArgumentException("Pass-local storage cannot be a graph output.", nameof(resource));
        }

        CheckResource(resource);
        _outputs.Add(resource);
        return this;
    }

    /// <summary>Retains a texture output and transitions it for subsequent external use.</summary>
    /// <param name="texture">The graph-local resource.</param>
    /// <param name="state">The defined final state, including Present for a borrowed surface image.</param>
    /// <param name="scope">The subsequent accesses; Present requires an empty scope.</param>
    /// <returns>This graph.</returns>
    public RenderGraph ExportTexture(RenderGraphTexture texture, TextureState state, BarrierScope scope)
    {
        if (state == TextureState.Undefined)
        {
            throw new ArgumentException("Export requires a defined state.", nameof(state));
        }

        Export(texture, state, scope);
        return this;
    }

    /// <summary>Retains a buffer output and synchronizes it for subsequent external use.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The graph-local resource.</param>
    /// <param name="scope">The subsequent accesses, such as HostRead for completed readback.</param>
    /// <returns>This graph.</returns>
    public RenderGraph ExportBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged
    {
        Export(buffer, TextureState.Undefined, scope);
        return this;
    }

    /// <summary>Retains shader data and synchronizes its subsequent external accesses.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="buffer">The graph-local resource.</param>
    /// <param name="scope">The subsequent accesses.</param>
    /// <returns>This graph.</returns>
    public RenderGraph ExportShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderData
    {
        Export(buffer, TextureState.Undefined, scope);
        return this;
    }

    /// <summary>Adds a recording callback; imported writes are retained automatically.</summary>
    /// <param name="name">A diagnostic name of at most 256 characters.</param>
    /// <param name="record">Records commands and closes all encoders without submitting.</param>
    /// <returns>The pass for declarations; use KeepAlive for other side effects.</returns>
    public RenderGraphPass AddPass(string name, Action<RenderGraphContext> record)
    {
        EnsureBuilding();
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(record);
        var pass = new RenderGraphPass(this, name, record);
        _passes.Add(pass);
        return pass;
    }

    /// <summary>Compiles dependencies, culls unused work and plans barriers without GPU allocation.</summary>
    /// <param name="frameId">The nonnegative application frame number, or zero when unspecified.</param>
    /// <param name="capture">Optional bounded descriptions for this compilation; otherwise only an observer request enables capture.</param>
    /// <returns>A disposable one-shot recording plan.</returns>
    public CompiledRenderGraph Compile(long frameId = 0, RenderGraphCaptureOptions? capture = null)
    {
        EnsureBuilding();
        if (_creatingExtension != 0)
        {
            throw new InvalidOperationException("Complete extension preparation before compiling.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(frameId);
        if (capture is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capture.MaxBytes);
        }

        var run = new RenderGraphRun(Id, Guid.NewGuid(), Name, frameId);
        IRenderGraphObserver? observer = null;
        CompiledRenderGraph? plan = null;
        RenderGraphProfiling.ProfileScope? profile = null;
        int operationOutcome = 2;
        _compiling = true;
        try
        {
            observer = Observer is { IsEnabled: true } ? Observer : null;
            RenderGraphCaptureOptions? requested = observer?.RequestCapture(run);
            if (requested is not null && (capture is null || requested.MaxBytes < capture.MaxBytes))
            {
                capture = requested;
            }

            using (IDisposable? diagnostic = observer?.Begin(run, "compile"))
            {
                long started = observer is null ? 0 : Stopwatch.GetTimestamp();
                (Guid profileRunId, long profileFrameId) = RenderGraphProfiling.Log.HasMeasurements ? RenderGraphProfiling.ReadContext() : default;
                using (profile = RenderGraphProfiling.Log.Begin(1, run, profileRunId, profileFrameId, operation: 1))
                {
                    try
                    {
                        plan = CompileCore(run, capture);
                        profile?.Add(1, plan.Statistics.LivePasses);
                        profile?.Add(2, plan.Statistics.CulledPasses);
                        profile?.Add(3, plan.Statistics.Resources);
                        profile?.Add(4, plan.Statistics.TransientResources);
                        profile?.Add(5, plan.Statistics.Dependencies);
                        profile?.Add(6, plan.Statistics.Barriers);
                        profile?.Complete();
                    }
                    catch (Exception exception)
                    {
                        profile?.Fail(exception);
                        throw;
                    }
                }

                observer?.Compiled(run, plan.Statistics, plan.Snapshot, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            if (_lifetimes is { Count: > 0 } lifetimes)
            {
                plan.Retain(new LifetimeGroup(lifetimes));
            }

            if (_extensions is not null || _extensionStates is not null || _lifetimes is not null)
            {
                _transferred = true;
                _closed = true;
            }

            operationOutcome = 0;
            return plan;
        }
        catch (Exception exception)
        {
            operationOutcome = exception is OperationCanceledException ? 1 : 2;
            plan?.Dispose();
            observer?.Failed(run, "compile", exception);
            throw;
        }
        finally
        {
            _compiling = false;
            if (profile is not null)
            {
                RenderGraphProfiling.Log.GraphOperationResult(run.RunId, profile.SpanId, 1, operationOutcome);
            }
        }
    }

    /// <summary>Releases abandoned preparation, leaving successfully transferred ownership with its plan.</summary>
    /// <remarks>Cleanup failure keeps the remaining owners for another Dispose call.</remarks>
    public void Dispose()
    {
        if (_compiling || _creatingExtension != 0 || _disposing)
        {
            throw new InvalidOperationException("Graph disposal cannot overlap compilation, extension creation or cleanup.");
        }

        _closed = true;
        if (_transferred)
        {
            return;
        }

        _disposing = true;
        try
        {
            if (_lifetimes is { } lifetimes)
            {
                Release(lifetimes);
            }

            _extensionStates?.Clear();
        }
        finally
        {
            _disposing = false;
        }
    }

    internal void EnsureBuilding()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_faulted || _compiling)
        {
            throw new InvalidOperationException("Graph preparation failed or compilation is already in progress.");
        }
    }

    internal void CheckResource(RenderGraphResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.Owner != this)
        {
            throw new ArgumentException("Resource belongs to another graph.", nameof(resource));
        }
    }

    internal RenderGraphBuffer<T> LocalBuffer<T>(RenderGraphPass pass, IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged => LocalResource(pass, buffer, scope, () => new RenderGraphBuffer<T>(this, buffer, default, "pass-local-buffer"));

    internal RenderGraphShaderDataBuffer<T> LocalShaderData<T>(RenderGraphPass pass, IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderData => LocalResource(pass, buffer, scope, () => new RenderGraphShaderDataBuffer<T>(this, buffer, default, "pass-local-shader-data"));

    internal RenderGraphSampler GetSamplerResource(IGraphicsSampler sampler)
    {
        EnsureBuilding();
        ArgumentNullException.ThrowIfNull(sampler);
        RenderGraphSampler? existing = _resources.OfType<RenderGraphSampler>().FirstOrDefault(resource => ReferenceEquals(resource.Key, sampler));
        if (existing is not null)
        {
            return existing;
        }

        var created = new RenderGraphSampler(this, sampler);
        _resources.Add(created);
        return created;
    }

    internal RenderGraphSampler GetSamplerResource(SamplerDesc desc)
    {
        EnsureBuilding();
        ArgumentNullException.ThrowIfNull(desc);
        RenderGraphSampler? existing = _resources.OfType<RenderGraphSampler>().FirstOrDefault(resource => resource.Key is SamplerDesc value && value == desc);
        if (existing is not null)
        {
            return existing;
        }

        var created = new RenderGraphSampler(this, desc);
        _resources.Add(created);
        return created;
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 256)
        {
            throw new ArgumentException("Diagnostic names are limited to 256 characters.", nameof(name));
        }
    }

    private static void Release(List<IRenderGraphLifetime> lifetimes)
    {
        while (lifetimes.Count != 0)
        {
            lifetimes[^1].Dispose();
            lifetimes.RemoveAt(lifetimes.Count - 1);
        }
    }

    private T ResolveExtension<T>(Func<RenderGraph, T>? fallback)
        where T : class
    {
        EnsureBuilding();
        RuntimeTypeHandle type = typeof(T).TypeHandle;
        if (_extensionStates is { } existing && existing.TryGetValue(type, out object? cached))
        {
            if (cached is null)
            {
                _faulted = true;
                throw new InvalidOperationException("Extension factories contain a dependency cycle.");
            }

            return (T)cached;
        }

        Func<RenderGraph, object>? factory = _extensions?.Find(type) ?? fallback;
        if (factory is null)
        {
            throw new InvalidOperationException($"No render graph extension is registered for '{typeof(T)}'.");
        }

        Dictionary<RuntimeTypeHandle, object?> states = _extensionStates ??= [];
        states.Add(type, null);
        _creatingExtension++;
        try
        {
            object value = factory(this);
            EnsureBuilding();
            states[type] = value;
            return (T)value;
        }
        catch
        {
            _faulted = true;
            throw;
        }
        finally
        {
            _creatingExtension--;
        }
    }

    private CompiledRenderGraph CompileCore(RenderGraphRun run, RenderGraphCaptureOptions? capture)
    {
        using RenderGraphProfiling.ProfileScope? dependenciesProfile = RenderGraphProfiling.Log.BeginChild(2);
        RenderGraphProfiling.ProfileScope? orderProfile = null;
        RenderGraphProfiling.ProfileScope? planProfile = null;
        try
        {
            var indexes = _passes.Select((pass, index) => (pass, index)).ToDictionary(item => item.pass, item => item.index);
            HashSet<int>[] ordering = _passes.Select(pass => pass.Dependencies.Select(dependency => indexes[dependency]).ToHashSet()).ToArray();
            HashSet<int>[] required = ordering.Select(dependencies => new HashSet<int>(dependencies)).ToArray();
            var lastWriters = new Dictionary<RenderGraphResource, int>();
            var accesses = new Dictionary<RenderGraphResource, List<(int Index, RenderGraphUse Use)>>();
            var roots = new HashSet<int>();
            for (int index = 0; index < _passes.Count; index++)
            {
                RenderGraphPass pass = _passes[index];
                if (pass.IsRoot)
                {
                    roots.Add(index);
                }

                foreach (RenderGraphUse use in pass.Uses)
                {
                    bool write = RenderGraphValidation.IsWrite(use.Scope);
                    bool requiresContents = (use.Scope.Access & ~RenderGraphValidation.Writes) != 0 || (write && !use.Discard);
                    if (requiresContents && lastWriters.TryGetValue(use.Resource, out int producer))
                    {
                        required[index].Add(producer);
                    }

                    if (!accesses.TryGetValue(use.Resource, out List<(int Index, RenderGraphUse Use)>? prior))
                    {
                        prior = [];
                        accesses.Add(use.Resource, prior);
                    }

                    foreach ((int before, RenderGraphUse previous) in prior)
                    {
                        if (write || RenderGraphValidation.IsWrite(previous.Scope) || use.State != previous.State)
                        {
                            ordering[index].Add(before);
                        }
                    }

                    prior.Add((index, use));
                    if (write)
                    {
                        lastWriters[use.Resource] = index;
                    }
                }
            }

            foreach (RenderGraphResource output in _outputs)
            {
                if (lastWriters.TryGetValue(output, out int producer))
                {
                    roots.Add(producer);
                }
                else if (!output.InitiallyInitialized)
                {
                    throw new InvalidOperationException($"Output '{output.Name}' has no initialized producer.");
                }
            }

            dependenciesProfile?.Complete();
            dependenciesProfile?.Dispose();
            orderProfile = RenderGraphProfiling.Log.BeginChild(3);
            var live = new HashSet<int>();
            var pending = new Stack<int>(roots);
            while (pending.TryPop(out int index))
            {
                if (live.Add(index))
                {
                    foreach (int dependency in required[index])
                    {
                        pending.Push(dependency);
                    }
                }
            }

            int[] degree = ordering.Select(dependencies => dependencies.Count).ToArray();
            List<int>[] followers = Enumerable.Range(0, _passes.Count).Select(_ => new List<int>()).ToArray();
            var ready = new PriorityQueue<int, int>();
            for (int index = 0; index < _passes.Count; index++)
            {
                foreach (int dependency in ordering[index])
                {
                    followers[dependency].Add(index);
                }

                if (degree[index] == 0)
                {
                    ready.Enqueue(index, index);
                }
            }

            var ordered = new List<int>();
            int visited = 0;
            while (ready.TryDequeue(out int index, out _))
            {
                visited++;
                if (live.Contains(index))
                {
                    ordered.Add(index);
                }

                foreach (int following in followers[index])
                {
                    if (--degree[following] == 0)
                    {
                        ready.Enqueue(following, following);
                    }
                }
            }

            if (visited != _passes.Count)
            {
                throw new InvalidOperationException("Render graph dependencies contain a cycle: " + string.Join(", ", _passes.Where((_, index) => degree[index] != 0).Select(pass => pass.Name)));
            }

            orderProfile?.Complete();
            orderProfile?.Dispose();
            planProfile = RenderGraphProfiling.Log.BeginChild(4);
            var plan = new CompiledRenderGraph(this, _passes.ToArray(), ordered, _resources.ToArray(), _outputs.ToHashSet(), _exports.Values.ToArray(), ordering, run, capture);
            planProfile?.Complete();
            return plan;
        }
        catch (Exception exception)
        {
            dependenciesProfile?.Fail(exception);
            orderProfile?.Fail(exception);
            planProfile?.Fail(exception);
            throw;
        }
        finally
        {
            planProfile?.Dispose();
            orderProfile?.Dispose();
        }
    }

    private TResource LocalResource<TResource>(RenderGraphPass pass, object allocation, BarrierScope scope, Func<TResource> create)
        where TResource : RenderGraphResource
    {
        EnsureBuilding();
        ArgumentNullException.ThrowIfNull(allocation);
        RenderGraphValidation.Scope(scope, required: true);
        RenderGraphResource? existing = _resources.FirstOrDefault(resource => ReferenceEquals(resource.Allocation, allocation));
        if (existing is not null)
        {
            if (existing is not TResource typed || (typed.LocalPassOwner is not null && !ReferenceEquals(typed.LocalPassOwner, pass)))
            {
                throw new ArgumentException("The allocation has another type, kind, or pass-local owner.", nameof(allocation));
            }

            typed.Validate(TextureState.Undefined, scope);
            return typed;
        }

        TResource result = create();
        result.Validate(TextureState.Undefined, scope);
        result.LocalPassOwner = pass;
        Register(result);
        return result;
    }

    private void Export(RenderGraphResource resource, TextureState state, BarrierScope scope)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.LocalPassOwner is not null)
        {
            throw new ArgumentException("Pass-local storage cannot be exported.", nameof(resource));
        }

        EnsureBuilding();
        CheckResource(resource);
        RenderGraphValidation.Scope(scope, required: state != TextureState.Present);
        if (state == TextureState.Present && scope != default)
        {
            throw new ArgumentException("Presentation exports require an empty scope.", nameof(scope));
        }

        resource.Validate(state, scope);
        _outputs.Add(resource);
        _exports[resource] = new RenderGraphUse(resource, state, scope);
    }

    private void Register(RenderGraphResource resource)
    {
        EnsureBuilding();
        RenderGraphValidation.Scope(resource.InitialScope, required: false);
        resource.Validate(resource.InitialState, resource.InitialScope);
        if (resource.Allocation is object allocation && !_allocations.Add(allocation))
        {
            throw new ArgumentException("An allocation may only be imported once into a graph.", nameof(resource));
        }

        _resources.Add(resource);
    }

    private string ResourceName(string? name)
    {
        string resolved = name ?? $"resource-{_resources.Count}";
        ValidateName(resolved);
        return resolved;
    }

    private void RequireDevice()
    {
        if (Device is null)
        {
            throw new InvalidOperationException("Pass a device to the graph before declaring temporary resources.");
        }
    }

    private sealed class LifetimeGroup(List<IRenderGraphLifetime> lifetimes) : IRenderGraphPreparation
    {
        public void Validate()
        {
            foreach (IRenderGraphLifetime lifetime in lifetimes)
            {
                lifetime.Validate();
            }
        }

        public void Prepare(RenderGraphPreparationContext context)
        {
            foreach (IRenderGraphPreparation preparation in lifetimes.OfType<IRenderGraphPreparation>())
            {
                preparation.Prepare(context);
            }
        }

        public void Dispose() => Release(lifetimes);
    }
}
