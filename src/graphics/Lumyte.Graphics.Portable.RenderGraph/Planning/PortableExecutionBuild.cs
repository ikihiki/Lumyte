using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources;

namespace Lumyte.Graphics.Portable.RenderGraph;

/// <summary>An internal operation and its explicit content dependencies.</summary>
public sealed class PortablePassBuilder
{
    private readonly PortableExecutionBuild build;
    internal PortablePassBuildContext Context { get; }
    internal Action<PortablePassRecordContext> Record { get; }
    internal Dictionary<PortablePassResource, GpuRenderGraphAccess> Uses { get; } = [];
    internal HashSet<PortablePassBuilder> Dependencies { get; } = [];
    internal bool Preserved { get; private set; }
    public string Name { get; }
    internal PortablePassBuilder(PortableExecutionBuild build, PortablePassBuildContext context, string name, Action<PortablePassRecordContext> record)
    { this.build = build; Context = context; Name = name; Record = record; }
    public PortablePassBuilder Preserve()
    {
        Context.Check();
        if (!Context.Feature.IsPreserved) { throw new InvalidOperationException("An internal side effect requires a preserved feature contract."); }
        Preserved = true; return this;
    }
    public PortablePassBuilder Read(PortablePassResource resource, PortablePassUsage usage) => Use(resource, usage, GpuRenderGraphAccess.Read);
    public PortablePassBuilder Write(PortablePassResource resource, PortablePassUsage usage) => Use(resource, usage, GpuRenderGraphAccess.Write);
    public PortablePassBuilder ReadWrite(PortablePassResource resource, PortablePassUsage usage) => Use(resource, usage, GpuRenderGraphAccess.ReadWrite);
    private PortablePassBuilder Use(PortablePassResource resource, PortablePassUsage usage, GpuRenderGraphAccess access)
    {
        Context.Check(); build.Check(resource);
        if (!Enum.IsDefined(usage)) { throw new ArgumentOutOfRangeException(nameof(usage)); }
        if (Uses.TryGetValue(resource, out GpuRenderGraphAccess previous) && previous != access) { access = GpuRenderGraphAccess.ReadWrite; }
        Uses[resource] = access;
        if (resource is PortablePassTexture texture)
        {
            GpuTextureUsage actual = usage switch
            {
                PortablePassUsage.SampledRead => GpuTextureUsage.Sampled,
                PortablePassUsage.StorageRead or PortablePassUsage.StorageWrite => GpuTextureUsage.Storage,
                PortablePassUsage.ColorAttachment => GpuTextureUsage.ColorAttachment,
                PortablePassUsage.DepthStencilAttachment => GpuTextureUsage.DepthStencilAttachment,
                PortablePassUsage.CopySource => GpuTextureUsage.CopySource,
                PortablePassUsage.CopyDestination => GpuTextureUsage.CopyDestination,
                _ => throw new ArgumentException("The usage describes a buffer, not a texture.", nameof(usage)),
            };
            if (texture.Reference is null) { texture.Description = texture.Description with { Usage = texture.Description.Usage | actual }; }
        }
        else if (resource is PortablePassBuffer buffer)
        {
            GpuBufferUsage actual = usage switch
            {
                PortablePassUsage.UniformRead => GpuBufferUsage.Uniform,
                PortablePassUsage.StorageRead or PortablePassUsage.StorageWrite => GpuBufferUsage.Storage,
                PortablePassUsage.CopySource => GpuBufferUsage.CopySource,
                PortablePassUsage.CopyDestination => GpuBufferUsage.CopyDestination,
                PortablePassUsage.IndexRead => GpuBufferUsage.Index,
                PortablePassUsage.IndirectRead => GpuBufferUsage.IndirectArguments,
                _ => throw new ArgumentException("The usage describes a texture, not a buffer.", nameof(usage)),
            };
            if (buffer.Reference is null) { buffer.Description = buffer.Description with { Usage = buffer.Description.Usage | actual }; }
        }
        return this;
    }
}

