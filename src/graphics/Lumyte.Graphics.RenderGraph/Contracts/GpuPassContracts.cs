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
    private readonly IReadOnlyDictionary<object, object?> constants;
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
        constants = new System.Collections.ObjectModel.ReadOnlyDictionary<object, object?>(new Dictionary<object, object?>(context.Constants));
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
        return value.Input is not null ? bindings.GetInput(value) : (T)constants[value]!;
    }
}

public sealed class GpuPassDeclarationContext
{
    private readonly GpuRenderGraph graph;
    private readonly string prefix;
    private bool closed;
    internal readonly Dictionary<GpuRenderGraphResource, GpuRenderGraphAccess> Uses = [];
    internal readonly List<IGpuInputUse> Inputs = [];
    internal readonly List<IGpuUploadData> Uploads = [];
    internal readonly Dictionary<object, object?> Constants = [];
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
            graph.Require(input.Graph);
            if (!ReferenceEquals(input.Contract, inputContract))
            {
                throw new ArgumentException("Use the contract that created the input slot.", nameof(inputContract));
            }
        }
        else if (!Constants.ContainsKey(value))
        {
            Constants.Add(value, inputContract.Snapshot(value.Value!));
        }
        if (!Inputs.Any(input => ReferenceEquals(input.ValueIdentity, value)))
        {
            Inputs.Add(new GpuInputUse<T>(value, inputContract));
        }
    }

    internal void Close() => closed = true;
    private void CheckOpen() => ObjectDisposedException.ThrowIf(closed, this);
    private void Use(GpuRenderGraphResource resource, GpuRenderGraphAccess access)
    {
        CheckOpen();
        ArgumentNullException.ThrowIfNull(resource);
        graph.Require(resource.Graph);
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
    private readonly GpuRenderGraphPass pass;
    private readonly List<IGpuUploadData> retained;
    internal GpuRenderInputRetentionContext(GpuRenderGraphPass pass, List<IGpuUploadData> retained)
    { this.pass = pass; this.retained = retained; }
    public void ReadUpload(IGpuUploadData data) { ArgumentNullException.ThrowIfNull(data); retained.Add(data); }
    public void UseDeclared(GpuRenderGraphResource resource)
    {
        if (!pass.Uses.Any(use => ReferenceEquals(use.Resource, resource) && use.Access != GpuRenderGraphAccess.Write))
        {
            throw new InvalidOperationException($"Input in pass '{pass.Name}' references an undeclared read: '{resource.Name}'.");
        }
    }
}
