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
    private readonly Dictionary<RenderGraphPass, Dictionary<object, ArgumentBufferAccess>>? _allowed;
    private readonly Dictionary<RenderGraphPass, Action<IReadOnlyCollection<IShaderReference>>>? _admissions;
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
        if (_registrations.Device.ValidationEnabled)
        {
            _allowed = [];
            _admissions = [];
        }
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
                if (_allowed is not null)
                {
                    var declarations = new Dictionary<object, ArgumentBufferAccess>(ReferenceEqualityComparer.Instance);
                    foreach (RenderGraphUse use in uses)
                    {
                        object allocation = Canonical(context.Resolve(use.Resource));
                        ArgumentBufferAccess access = ((use.Scope.Access & ResourceAccess.ShaderRead) != 0 ? ArgumentBufferAccess.Read : 0) |
                            ((use.Scope.Access & ResourceAccess.ShaderWrite) != 0 ? ArgumentBufferAccess.Write : 0);
                        if (access != 0)
                        {
                            declarations[allocation] = declarations.GetValueOrDefault(allocation) | access;
                        }
                    }

                    _allowed.Add(pass, declarations);
                    core.Inspect((resource, offset, size, access) => CheckAllowed(pass, resource, access));
                }

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
                    if (_allowed is not null)
                    {
                        ((ModuleResources)module).Inspect((resource, offset, size, access) => CheckAllowed(pass, resource, access));
                    }

                    active.Add(module);
                }
            }

            // Shader-data copies and cached references retain table identities. Connect all uses of the same allocation,
            // including copy-only passes, without executing callbacks or inspecting their closures.
            var ranges = new List<(RenderGraphPass Pass, GraphShaderBinding Binding)>();
            foreach (RenderGraphPass pass in context.LivePasses)
            {
                GraphShaderBinding[] data = _uses[pass].SelectMany(use => use.Bindings ?? []).Where(binding => binding.ShaderData).ToArray();
                foreach (GraphShaderBinding binding in data)
                {
                    foreach ((RenderGraphPass previous, GraphShaderBinding other) in ranges)
                    {
                        if (ReferenceEquals(binding.Resource, other.Resource))
                        {
                            ((ModuleResources)_lookups[pass].Core).SameTable(_lookups[previous].Core);
                        }
                    }

                    ranges.Add((pass, binding));
                }
            }

            _collectingDeclarations = false;
            _registrations.MaterializeResources(active);
            if (_allowed is not null)
            {
                foreach (RenderGraphPass pass in context.LivePasses)
                {
                    _admissions!.Add(pass, CreateAdmission(new(_allowed[pass], ReferenceEqualityComparer.Instance)));
                }
            }
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
        _allowed?.Clear();
        _admissions?.Clear();
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

    internal Action<IReadOnlyCollection<IShaderReference>> Admission(IReadOnlyList<ShaderResources> resources) => _admissions![Pass(resources[0])];

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

    internal IReadOnlyList<ShaderResources> DeclaredArguments(RenderGraphPass pass, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
    {
        ShaderResources core = Lookup(pass).Core;
        ValidateExecution(core, pass, outputs, graphics);
        return Array.AsReadOnly(new[] { core });
    }

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
        where T : struct, IShaderArguments => Preparation().GetShaderDataBuffer(Pass(resources), buffer);

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

    // Detached allocation membership snapshot; native refreshed closure admission runs per execution.
    // Whole-buffer aggregate metadata may conservatively retain old dependencies and reject valid uses.
    private static Action<IReadOnlyCollection<IShaderReference>> CreateAdmission(Dictionary<object, ArgumentBufferAccess> declarations)
        => references =>
        {
            foreach (IShaderReference reference in references)
            {
                ArgumentBufferAccess access = reference.BufferAccess switch
                {
                    ShaderBufferAccess.ReadOnly => ArgumentBufferAccess.Read,
                    ShaderBufferAccess.ReadWrite => ArgumentBufferAccess.ReadWrite,
                    _ => throw new ArgumentException("The shader reference has an unsupported binding access."),
                };
                CheckAllowed(declarations, reference.Resource, access);
            }
        };

    private static object Canonical(object resource) => resource is IGraphicsTextureView view ? view.Texture : resource;

    private static void CheckAllowed(Dictionary<object, ArgumentBufferAccess> declarations, object resource, ArgumentBufferAccess access)
    {
        if (!declarations.TryGetValue(Canonical(resource), out ArgumentBufferAccess declared) || (access & ~declared) != 0)
        {
            throw new ArgumentException("A shader reference uses an allocation or access absent from this pass's declarations.");
        }
    }

    private IReadOnlyList<ShaderResources> SelectArguments(IReadOnlyList<IShaderReference> references, RenderGraphPass pass, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
    {
        IReadOnlyList<ShaderResources> resources = DeclaredArguments(pass, outputs, graphics);
        _admissions![pass](references);
        return resources;
    }

    private void CheckAllowed(RenderGraphPass pass, object resource, ArgumentBufferAccess access)
        => CheckAllowed(_allowed![pass], resource, access);

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

        internal void Inspect(Action<object, ulong, ulong, ArgumentBufferAccess> inspect) => InspectBindingRanges(inspect);

        internal void CoRecord(ShaderResources other) => JoinOperation(other);

        internal void SameTable(ShaderResources other) => JoinTable(other);

        internal void BindRender(IRenderEncoder encoder) => Bind(encoder);

        internal void BindCompute(IComputeEncoder encoder) => Bind(encoder);
    }
}
