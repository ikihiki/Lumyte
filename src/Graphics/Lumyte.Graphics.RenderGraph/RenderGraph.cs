using System.Diagnostics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Builds a single-queue render graph with explicit outputs and plan-owned temporary resources.</summary>
/// <remarks>The caller manages thread synchronization, imported resource lifetime and submission.</remarks>
public sealed class RenderGraph
{
    private readonly HashSet<object> _allocations = new(ReferenceEqualityComparer.Instance);
    private readonly List<RenderGraphResource> _resources = [];
    private readonly List<RenderGraphPass> _passes = [];
    private readonly HashSet<RenderGraphResource> _outputs = [];
    private readonly Dictionary<RenderGraphResource, RenderGraphUse> _exports = [];

    /// <summary>Initializes a new instance of the <see cref="RenderGraph"/> class.</summary>
    /// <param name="device">The same device used for recording; required for temporary allocations.</param>
    /// <param name="name">A diagnostic name of at most 256 characters.</param>
    /// <param name="observer">An optional diagnostic adapter.</param>
    public RenderGraph(IGraphicDevice? device = null, string name = "render-graph", IRenderGraphObserver? observer = null)
    {
        ValidateName(name);
        Device = device;
        Name = name;
        Observer = observer;
    }

    /// <summary>Gets the graph's diagnostic name.</summary>
    public string Name { get; }

    internal IGraphicDevice? Device { get; }

    internal IRenderGraphObserver? Observer { get; }

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

    /// <summary>Retains the last producer of a resource as an observable graph output.</summary>
    /// <param name="resource">The graph-local resource.</param>
    /// <returns>This graph.</returns>
    public RenderGraph MarkOutput(RenderGraphResource resource)
    {
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
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(record);
        var pass = new RenderGraphPass(this, name, record);
        _passes.Add(pass);
        return pass;
    }

    /// <summary>Compiles dependencies, culls unused work and plans barriers without GPU allocation.</summary>
    /// <returns>A disposable one-shot recording plan.</returns>
    public CompiledRenderGraph Compile()
    {
        using IDisposable? diagnostic = Observer?.Begin("compile");
        long started = Stopwatch.GetTimestamp();
        try
        {
            CompiledRenderGraph plan = CompileCore();
            Observer?.Compiled(plan.Snapshot, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return plan;
        }
        catch (Exception exception)
        {
            Observer?.Failed("compile", exception);
            throw;
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

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 256)
        {
            throw new ArgumentException("Diagnostic names are limited to 256 characters.", nameof(name));
        }
    }

    private CompiledRenderGraph CompileCore()
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
                if ((use.Scope.Access & ~RenderGraphValidation.Writes) != 0 && lastWriters.TryGetValue(use.Resource, out int producer))
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

        return new CompiledRenderGraph(this, _passes.ToArray(), ordered, _resources.ToArray(), _outputs.ToHashSet(), _exports.Values.ToArray(), ordering);
    }

    private void Export(RenderGraphResource resource, TextureState state, BarrierScope scope)
    {
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
}
