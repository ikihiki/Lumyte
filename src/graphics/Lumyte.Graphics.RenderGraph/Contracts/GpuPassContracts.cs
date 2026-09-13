namespace Lumyte.Graphics.RenderGraph;

public readonly record struct GpuRenderPassId(string Id, int Version);

public interface IGpuRenderPassContract<TRequest, TResult>
{
    string Id { get; }
    int Version { get; }
    TRequest Snapshot(TRequest request);
    TResult Declare(GpuPassDeclarationContext context, TRequest request);
}

public enum GpuRenderGraphAccess { Read, Write, ReadWrite }

public readonly record struct GpuRenderGraphUse(GpuRenderGraphResource Resource, GpuRenderGraphAccess Access);

/// <summary>The immutable feature declaration consumed by independently registered providers.</summary>
public sealed class GpuRenderGraphPass
{
    private readonly IReadOnlyDictionary<object, GpuInputSnapshot> constants;
    internal readonly IReadOnlyList<IGpuInputUse> Inputs;
    internal readonly IReadOnlyList<IGpuUploadData> Uploads;

    internal GpuRenderGraphPass(string name, GpuRenderPassId id, Type requestType, Type resultType, object? request, object? result,
        GpuPassDeclarationContext context)
    {
        Name = name;
        Id = id;
        RequestType = requestType;
        ResultType = resultType;
        Request = request;
        Result = result;
        Uses = Array.AsReadOnly(context.Uses.Select(pair => new GpuRenderGraphUse(pair.Key, pair.Value)).ToArray());
        Inputs = Array.AsReadOnly(context.Inputs.ToArray());
        Uploads = Array.AsReadOnly(context.Uploads.ToArray());
        constants = new System.Collections.ObjectModel.ReadOnlyDictionary<object, GpuInputSnapshot>(new Dictionary<object, GpuInputSnapshot>(context.Constants));
        IsPreserved = context.IsPreserved;
    }

    public string Name { get; }
    public GpuRenderPassId Id { get; }
    public Type RequestType { get; }
    public Type ResultType { get; }
    public object? Request { get; }
    public object? Result { get; }
    public IReadOnlyList<GpuRenderGraphUse> Uses { get; }
    public bool IsPreserved { get; }

    public T GetInput<T>(GpuGraphValue<T> value, GpuRenderGraphBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Inputs.Any(input => ReferenceEquals(input.ValueIdentity, value)))
        {
            throw new ArgumentException("The input was not declared by this pass.", nameof(value));
        }
        if (!bindings.Plan.Passes.Contains(this)) { throw new ArgumentException("Bindings belong to another plan.", nameof(bindings)); }
        return value.Input is not null ? bindings.GetInput(value) : (T)constants[value].Value!;
    }
    internal GpuInputSnapshot Constant(object identity) => constants[identity];
}

public sealed class GpuPassDeclarationContext
{
    private readonly GpuRenderGraph graph;
    private readonly string prefix;
    private bool closed;
    internal readonly Dictionary<GpuRenderGraphResource, GpuRenderGraphAccess> Uses = [];
    internal readonly List<IGpuInputUse> Inputs = [];
    internal readonly List<IGpuUploadData> Uploads = [];
    internal readonly Dictionary<object, GpuInputSnapshot> Constants = [];
    internal bool IsPreserved;

    internal GpuPassDeclarationContext(GpuRenderGraph graph, string name) { this.graph = graph; prefix = name + "/"; }

    public GpuRenderGraphBuffer CreateBuffer(string name, GpuGraphBufferDescription description)
    { CheckOpen(); return graph.CreateBuffer(prefix + name, description); }
    public GpuRenderGraphTexture CreateTexture(string name, GpuGraphTextureDescription description)
    { CheckOpen(); return graph.CreateTexture(prefix + name, description); }
    public void Read(GpuRenderGraphResource resource) => Use(resource, GpuRenderGraphAccess.Read);
    public void Write(GpuRenderGraphResource resource) => Use(resource, GpuRenderGraphAccess.Write);
    public void ReadWrite(GpuRenderGraphResource resource) => Use(resource, GpuRenderGraphAccess.ReadWrite);
    public void Preserve() { CheckOpen(); IsPreserved = true; }
    public void ReadUpload(IGpuUploadData data) { CheckOpen(); ArgumentNullException.ThrowIfNull(data); Uploads.Add(data); }

    public void ReadInput<T>(GpuGraphValue<T> value, IGpuGraphInputContract<T> inputContract)
    {
        CheckOpen();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(inputContract);
        if (value.Input is { } input)
        {
            graph.Require(input.GraphIdentity);
            if (!ReferenceEquals(input.Contract, inputContract))
            {
                throw new ArgumentException("Use the contract that created the input slot.", nameof(inputContract));
            }
        }
        else if (!Constants.ContainsKey(value))
        {
            Constants.Add(value, GpuInputSnapshot.Create(inputContract, inputContract.Snapshot(value.Value!)));
        }
        if (!Inputs.Any(input => ReferenceEquals(input.ValueIdentity, value)))
        {
            Inputs.Add(new GpuInputUse<T>(value));
        }
    }

    internal void Close() => closed = true;
    private void CheckOpen() => ObjectDisposedException.ThrowIf(closed, this);
    private void Use(GpuRenderGraphResource resource, GpuRenderGraphAccess access)
    {
        CheckOpen();
        ArgumentNullException.ThrowIfNull(resource);
        graph.Require(resource.GraphIdentity);
        if (Uses.TryGetValue(resource, out var previous) && previous != access)
        {
            access = GpuRenderGraphAccess.ReadWrite;
        }
        Uses[resource] = access;
    }
}

public interface IGpuGraphInputContract<T>
{
    T Snapshot(T value);
    void Retain(GpuRenderInputRetentionContext context, T snapshot);
}

public sealed class GpuRenderInputRetentionContext
{
    private readonly List<IGpuUploadData> retained;
    private readonly List<GpuRenderGraphResource> declared;
    private readonly List<GpuInputSnapshot> children;
    private bool closed;
    internal GpuRenderInputRetentionContext(List<IGpuUploadData> retained, List<GpuRenderGraphResource> declared, List<GpuInputSnapshot> children)
    { this.retained = retained; this.declared = declared; this.children = children; }
    public void ReadUpload(IGpuUploadData data)
    { ObjectDisposedException.ThrowIf(closed, this); ArgumentNullException.ThrowIfNull(data); retained.Add(data); }
    public void UseDeclared(GpuRenderGraphResource resource)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        ArgumentNullException.ThrowIfNull(resource);
        declared.Add(resource);
    }
    /// <summary>Retain an already immutable child snapshot. Its ownership set is shared across unchanged branches.</summary>
    public void ReadSnapshot<T>(T snapshot, IGpuGraphInputContract<T> inputContract)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        children.Add(GpuInputSnapshot.Create(inputContract, snapshot));
    }
    internal void Close() => closed = true;
}
