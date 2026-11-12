using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Owns declared graph-resource registrations and ordered table partitions through compiled-plan disposal.</summary>
public sealed class RenderGraphArguments : IRenderGraphPreparation
{
    private readonly RenderGraph _graph;
    private readonly Dictionary<RenderGraphPass, List<ArgumentTableReservation>> _byPass = [];
    private readonly Dictionary<ArgumentTableReservation, RenderGraphPass> _byScope = [];
    private readonly Dictionary<SamplerDesc, IGraphicsSampler> _samplers = [];
    private readonly List<IDisposable> _owned = [];
    private readonly List<(ArgumentTableReservation Trigger, Action Finalize)> _finalizers = [];
    private RenderGraphPreparationContext? _preparation;
    private bool _disposed;
    private bool _prepared;

    internal RenderGraphArguments(RenderGraph graph)
    {
        _graph = graph;
        Registrations = new(graph.Device ?? throw new InvalidOperationException("Graph arguments require a physical device."));
    }

    /// <summary>Gets the borrowed common allocator; native resources are owned by the graph or their explicit owners.</summary>
    public ArgumentTablePlan Registrations { get; }

    /// <summary>Reserves one whole draw in the specified pass.</summary>
    /// <param name="pass">The exact consuming pass.</param>
    /// <param name="colorAttachments">Actual combined-budget color attachments.</param>
    /// <returns>A draw-local scope bound to one selected table.</returns>
    public ArgumentTableReservation Reserve(RenderGraphPass pass, uint colorAttachments = 1)
    {
        ValidateBuilding();
        ArgumentNullException.ThrowIfNull(pass);
        if (!ReferenceEquals(pass.Owner, _graph))
        {
            throw new ArgumentException("The argument pass belongs to another graph.", nameof(pass));
        }

        ArgumentTableReservation scope = Registrations.Reserve(colorAttachments);
        Attach(pass, [scope]);
        return scope;
    }

    /// <summary>Associates existing scopes with one opaque pass; private buffers remain that pass owner's responsibility.</summary>
    /// <param name="pass">The exact consuming pass.</param>
    /// <param name="scopes">Scopes in draw order; repeated or foreign scopes are rejected.</param>
    /// <param name="privateBuffers">Permits pass-owned buffers that were never imported into this graph.</param>
    public void Attach(RenderGraphPass pass, IReadOnlyList<ArgumentTableReservation> scopes, bool privateBuffers = false)
    {
        ValidateBuilding();
        ArgumentNullException.ThrowIfNull(pass);
        if (!ReferenceEquals(pass.Owner, _graph))
        {
            throw new ArgumentException("The argument pass belongs to another graph.", nameof(pass));
        }

        foreach (ArgumentTableReservation scope in scopes)
        {
            if (!Registrations.Reservations.Contains(scope) || _byScope.ContainsKey(scope))
            {
                throw new ArgumentException("A reservation must belong to this allocator and exactly one pass.", nameof(scopes));
            }
        }

        if (scopes.Distinct().Count() != scopes.Count)
        {
            throw new ArgumentException("Duplicate draw reservations are not execution occurrences.", nameof(scopes));
        }

        if (!_byPass.TryGetValue(pass, out List<ArgumentTableReservation>? list))
        {
            list = [];
            _byPass.Add(pass, list);
        }

        foreach (ArgumentTableReservation scope in scopes)
        {
            _byScope.Add(scope, pass);
            list.Add(scope);
            scope.InspectResources((resource, access) =>
            {
                RenderGraphPreparationContext context = Preparation();
                if (resource is IGraphicsSampler)
                {
                    return;
                }

                if (resource is IGraphicsTextureView view)
                {
                    context.CheckAllocation(pass, view.Texture, ResourceAccess.ShaderRead, sampled: true);
                }
                else if (!privateBuffers || context.IsGraphAllocation(resource))
                {
                    ResourceAccess required = ((access & ArgumentBufferAccess.Read) != 0 ? ResourceAccess.ShaderRead : 0) | ((access & ArgumentBufferAccess.Write) != 0 ? ResourceAccess.ShaderWrite : 0);
                    context.CheckAllocation(pass, resource, required, sampled: false);
                }
            });
        }
    }

    /// <summary>Reserves a graph-owned view without resolving transient storage early or adding dependencies implicitly.</summary>
    /// <param name="scope">The consuming draw scope.</param>
    /// <param name="texture">The sampled handle; its pass must declare Sampled/ShaderRead.</param>
    /// <param name="desc">The selected view range.</param>
    /// <returns>A real-reference handle materialized before recording.</returns>
    public ArgumentRegistration<IGraphicsTextureView> RegisterTexture(ArgumentTableReservation scope, RenderGraphTexture texture, TextureViewDesc? desc = null)
    {
        ValidateBuilding();
        return scope.RegisterTexture(() => Preparation().CreateTextureView(Pass(scope), texture, desc));
    }

