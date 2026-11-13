using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

internal sealed class GraphShaderResourceOwner : IRenderGraphPreparation
{
    private readonly RenderGraph _graph;
    private readonly ArgumentTablePlan _registrations;
    private readonly Dictionary<RenderGraphPass, GraphBindingLookup> _lookups = [];
    private readonly Dictionary<ShaderResources, RenderGraphPass> _byResources = [];
    private readonly Dictionary<RenderGraphPass, List<ShaderResources>> _modules = [];
    private readonly HashSet<ShaderResources> _moduleScopes = [];
    private readonly Dictionary<RenderGraphPass, IReadOnlyList<RenderGraphUse>> _uses = [];
    private readonly Dictionary<RenderGraphPass, List<(object Resource, ulong Offset, ulong Size, ArgumentBufferAccess Access)>> _allowed = [];
    private readonly Dictionary<SamplerDesc, IGraphicsSampler> _samplers = [];
    private readonly List<IDisposable> _owned = [];
    private RenderGraphPreparationContext? _preparation;
    private bool _disposed;
    private bool _prepared;
    private bool _collectingDeclarations;

    internal GraphShaderResourceOwner(RenderGraph graph)
    {
        _graph = graph;
        _registrations = new(graph.Device ?? throw new InvalidOperationException("Shader references require a graph device."));
    }

    public void Validate() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Prepare(RenderGraphPreparationContext context)
    {
        Validate();
        if (_prepared)
        {
            throw new InvalidOperationException("Pass references prepare once per compiled generation.");
        }

        _prepared = true;
        _preparation = context;
        _collectingDeclarations = true;
        try
        {
            foreach (ShaderResources scope in _moduleScopes.Where(_registrations.Owns))
            {
                if (!_byResources.ContainsKey(scope))
                {
                    throw new InvalidOperationException("A prepared payload has no consuming pass.");
                }
            }

            var active = new List<ShaderResources>();
            foreach (RenderGraphPass pass in context.LivePasses)
            {
                IReadOnlyList<RenderGraphUse> uses = context.GetUses(pass);
                _uses.Add(pass, uses);
                var core = new ModuleResources(this, _registrations.CreateShaderResources());
                _byResources.Add(core, pass);
                _allowed.Add(pass, []);
                core.Inspect((resource, offset, size, access) => _allowed[pass].Add((resource, offset, size, access)));
                var lookup = new GraphBindingLookup(this, core);
                _lookups.Add(pass, lookup);
                bool graphics = uses.Any(use => use.State is TextureState.ColorAttachment or TextureState.DepthStencilAttachment);
                uint colors = checked((uint)uses.Count(use => use.State == TextureState.ColorAttachment));
                _registrations.ConfigureExecution(core, colors, graphics);
                foreach (GraphShaderBinding binding in uses.SelectMany(use => use.Bindings ?? []))
                {
                    binding.Register?.Invoke(lookup);
                }

                active.Add(core);
                foreach (ShaderResources module in _modules.GetValueOrDefault(pass) ?? [])
                {
                    if (!_registrations.Owns(module))
                    {
                        continue;
                    }

                    _registrations.ConfigureExecution(module, colors, graphics);
                    ((ModuleResources)module).CoRecord(core);
                    ((ModuleResources)module).Inspect((resource, offset, size, access) => CheckAllowed(pass, resource, offset, size, access));
                    active.Add(module);
                }
            }

            // Shader-data copies retain encoded native table identities. Connect overlapping producer/consumer ranges,
            // including copy-only passes, without executing callbacks or inspecting their closures.
            var ranges = new List<(RenderGraphPass Pass, GraphShaderBinding Binding)>();
            foreach (RenderGraphPass pass in context.LivePasses)
            {
                GraphShaderBinding[] data = _uses[pass].SelectMany(use => use.Bindings ?? []).Where(binding => binding.ShaderData).ToArray();
                foreach (GraphShaderBinding binding in data)
                {
                    foreach ((RenderGraphPass previous, GraphShaderBinding other) in ranges)
                    {
                        if (ReferenceEquals(binding.Resource, other.Resource) && binding.Offset < other.Offset + other.Count && other.Offset < binding.Offset + binding.Count)
                        {
                            ((ModuleResources)_lookups[pass].Core).SameTable(_lookups[previous].Core);
                        }
                    }

                    ranges.Add((pass, binding));
                }
            }

            _collectingDeclarations = false;
            _registrations.MaterializeResources(active);
        }
        catch
        {
            _registrations.Invalidate();
            throw;
        }
        finally
        {
            _collectingDeclarations = false;
            _preparation = null;
        }
    }

