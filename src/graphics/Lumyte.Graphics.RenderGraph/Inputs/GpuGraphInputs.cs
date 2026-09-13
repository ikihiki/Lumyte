using System.Collections.ObjectModel;

namespace Lumyte.Graphics.RenderGraph;

public abstract class GpuGraphInput
{
    private protected GpuGraphInput(GpuRenderGraph graph, string name, bool hasInitial, object? initial)
    { Graph = graph; Name = name; HasInitial = hasInitial; Initial = initial; }
    internal GpuRenderGraph Graph { get; }
    internal bool HasInitial { get; }
    internal object? Initial { get; }
    public string Name { get; }
}

public sealed class GpuGraphInput<T> : GpuGraphInput
{
    internal GpuGraphInput(GpuRenderGraph graph, string name, IGpuGraphInputContract<T> contract, bool hasInitial, T? initial)
        : base(graph, name, hasInitial, hasInitial ? contract.Snapshot(initial!) : null) => Contract = contract;
    internal IGpuGraphInputContract<T> Contract { get; }
}

public sealed class GpuGraphValue<T>
{
    private GpuGraphValue(T? value, GpuGraphInput<T>? input) { Value = value; Input = input; }
    internal T? Value { get; }
    internal GpuGraphInput<T>? Input { get; }
    public static GpuGraphValue<T> Constant(T value) => new(value, null);
    public static GpuGraphValue<T> FromInput(GpuGraphInput<T> input) => new(default, input ?? throw new ArgumentNullException(nameof(input)));
    public static implicit operator GpuGraphValue<T>(T value) => Constant(value);
    public static implicit operator GpuGraphValue<T>(GpuGraphInput<T> input) => FromInput(input);
}

internal interface IGpuInputUse
{
    object ValueIdentity { get; }
    GpuGraphInput? Input { get; }
    void Retain(GpuRenderGraphPass pass, GpuRenderGraphBindings bindings, List<IGpuUploadData> data);
}

internal sealed class GpuInputUse<T>(GpuGraphValue<T> value, IGpuGraphInputContract<T> contract) : IGpuInputUse
{
    public object ValueIdentity => value;
    public GpuGraphInput? Input => value.Input;
    public void Retain(GpuRenderGraphPass pass, GpuRenderGraphBindings bindings, List<IGpuUploadData> data)
        => contract.Retain(new GpuRenderInputRetentionContext(pass, data), pass.GetInput(value, bindings));
}

public sealed class GpuRenderGraphBindingsBuilder
{
    private readonly GpuRenderGraphPlan plan;
    private readonly Dictionary<GpuGraphInput, object?> inputs = [];
    private readonly Dictionary<GpuRenderGraphResource, GpuGraphResourceRef> resources = [];
    internal GpuRenderGraphBindingsBuilder(GpuRenderGraphPlan plan)
    {
        this.plan = plan;
        foreach (var input in plan.Inputs)
        {
            if (input.HasInitial) { inputs.Add(input, input.Initial); }
        }
    }

    internal GpuRenderGraphBindingsBuilder(GpuRenderGraphBindings bindings)
    {
        plan = bindings.Plan;
        inputs = new(bindings.InputValues);
        resources = new(bindings.ResourceValues);
    }

    public void Set<T>(GpuGraphInput<T> input, T value)
    {
        if (!plan.Inputs.Contains(input)) { throw new ArgumentException("Input does not belong to this plan.", nameof(input)); }
        inputs[input] = input.Contract.Snapshot(value);
    }
    public void Set(GpuGraphTextureInput input, GpuGraphTextureRef reference) => SetResource(input.Texture, reference);
    public void Set(GpuGraphBufferInput input, GpuGraphBufferRef reference) => SetResource(input.Buffer, reference);
    private void SetResource(GpuRenderGraphResource input, GpuGraphResourceRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!plan.Resources.Contains(input)) { throw new ArgumentException("Resource input does not belong to this plan.", nameof(input)); }
        resources[input] = reference;
    }
    public GpuRenderGraphBindings Build() => new(plan, plan.NextBindingsGeneration(), inputs, resources);
}

public sealed class GpuRenderGraphBindings
{
    internal IReadOnlyDictionary<GpuGraphInput, object?> InputValues { get; }
    internal IReadOnlyDictionary<GpuRenderGraphResource, GpuGraphResourceRef> ResourceValues { get; }
    // Own the explicit CPU data set independently of mutable builder dictionaries.
    private readonly List<IGpuUploadData> retained = [];
    internal GpuRenderGraphBindings(GpuRenderGraphPlan plan, long generation,
        Dictionary<GpuGraphInput, object?> inputs, Dictionary<GpuRenderGraphResource, GpuGraphResourceRef> resources)
    {
        Plan = plan;
        Generation = generation;
        InputValues = new ReadOnlyDictionary<GpuGraphInput, object?>(new Dictionary<GpuGraphInput, object?>(inputs));
        ResourceValues = new ReadOnlyDictionary<GpuRenderGraphResource, GpuGraphResourceRef>(new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>(resources));
        foreach (var pass in plan.Passes)
        {
            retained.AddRange(pass.Uploads);
            foreach (var input in pass.Inputs)
            {
                if (input.Input is null || InputValues.ContainsKey(input.Input)) { input.Retain(pass, this, retained); }
            }
        }
    }
    public GpuRenderGraphPlan Plan { get; }
    public long Generation { get; }
    public GpuRenderGraphBindingsBuilder ToBuilder() => new(this);
    public T GetInput<T>(GpuGraphValue<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Input is not { } input)
        {
            throw new ArgumentException("Resolve constants through the pass declaration's GetInput method.", nameof(value));
        }
        if (!InputValues.TryGetValue(input, out var result)) { throw new InvalidOperationException($"Input '{input.Name}' has no value."); }
        return (T)result!;
    }
    public GpuGraphResourceRef? ResolveResource(GpuRenderGraphResource resource)
    {
        if (!Plan.Resources.Contains(resource)) { throw new ArgumentException("Resource does not belong to the live plan.", nameof(resource)); }
        return resource.IsInput
            ? ResourceValues.GetValueOrDefault(resource) ?? throw new InvalidOperationException($"Resource input '{resource.Name}' has no value.")
            : resource.ImportedReference;
    }
}