    /// <summary>Reserves the exact typed range of a graph buffer; no overlapping-range merge is performed.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="scope">The consuming scope.</param>
    /// <param name="buffer">The declared graph buffer.</param>
    /// <param name="offset">The element base.</param>
    /// <param name="count">The element count.</param>
    /// <param name="access">Actual shader descriptor uses.</param>
    /// <returns>The pending real-reference handle.</returns>
    public ArgumentRegistration<T> RegisterBuffer<T>(ArgumentTableReservation scope, RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        ValidateBuilding();
        return scope.RegisterBuffer(() => Preparation().GetBuffer(Pass(scope), buffer, access).SliceElements(offset, count), access);
    }

    /// <summary>Reserves a shader-data range whose allocation and transfer metadata belong to the graph.</summary>
    /// <typeparam name="T">The logical element type.</typeparam>
    /// <param name="scope">The consuming draw.</param>
    /// <param name="buffer">The declared shader-data handle.</param>
    /// <param name="offset">The element base.</param>
    /// <param name="count">The element count.</param>
    /// <returns>The pending real-reference handle.</returns>
    public ArgumentRegistration<T> RegisterBuffer<T>(ArgumentTableReservation scope, RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count)
        where T : struct, IShaderData
    {
        ValidateBuilding();
        return scope.RegisterShaderData(() => Preparation().GetShaderDataBuffer(Pass(scope), buffer).SliceElements(offset, count));
    }

    /// <summary>Creates one immutable graph-owned default sampler per descriptor value; caller-owned samplers are never coalesced.</summary>
    /// <param name="desc">The exact immutable sampler configuration.</param>
    /// <returns>A sampler borrowed through normal graph disposal.</returns>
    public IGraphicsSampler GetSampler(SamplerDesc desc)
    {
        ValidateBuilding();
        if (!_samplers.TryGetValue(desc, out IGraphicsSampler? sampler))
        {
            sampler = Registrations.Device.CreateSampler(desc);
            _owned.Add(sampler);
            _samplers.Add(desc, sampler);
        }

        return sampler;
    }

    /// <summary>Schedules synchronous payload packing after all active references materialize.</summary>
    /// <param name="trigger">A scope live whenever this payload is needed.</param>
    /// <param name="finalize">Must not await, submit, wait, or record GPU commands.</param>
    public void OnMaterialized(ArgumentTableReservation trigger, Action finalize)
    {
        ValidateBuilding();
        ArgumentNullException.ThrowIfNull(finalize);
        _finalizers.Add((trigger, finalize));
    }

    /// <inheritdoc/>
    public void Validate() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc/>
    public void Prepare(RenderGraphPreparationContext context)
    {
        Validate();
        if (_prepared)
        {
            throw new InvalidOperationException("Graph argument generations materialize once.");
        }

        _prepared = true;
        _preparation = context;
        try
        {
            if (Registrations.Reservations.Any(scope => !_byScope.ContainsKey(scope)))
            {
                throw new InvalidOperationException("Every graph reservation must be attached to a declared pass.");
            }

            ArgumentTableReservation[] active = context.LivePasses.SelectMany(pass => _byPass.TryGetValue(pass, out List<ArgumentTableReservation>? scopes) ? scopes : []).ToArray();
            Registrations.Materialize(active);
            foreach ((ArgumentTableReservation trigger, Action finalize) in _finalizers)
            {
                if (trigger.IsMaterialized)
                {
                    finalize();
                }
            }
        }
        catch
        {
            Registrations.Invalidate();
            throw;
        }
        finally
        {
            _preparation = null;
            _finalizers.Clear();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Registrations.Dispose();
        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }

        _samplers.Clear();
        _finalizers.Clear();
        _byPass.Clear();
        _byScope.Clear();
        _disposed = true;
    }

    private RenderGraphPass Pass(ArgumentTableReservation scope) => _byScope.TryGetValue(scope, out RenderGraphPass? pass) ? pass : throw new InvalidOperationException("Attach the draw to its consuming pass before materialization.");

    private RenderGraphPreparationContext Preparation() => _preparation ?? throw new InvalidOperationException("Graph references resolve only at the materialization boundary.");

    private void ValidateBuilding()
    {
        Validate();
        if (!_graph.IsBuilding || _prepared)
        {
            throw new InvalidOperationException("Graph argument reservation collection is closed.");
        }
    }
}