    public void Dispose()
    {
        _registrations.Dispose();
        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }

        _samplers.Clear();
        _lookups.Clear();
        _allowed.Clear();
        _uses.Clear();
        _byResources.Clear();
        _modules.Clear();
        _moduleScopes.Clear();
        _disposed = true;
    }

    internal static GraphShaderResourceOwner Get(RenderGraph graph) => graph.GetOrAddExtension(owner =>
    {
        var resources = new GraphShaderResourceOwner(owner);
        owner.ShaderResourceOwner = resources;
        owner.Retain(resources);
        return resources;
    });

    internal GraphBindingLookup Lookup(RenderGraphPass pass)
    {
        Validate();
        return _lookups.GetValueOrDefault(pass) ?? throw new ArgumentException("This pass has no prepared shader declarations.");
    }

    internal IShaderArgumentScope Scope(IReadOnlyList<ShaderResources> resources) => ((ModuleResources)resources[0]).Scope(resources);

    internal void Bind(ShaderResources resources, IRenderEncoder encoder) => ((ModuleResources)resources).BindRender(encoder);

    internal void Bind(ShaderResources resources, IComputeEncoder encoder) => ((ModuleResources)resources).BindCompute(encoder);

    internal void ValidateResource(RenderGraphResource resource)
    {
        if (!ReferenceEquals(resource.Owner, _graph))
        {
            throw new ArgumentException("The logical resource belongs to another graph.");
        }
    }

    internal void ValidateLookup(ShaderResources resources, RenderGraphPass pass)
    {
        Validate();
        if (!_byResources.TryGetValue(resources, out RenderGraphPass? consumer) || !ReferenceEquals(consumer, pass))
        {
            throw new ArgumentException("Shader references belong to another graph or pass.");
        }

        if (!resources.IsReady)
        {
            throw new InvalidOperationException("The pass references are not ready for recording.");
        }
    }

    internal IReadOnlyList<ShaderResources> SelectArguments(IShaderReferenceMetadata references, RenderGraphPass pass, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
        => SelectArguments(references.References, pass, outputs, graphics);

    internal ShaderResources CreateModule()
    {
        ValidateBuilding();
        var core = new ModuleResources(this, _registrations.CreateShaderResources());
        _moduleScopes.Add(core);
        return core;
    }

    internal void Attach(RenderGraphPass pass, ShaderResources resources)
    {
        ValidateBuilding();
        ValidateModule(resources);
        if (!ReferenceEquals(pass.Owner, _graph) || _byResources.ContainsKey(resources))
        {
            throw new ArgumentException("Prepared payloads belong to one declaring pass.");
        }

        _byResources.Add(resources, pass);
        if (!_modules.TryGetValue(pass, out List<ShaderResources>? modules))
        {
            modules = [];
            _modules.Add(pass, modules);
        }

        modules.Add(resources);
    }

    internal void ValidateModule(ShaderResources resources)
    {
        ValidateBuilding();
        if (!_registrations.Owns(resources))
        {
            throw new ArgumentException("The prepared payload belongs to another graph.");
        }
    }

    internal void ValidateExecution(ShaderResources resources, RenderGraphPass pass, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
    {
        ValidateLookup(resources, pass);
        IReadOnlyList<RenderGraphUse> uses = _uses[pass];
        bool declaredGraphics = uses.Any(use => use.State is TextureState.ColorAttachment or TextureState.DepthStencilAttachment);
        if (declaredGraphics != graphics || outputs.Any(output => !uses.Any(use => ReferenceEquals(use.Resource, output) && use.State == TextureState.ColorAttachment)))
        {
            throw new ArgumentException("The execution attachments or shader role differ from the pass declarations.");
        }
    }

    internal IGraphicsTextureView Texture(ShaderResources resources, RenderGraphTexture texture, TextureViewDesc? desc) => Preparation().CreateTextureView(Pass(resources), texture, desc);

    internal IGraphicsTextureView BorrowedView(ShaderResources resources, RenderGraphTexture texture, IGraphicsTextureView view)
    {
        if (!ReferenceEquals(Preparation().GetTexture(Pass(resources), texture), view.Texture))
        {
            throw new ArgumentException("The borrowed view does not refer to the declared image.");
        }

        return view;
    }

    internal IGraphicsBuffer<T> Buffer<T>(ShaderResources resources, RenderGraphBuffer<T> buffer, ArgumentBufferAccess access)
        where T : unmanaged => Preparation().GetBuffer(Pass(resources), buffer, access);

    internal IGraphicsShaderDataBuffer<T> Buffer<T>(ShaderResources resources, RenderGraphShaderDataBuffer<T> buffer)
        where T : IShaderData => Preparation().GetShaderDataBuffer(Pass(resources), buffer);

    internal IGraphicsSampler Sampler(ShaderResources resources, RenderGraphSampler sampler) => Preparation().GetSampler(Pass(resources), sampler);

    internal IGraphicsSampler Sampler(SamplerDesc desc)
    {
        ValidateBuilding();
        if (!_samplers.TryGetValue(desc, out IGraphicsSampler? sampler))
        {
            sampler = _registrations.Device.CreateSampler(desc);
            _owned.Add(sampler);
            _samplers.Add(desc, sampler);
        }

        return sampler;
    }

    internal void ValidateBuilding()
    {
        Validate();
        if (!_collectingDeclarations && (!_graph.IsBuilding || _prepared))
        {
            throw new InvalidOperationException("Pass shader declarations are closed.");
        }
    }

    private IReadOnlyList<ShaderResources> SelectArguments(IReadOnlyList<IShaderReference> references, RenderGraphPass pass, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
    {
        ShaderResources core = Lookup(pass).Core;
        ValidateExecution(core, pass, outputs, graphics);
        ValidateLookup(core, pass);
        foreach (IShaderReference reference in references)
        {
            if (!((ModuleResources)core).Accepts(reference))
            {
                throw new ArgumentException("Root references must originate from this pass's shader declarations.");
            }
        }

        return [core];
    }

    private void CheckAllowed(RenderGraphPass pass, object resource, ulong offset, ulong size, ArgumentBufferAccess access)
    {
        if (!_allowed[pass].Any(value => ReferenceEquals(value.Resource, resource) && offset >= value.Offset && offset - value.Offset <= value.Size && size <= value.Size - (offset - value.Offset) && (access & ~value.Access) == 0))
        {
            throw new ArgumentException("A prepared payload uses a view, sampler, range or access absent from its standard pass declarations.");
        }
    }

    private RenderGraphPass Pass(ShaderResources resources) => _byResources.GetValueOrDefault(resources) ?? throw new InvalidOperationException("The prepared payload has no pass.");

    private RenderGraphPreparationContext Preparation() => _preparation ?? throw new InvalidOperationException("Graph allocations resolve only during preparation.");

    private sealed class ModuleResources : ShaderResources
    {
        private readonly GraphShaderResourceOwner _owner;

        internal ModuleResources(GraphShaderResourceOwner owner, ShaderResources resources)
            : base(resources)
        {
            _owner = owner;
            ValidateBeforeCollection(owner.ValidateBuilding);
        }

        public override ShaderResources CreateSibling() => _owner.CreateModule();

        internal bool Accepts(IShaderReference reference) => ContainsReference(reference);

        internal void Inspect(Action<object, ulong, ulong, ArgumentBufferAccess> inspect) => InspectBindingRanges(inspect);

        internal void CoRecord(ShaderResources other) => JoinOperation(other);

        internal void SameTable(ShaderResources other) => JoinTable(other);

        internal IShaderArgumentScope Scope(IReadOnlyList<ShaderResources> candidates) => CreateArgumentScope(candidates);

        internal void BindRender(IRenderEncoder encoder) => Bind(encoder);

        internal void BindCompute(IComputeEncoder encoder) => Bind(encoder);
    }
}