internal sealed class PortableExecutionBuild(PortableRenderRuntime runtime, PortablePassServices services,
    GpuRenderGraphPlan plan, GpuRenderGraphBindings bindings, GpuResourceBatch batch)
{
    internal PortableRenderRuntime Runtime { get; } = runtime;
    internal object Identity { get; } = new();
    internal PortablePassServices Services { get; } = services;
    internal GpuRenderGraphBindings Bindings { get; } = bindings;
    internal GpuResourceBatch Batch { get; } = batch;
    internal Dictionary<GpuRenderGraphResource, PortablePassResource> Resources { get; } = [];
    internal List<PortablePassResource> InternalResources { get; } = [];
    internal List<PortablePassView> Views { get; } = [];
    internal List<PortablePassBindings> BindingSets { get; } = [];
    internal List<PortablePassBuilder> Passes { get; } = [];
    internal List<PortableContentState> Generations { get; } = [];
    internal HashSet<PortableContentState> Dependencies { get; } = [];
    internal List<Task> ExternalResults { get; } = [];
    internal PortablePassBuilder[] LivePasses { get; private set; } = [];
    private Dictionary<PortablePassResource, int> resourceIndices = [];
    private PortableSchedule? schedule;
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    internal string Name(GpuRenderGraphPass feature, string name, string kind = "resource")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string qualified = feature.Name + "/" + name;
        if (!names.Add(kind + ":" + qualified)) { throw new ArgumentException($"Internal declaration '{qualified}' already exists.", nameof(name)); }
        return qualified;
    }
    internal void Check(PortablePassResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Owner, this)) { throw new ArgumentException("The resource belongs to another execution.", nameof(resource)); }
    }
    internal PortablePassTexture ImportTexture(GpuTextureRef reference)
    {
        Batch.Use(reference);
        PortablePassTexture? existing = Resources.Values.Concat(InternalResources).OfType<PortablePassTexture>().FirstOrDefault(item => ReferenceEquals(item.Reference, reference));
        if (existing is not null) { return existing; }
        var resource = new PortablePassTexture(this, "Imported texture", reference.Description, reference); InternalResources.Add(resource); return resource;
    }
    internal PortablePassBuffer ImportBuffer(GpuBufferRef reference)
    {
        Batch.Use(reference);
        PortablePassBuffer? existing = Resources.Values.Concat(InternalResources).OfType<PortablePassBuffer>().FirstOrDefault(item => ReferenceEquals(item.Reference, reference));
        if (existing is not null) { return existing; }
        var resource = new PortablePassBuffer(this, "Imported buffer", reference.Description, reference); InternalResources.Add(resource); return resource;
    }
    internal void Compile()
    {
        ValidateFeatures();
        var writers = new Dictionary<PortablePassResource, PortablePassBuilder>();
        var undefined = new Dictionary<PortablePassBuilder, PortablePassResource>();
        var roots = new HashSet<PortablePassBuilder>();
        foreach (PortablePassBuilder pass in Passes)
        {
            if (pass.Preserved || (pass.Context.Feature.IsPreserved && pass.Uses.Count == 0)
                || pass.Context.Feature.Uses.Any(use => use.Resource is GpuRenderGraphDependency && use.Access != GpuRenderGraphAccess.Read))
            { roots.Add(pass); }
            foreach (var use in pass.Uses)
            {
                if (use.Value == GpuRenderGraphAccess.Write) { continue; }
                if (writers.TryGetValue(use.Key, out PortablePassBuilder? writer)) { pass.Dependencies.Add(writer); }
                else if (!IsImported(use.Key)) { undefined.TryAdd(pass, use.Key); }
            }
            foreach (var use in pass.Uses)
            { if (use.Value != GpuRenderGraphAccess.Read) { writers[use.Key] = pass; } }
        }
        foreach (GpuRenderGraphResource output in plan.Outputs)
        { if (Resources.TryGetValue(output, out PortablePassResource? resource) && writers.TryGetValue(resource, out PortablePassBuilder? writer)) { roots.Add(writer); } }
        foreach (GpuRenderGraphPass feature in plan.Passes.Where(item => item.IsPreserved))
        {
            foreach (GpuRenderGraphUse use in feature.Uses.Where(item => item.Access != GpuRenderGraphAccess.Read))
            {
                if (Resources.TryGetValue(use.Resource, out PortablePassResource? resource))
                {
                    PortablePassBuilder? writer = Passes.LastOrDefault(item => ReferenceEquals(item.Context.Feature, feature) && item.Uses.TryGetValue(resource, out var access) && access != GpuRenderGraphAccess.Read);
                    if (writer is not null) { roots.Add(writer); }
                }
            }
        }
        resourceIndices = Resources.Values.Concat(InternalResources).Distinct().Select((resource, index) => (resource, index)).ToDictionary(item => item.resource, item => item.index);
        var passIndices = Passes.Select((pass, index) => (pass, index)).ToDictionary(item => item.pass, item => item.index);
        var key = new System.Text.StringBuilder();
        foreach (PortablePassBuilder pass in Passes)
        {
            key.Append(roots.Contains(pass) ? 'R' : '-');
            foreach (var use in pass.Uses.OrderBy(item => resourceIndices[item.Key])) { key.Append(resourceIndices[use.Key]).Append(':').Append((int)use.Value).Append(','); }
            key.Append('|');
            foreach (PortablePassBuilder dependency in pass.Dependencies.OrderBy(item => passIndices[item])) { key.Append(passIndices[dependency]).Append(','); }
            key.Append(';');
        }
        int[] exported = plan.Outputs.Where(Resources.ContainsKey).Select(output => resourceIndices[Resources[output]]).Order().ToArray();
        key.Append('E').AppendJoin(',', exported);
        schedule = Runtime.ScheduleCache.GetOrCreate(key.ToString(), () =>
        {
            var selected = new HashSet<PortablePassBuilder>(); var pending = new Stack<PortablePassBuilder>(roots);
            while (pending.TryPop(out PortablePassBuilder? pass))
            { if (selected.Add(pass)) { foreach (PortablePassBuilder dependency in pass.Dependencies) { pending.Push(dependency); } } }
            int[] indices = selected.Select(pass => passIndices[pass]).Order().ToArray();
            var lifetimes = new Dictionary<int, (int First, int Last)>();
            for (int position = 0; position < indices.Length; position++)
            {
                foreach (PortablePassResource resource in Passes[indices[position]].Uses.Keys)
                {
                    int index = resourceIndices[resource];
                    lifetimes[index] = (lifetimes.TryGetValue(index, out var previous) ? previous.First : position, position);
                }
            }
            foreach (int index in exported) { lifetimes[index] = (lifetimes.GetValueOrDefault(index).First, int.MaxValue); }
            return new(indices, lifetimes);
        });
        var live = schedule.LivePasses.Select(index => Passes[index]).ToHashSet();
        foreach (PortablePassBuilder pass in live)
        { if (undefined.TryGetValue(pass, out PortablePassResource? resource)) { throw new InvalidOperationException($"Pass '{pass.Name}' reads uninitialized '{resource.Name}'."); } }
        LivePasses = Passes.Where(live.Contains).ToArray();
        foreach (PortableContentState generation in Generations)
        { if (!generation.IsIndependent && generation.Writers.Any(writer => !live.Contains(writer))) { generation.Invalidate(); } }
    }
    private void ValidateFeatures()
    {
        foreach (GpuRenderGraphPass feature in plan.Passes)
        {
            PortablePassBuilder[] nodes = Passes.Where(pass => ReferenceEquals(pass.Context.Feature, feature)).ToArray();
            foreach (var logical in Resources)
            {
                GpuRenderGraphUse? declaration = feature.Uses.Where(use => ReferenceEquals(use.Resource, logical.Key)).Cast<GpuRenderGraphUse?>().SingleOrDefault();
                bool written = false;
                foreach (PortablePassBuilder node in nodes)
                {
                    if (!node.Uses.TryGetValue(logical.Value, out GpuRenderGraphAccess access)) { continue; }
                    if (declaration is null || (declaration.Value.Access == GpuRenderGraphAccess.Read && access != GpuRenderGraphAccess.Read)
                        || (declaration.Value.Access == GpuRenderGraphAccess.Write && !written && access != GpuRenderGraphAccess.Write))
                    { throw new InvalidOperationException($"Internal pass '{node.Name}' exceeds the feature declaration for '{logical.Key.Name}'."); }
                    if (access != GpuRenderGraphAccess.Read) { written = true; }
                }
                if (declaration is { Access: not GpuRenderGraphAccess.Read } && !written)
                { throw new InvalidOperationException($"Feature '{feature.Name}' does not initialize its declared output '{logical.Key.Name}'."); }
            }
        }
    }
    internal void CheckDependencies()
    {
        if (ExternalResults.Any(result => result.IsFaulted || result.IsCanceled))
        { throw new InvalidOperationException("An external GPU content dependency failed."); }
        foreach (PortableContentState dependency in Dependencies)
        { if (dependency.Failed) { throw new InvalidOperationException("A GPU content dependency failed.", dependency.Result?.Exception); } }
    }
    internal Task Accept(GpuSubmissionToken token)
    {
        Task result = Task.WhenAll(Dependencies.Select(item => item.Result!).Concat(ExternalResults).Append(token.WaitAsync().AsTask()));
        foreach (PortableContentState generation in Generations)
        { if (!generation.IsIndependent && !generation.Failed) { generation.Accept(result, prerequisites: Dependencies.ToArray()); } }
        return result;
    }
    internal void Abort()
    { foreach (PortableContentState generation in Generations) { if (!generation.IsIndependent && generation.Result is null) { generation.Invalidate(); } } }
    private static bool IsImported(PortablePassResource resource) => resource switch
    { PortablePassTexture texture => texture.Reference is not null, PortablePassBuffer buffer => buffer.Reference is not null, _ => false };
    internal void Prepare(GpuResourceScope scope)
    {
        var used = LivePasses.SelectMany(pass => pass.Uses.Keys).Concat(plan.Exports.Select(export => Resources[export])).ToHashSet();
        var intervals = used.ToDictionary(resource => resource, resource => schedule!.Lifetimes[resourceIndices[resource]]);
        var allocated = new List<(PortablePassResource Resource, int Last)>();
        foreach (PortablePassResource resource in used.OrderBy(item => intervals[item].First))
        {
            if (!IsImported(resource))
            {
                int reusable = allocated.FindIndex(item => item.Last < intervals[resource].First && SameDescription(item.Resource, resource));
                PortablePassResource? previous = reusable >= 0 ? allocated[reusable].Resource : null;
                switch (resource)
                {
                    case PortablePassTexture texture: texture.Reference = (previous as PortablePassTexture)?.Reference ?? scope.CreateTexture(texture.Description); break;
                    case PortablePassBuffer buffer: buffer.Reference = (previous as PortablePassBuffer)?.Reference ?? scope.CreateBuffer(buffer.Description); break;
                }
                if (reusable >= 0) { allocated[reusable] = (resource, intervals[resource].Last); }
                else { allocated.Add((resource, intervals[resource].Last)); }
            }
            switch (resource)
            { case PortablePassTexture texture: Batch.Use(texture.Reference!); break; case PortablePassBuffer buffer: Batch.Use(buffer.Reference!); break; }
        }
        foreach (PortablePassView view in Views.Where(view => used.Contains(view.Texture))) { view.Reference = scope.GetView(view.Texture.Reference!, view.Description); }
        foreach (PortablePassBindings binding in BindingSets.Where(binding => binding.Resources.All(used.Contains)))
        { binding.Reference = scope.GetBindings(binding.Program, binding.Group, new PreparedInputs(binding.Entries)); }
        Batch.Use(scope);
    }
    private static bool SameDescription(PortablePassResource first, PortablePassResource second) => (first, second) switch
    {
        (PortablePassTexture a, PortablePassTexture b) => a.Description == b.Description,
        (PortablePassBuffer a, PortablePassBuffer b) => a.Description == b.Description,
        _ => false,
    };
    private sealed class PreparedInputs(IReadOnlyList<Action<GpuBindingWriter>> entries) : IGpuBindingInputs
    { public void Write(GpuBindingWriter writer) { foreach (Action<GpuBindingWriter> entry in entries) { entry(writer); } } }
}
