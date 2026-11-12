using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

internal sealed class GraphShaderResourceOwner : IRenderGraphPreparation
{
    private readonly RenderGraph _graph;
    private readonly ArgumentTablePlan _registrations;
    private readonly Dictionary<RenderGraphPass, List<ShaderResources>> _byPass = [];
    private readonly Dictionary<ShaderResources, RenderGraphPass> _byResources = [];
    private readonly Dictionary<SamplerDesc, IGraphicsSampler> _samplers = [];
    private readonly Dictionary<ShaderResources, (RenderGraphTexture[] Outputs, bool Graphics)> _execution = [];
    private readonly List<IDisposable> _owned = [];
    private RenderGraphPreparationContext? _preparation;
    private bool _disposed;
    private bool _prepared;

    private GraphShaderResourceOwner(RenderGraph graph)
    {
        _graph = graph;
        _registrations = new(graph.Device ?? throw new InvalidOperationException("Shader resources require a physical device."));
    }

    internal ShaderTarget Target => _registrations.Device.Caps.ShaderTarget;

    public void Validate() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Prepare(RenderGraphPreparationContext context)
    {
        Validate();
        if (_prepared)
        {
            throw new InvalidOperationException("Graph shader resources prepare once.");
        }

        _prepared = true;
        _preparation = context;
        try
        {
            foreach (ShaderResources canceled in _byResources.Keys.Where(resources => !_registrations.Owns(resources)).ToArray())
            {
                _byPass[_byResources[canceled]].Remove(canceled);
                _byResources.Remove(canceled);
            }

            if (_registrations.Reservations.Count != _byResources.Count)
            {
                throw new InvalidOperationException("Every shader resource set must belong to exactly one pass.");
            }

            ShaderResources[] active = context.LivePasses.SelectMany(pass => _byPass.TryGetValue(pass, out List<ShaderResources>? resources) ? resources : []).ToArray();
            _registrations.MaterializeResources(active);
        }
        catch
        {
            _registrations.Invalidate();
            throw;
        }
        finally
        {
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
        _byPass.Clear();
        _byResources.Clear();
        _disposed = true;
    }

    internal static GraphShaderResourceOwner Get(RenderGraph graph) => graph.GetOrAddExtension(owner =>
    {
        var resources = new GraphShaderResourceOwner(owner);
        owner.Retain(resources);
        return resources;
    });

    internal void Bind(ShaderResources resources, IRenderEncoder encoder) => ((ModuleResources)resources).BindRender(encoder);

    internal void Bind(ShaderResources resources, IComputeEncoder encoder) => ((ModuleResources)resources).BindCompute(encoder);

    internal RenderGraphShaderResources Create(ShaderArtifact artifact, string root)
    {
        ValidateBuilding();
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new(this, CreateModule(), artifact, Target, root);
    }

    internal ShaderResources CreateModule()
    {
        ValidateBuilding();
        return new ModuleResources(this, _registrations.CreateShaderResources());
    }

    internal void Use(RenderGraphPass pass, ShaderResources resources, IReadOnlyList<RenderGraphTexture> attachments, bool? graphics = null)
    {
        ValidateBuilding();
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(attachments);
        if (!ReferenceEquals(pass.Owner, _graph) || !_registrations.Owns(resources) || _byResources.ContainsKey(resources))
        {
            throw new ArgumentException("Shader resources must belong to this graph and exactly one consuming pass.", nameof(resources));
        }

        if (attachments.Distinct().Count() != attachments.Count || attachments.Any(texture => !pass.Uses.Any(use => ReferenceEquals(use.Resource, texture) && use.State == TextureState.ColorAttachment && (use.Scope.Access & ResourceAccess.ColorWrite) != 0)))
        {
            throw new ArgumentException("Declare each actual color output exactly once before connecting shader resources.", nameof(attachments));
        }

        bool render = graphics ?? attachments.Count != 0;
        if (!render && attachments.Count != 0)
        {
            throw new ArgumentException("Compute resources cannot have color outputs.", nameof(attachments));
        }

        _registrations.ConfigureExecution(resources, checked((uint)attachments.Count), render);
        _execution.Add(resources, (attachments.ToArray(), render));
        if (!_byPass.TryGetValue(pass, out List<ShaderResources>? list))
        {
            list = [];
            _byPass.Add(pass, list);
        }

        _byResources.Add(resources, pass);
        list.Add(resources);
        resources.InspectResources((resource, access) =>
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
            else
            {
                ResourceAccess required = ((access & ArgumentBufferAccess.Read) != 0 ? ResourceAccess.ShaderRead : 0) | ((access & ArgumentBufferAccess.Write) != 0 ? ResourceAccess.ShaderWrite : 0);
                context.CheckAllocation(pass, resource, required, sampled: false);
            }
        });
    }

    internal void ValidateModule(ShaderResources resources)
    {
        ValidateBuilding();
        if (!_registrations.Owns(resources))
        {
            throw new ArgumentException("The prepared module resources belong to another graph.", nameof(resources));
        }
    }

    internal void ValidateExecution(ShaderResources resources, RenderGraphPass pass, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
    {
        Validate();
        if (!_byResources.TryGetValue(resources, out RenderGraphPass? consumer) || !ReferenceEquals(consumer, pass) || !_execution.TryGetValue(resources, out (RenderGraphTexture[] Outputs, bool Graphics) shape) || shape.Graphics != graphics || !shape.Outputs.SequenceEqual(outputs))
        {
            throw new ArgumentException("Shader inputs belong to another pass, graph, or execution shape.", nameof(resources));
        }
    }

    internal IGraphicsTextureView Texture(ShaderResources resources, RenderGraphTexture texture, TextureViewDesc? desc) => Preparation().CreateTextureView(Pass(resources), texture, desc);

    internal IGraphicsBuffer<T> Buffer<T>(ShaderResources resources, RenderGraphBuffer<T> buffer, ArgumentBufferAccess access)
        where T : unmanaged => Preparation().GetBuffer(Pass(resources), buffer, access);

    internal IGraphicsShaderDataBuffer<T> Buffer<T>(ShaderResources resources, RenderGraphShaderDataBuffer<T> buffer)
        where T : struct, IShaderData => Preparation().GetShaderDataBuffer(Pass(resources), buffer);

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
        if (!_graph.IsBuilding || _prepared)
        {
            throw new InvalidOperationException("Graph shader resource collection is closed.");
        }
    }

    private RenderGraphPass Pass(ShaderResources resources) => _byResources.TryGetValue(resources, out RenderGraphPass? pass) ? pass : throw new InvalidOperationException("Connect shader resources to their consuming pass before compilation.");

    private RenderGraphPreparationContext Preparation() => _preparation ?? throw new InvalidOperationException("Graph shader resources resolve only during preparation.");

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

        internal void BindRender(IRenderEncoder encoder) => Bind(encoder);

        internal void BindCompute(IComputeEncoder encoder) => Bind(encoder);
    }
}
