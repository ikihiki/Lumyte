namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraph
{
    private readonly List<GpuRenderGraphResource> resources = [];
    private readonly List<GpuRenderGraphPass> passes = [];
    private readonly List<GpuGraphInput> inputs = [];
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    private readonly HashSet<string> passNames = new(StringComparer.Ordinal);
    private readonly Dictionary<GpuRenderGraphResource, int> writers = [];
    private readonly List<HashSet<int>> dependencies = [];
    private readonly HashSet<int> roots = [];
    private readonly HashSet<GpuRenderGraphResource> outputs = [];
    private readonly HashSet<GpuRenderGraphResource> exports = [];
    private readonly Dictionary<GpuRenderGraphResource, int> outputVersions = [];
    private readonly List<List<GpuRenderGraphResource>> uninitialized = [];
    private bool declaring;

    public GpuRenderGraphTexture CreateTexture(string name, GpuGraphTextureDescription description)
        => AddResource(new GpuRenderGraphTexture(this, name, description));
    public GpuRenderGraphBuffer CreateBuffer(string name, GpuGraphBufferDescription description)
        => AddResource(new GpuRenderGraphBuffer(this, name, description));
    public GpuRenderGraphDependency CreateDependency(string name) => AddResource(new GpuRenderGraphDependency(this, name));
    public GpuRenderGraphTexture ImportTexture(string name, GpuGraphTextureRef reference)
    { ArgumentNullException.ThrowIfNull(reference); return AddResource(new GpuRenderGraphTexture(this, name, reference.Description, reference)); }
    public GpuRenderGraphBuffer ImportBuffer(string name, GpuGraphBufferRef reference)
    { ArgumentNullException.ThrowIfNull(reference); return AddResource(new GpuRenderGraphBuffer(this, name, reference.Description, reference)); }
    public GpuGraphTextureInput CreateTextureInput(string name, GpuGraphTextureDescription description)
        => new(AddResource(new GpuRenderGraphTexture(this, name, description, isInput: true)));
    public GpuGraphBufferInput CreateBufferInput(string name, GpuGraphBufferDescription description)
        => new(AddResource(new GpuRenderGraphBuffer(this, name, description, isInput: true)));
    public GpuGraphInput<T> CreateInput<T>(string name, IGpuGraphInputContract<T> contract)
        => AddInput(new GpuGraphInput<T>(this, name, contract, false, default));
    public GpuGraphInput<T> CreateInput<T>(string name, IGpuGraphInputContract<T> contract, T initialValue)
        => AddInput(new GpuGraphInput<T>(this, name, contract, true, initialValue));

    public TResult AddPass<TRequest, TResult>(string name, IGpuRenderPassContract<TRequest, TResult> contract, TRequest request)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (declaring) { throw new InvalidOperationException("A pass declaration cannot register another feature pass."); }
        if (!passNames.Add(name)) { throw new ArgumentException($"Pass '{name}' already exists.", nameof(name)); }
        var resourceCount = resources.Count;
        var inputCount = inputs.Count;
        var context = new GpuPassDeclarationContext(this, name);
        declaring = true;
        try
        {
            var snapshot = contract.Snapshot(request);
            var result = contract.Declare(context, snapshot);
            var pass = new GpuRenderGraphPass(name, new(contract.Id, contract.Version), typeof(TRequest), typeof(TResult), snapshot, result, context);
            var index = passes.Count;
            var reads = new HashSet<int>();
            var undefined = new List<GpuRenderGraphResource>();
            foreach (var use in pass.Uses)
            {
                if (use.Access != GpuRenderGraphAccess.Write)
                {
                    if (writers.TryGetValue(use.Resource, out var writer)) { reads.Add(writer); }
                    else if (use.Resource.ImportedReference is null && !use.Resource.IsInput) { undefined.Add(use.Resource); }
                }
            }
            foreach (var use in pass.Uses)
            {
                if (use.Access != GpuRenderGraphAccess.Read) { writers[use.Resource] = index; }
            }
            passes.Add(pass);
            dependencies.Add(reads);
            uninitialized.Add(undefined);
            if (pass.IsPreserved) { roots.Add(index); }
            return result;
        }
        catch
        {
            foreach (var resource in resources.Skip(resourceCount)) { names.Remove(resource.Name); }
            resources.RemoveRange(resourceCount, resources.Count - resourceCount);
            foreach (var input in inputs.Skip(inputCount)) { names.Remove(input.Name); }
            inputs.RemoveRange(inputCount, inputs.Count - inputCount);
            passNames.Remove(name);
            throw;
        }
        finally { context.Close(); declaring = false; }
    }

    public void MarkOutput(GpuRenderGraphResource resource)
    {
        if (declaring) { throw new InvalidOperationException("Use Preserve inside a pass declaration."); }
        Require(resource.Graph);
        if (writers.TryGetValue(resource, out var writer)) { roots.Add(writer); }
        else if (resource.ImportedReference is null && !resource.IsInput)
        { throw new InvalidOperationException($"Output '{resource.Name}' has no initialized content."); }
        outputs.Add(resource);
        outputVersions[resource] = writers.GetValueOrDefault(resource, -1);
    }
    public void ExportTexture(GpuRenderGraphTexture resource) => Export(resource);
    public void ExportBuffer(GpuRenderGraphBuffer resource) => Export(resource);
    private void Export(GpuRenderGraphResource resource)
    {
        if (resource.ImportedReference is not null || resource.IsInput)
        { throw new ArgumentException("Only graph-owned transient resources can be exported.", nameof(resource)); }
        MarkOutput(resource);
        exports.Add(resource);
    }

    public GpuRenderGraphPlan Compile()
    {
        if (declaring) { throw new InvalidOperationException("Cannot compile during pass declaration."); }
        var live = new HashSet<int>();
        var pending = new Stack<int>(roots);
        while (pending.TryPop(out var index))
        {
            if (!live.Add(index)) { continue; }
            foreach (var dependency in dependencies[index]) { pending.Push(dependency); }
        }
        foreach (var index in live)
        {
            if (uninitialized[index].Count != 0)
            { throw new InvalidOperationException($"Pass '{passes[index].Name}' reads uninitialized '{uninitialized[index][0].Name}'."); }
        }
        // All edges point to earlier content. Registration order also preserves WAR/WAW
        // between surviving uses without retaining otherwise dead writers or readers.
        var livePasses = passes.Where((_, index) => live.Contains(index)).ToArray();
        foreach (var output in outputVersions)
        {
            if (live.Any(index => index > output.Value && passes[index].Uses.Any(use =>
                ReferenceEquals(use.Resource, output.Key) && use.Access != GpuRenderGraphAccess.Read)))
            {
                throw new InvalidOperationException($"Output '{output.Key.Name}' is overwritten by a later live pass. Copy the earlier content to a separate output.");
            }
        }
        var liveResources = livePasses.SelectMany(pass => pass.Uses.Select(use => use.Resource)).Concat(outputs).ToHashSet();
        var liveInputs = livePasses.SelectMany(pass => pass.Inputs).Select(use => use.Input).OfType<GpuGraphInput>().ToHashSet();
        return new GpuRenderGraphPlan(livePasses, resources.Where(liveResources.Contains).ToArray(),
            inputs.Where(liveInputs.Contains).ToArray(), exports, outputs);
    }

    internal void Require(GpuRenderGraph graph)
    { if (!ReferenceEquals(this, graph)) { throw new ArgumentException("The declaration belongs to another graph."); } }
    private T AddResource<T>(T resource) where T : GpuRenderGraphResource
    { AddName(resource.Name); resources.Add(resource); return resource; }
    private GpuGraphInput<T> AddInput<T>(GpuGraphInput<T> input)
    { AddName(input.Name); inputs.Add(input); return input; }
    private void AddName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!names.Add(name)) { throw new ArgumentException($"Declaration '{name}' already exists.", nameof(name)); }
    }
}
