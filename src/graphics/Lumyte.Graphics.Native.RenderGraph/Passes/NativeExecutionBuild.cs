using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph;

internal sealed class NativeExecutionBuild(NativePassServices services, GpuRenderGraphBindings bindings,
    GpuRenderGraphPlan plan, NativeContentStore content, NativeScheduleCache schedules)
{
    internal NativePassServices Services { get; } = services;
    internal GpuRenderGraphBindings Bindings { get; } = bindings;
    internal NativeContentStore Content { get; } = content;
    internal Dictionary<GpuRenderGraphResource, NativePassResource> Resources { get; } = [];
    internal Dictionary<GpuResourceRef, NativePassResource> Imported { get; } = [];
    internal List<NativePassResource> PrivateResources { get; } = [];
    internal List<NativePassView> Views { get; } = [];
    internal List<NativePassBuilder> Passes { get; } = [];
    internal List<NativePassBuildContext> Features { get; } = [];
    internal List<IDisposable> Leases { get; } = [];
    internal List<NativeContentEntry> Registered { get; } = [];
    internal HashSet<NativeContentEntry> UsedContent { get; } = [];
    internal List<Task> Predecessors { get; } = [];
    internal List<(NativePassBuildContext Feature, int Start, NativePassBuilder[] Writers)> ContentDependencies { get; } = [];
    private NativePassBuilder[] live = [];
    private readonly Dictionary<NativePassResource, (int First, int Last)> lifetimes = [];
    internal IEnumerable<NativePassResource> AllResources => lifetimes.Keys;
    internal Task[] ResultDependencies => Predecessors.Concat(UsedContent.Where(entry => !ReferenceEquals(entry.Origin, this)).Select(entry => entry.Result)).Distinct().ToArray();

    internal void Compile()
    {
        NativePassResource[] resourceTable = Resources.Values.Concat(PrivateResources).ToArray();
        var resourceIndices = resourceTable.Select((resource, index) => (resource, index)).ToDictionary(pair => pair.resource, pair => pair.index);
        string key = ScheduleKey(resourceIndices);
        HashSet<NativePassBuilder> selected;
        bool cached = schedules.TryGet(key, out NativeSchedule? schedule);
        if (cached)
        {
            live = schedule!.Passes.Select(index => Passes[index]).ToArray(); selected = live.ToHashSet();
        }
        else
        {
            var writers = new Dictionary<NativePassResource, NativePassBuilder>();
            var undefined = new Dictionary<NativePassBuilder, NativePassResource>();
            var roots = new HashSet<NativePassBuilder>();
            foreach ((NativePassBuildContext feature, int start, NativePassBuilder[] predecessors) in ContentDependencies)
            {
                foreach (NativePassBuilder reader in feature.Passes.Skip(start))
                { foreach (NativePassBuilder writer in predecessors) { if (!ReferenceEquals(writer, reader)) { reader.Dependencies.Add(writer); } } }
            }
            foreach (NativePassBuilder pass in Passes)
            {
                foreach ((NativePassResource resource, GpuRenderGraphAccess access) in pass.Accesses)
                {
                    if (access != GpuRenderGraphAccess.Write)
                    {
                        if (writers.TryGetValue(resource, out NativePassBuilder? writer)) { pass.Dependencies.Add(writer); }
                        else if (!resource.IsImported) { undefined.TryAdd(pass, resource); }
                    }
                    if (access != GpuRenderGraphAccess.Read) { writers[resource] = pass; }
                }
                if (pass.IsPreserved || pass.Feature.Declaration.IsPreserved && pass.Uses.Count == 0) { roots.Add(pass); }
            }
            foreach (GpuRenderGraphResource output in plan.Outputs)
            { if (Resources.TryGetValue(output, out NativePassResource? resource) && writers.TryGetValue(resource, out NativePassBuilder? writer)) { roots.Add(writer); } }
            foreach (NativePassBuildContext feature in Features)
            {
                bool opaqueEffect = feature.Declaration.Uses.Any(use => use.Resource is GpuRenderGraphDependency && use.Access != GpuRenderGraphAccess.Read);
                if (opaqueEffect) { foreach (NativePassBuilder pass in feature.Passes) { roots.Add(pass); } }
                if (feature.Declaration.IsPreserved)
                {
                    foreach (GpuRenderGraphUse use in feature.Declaration.Uses.Where(use => use.Access != GpuRenderGraphAccess.Read))
                    {
                        if (!Resources.TryGetValue(use.Resource, out NativePassResource? resource)) { continue; }
                        NativePassBuilder? writer = feature.Passes.LastOrDefault(pass => pass.Accesses.TryGetValue(resource, out var access) && access != GpuRenderGraphAccess.Read);
                        if (writer is not null) { roots.Add(writer); }
                    }
                }
            }
            selected = [];
            var pending = new Stack<NativePassBuilder>(roots);
            while (pending.TryPop(out NativePassBuilder? pass))
            { if (selected.Add(pass)) { foreach (NativePassBuilder predecessor in pass.Dependencies) { pending.Push(predecessor); } } }
            live = Passes.Where(selected.Contains).ToArray();
            foreach (NativePassBuilder pass in live)
            {
                if (undefined.TryGetValue(pass, out NativePassResource? resource))
                { throw new InvalidOperationException($"Native pass '{pass.Name}' reads uninitialized '{resource.Name}'."); }
            }
            }
        ValidateDeclarations();
        foreach (NativeContentEntry entry in Registered)
        {
            if (entry.Writers.Any(writer => !selected.Contains(writer)))
            { entry.Invalidate(new InvalidOperationException("A required content writer was culled.")); }
        }
        if (cached)
        {
            foreach (var lifetime in schedule!.Lifetimes)
            { lifetimes.Add(resourceTable[lifetime.Resource], (lifetime.First, lifetime.Last)); }
        }
        for (int index = 0; index < live.Length; index++)
        {
            foreach ((NativePassResource resource, NativePassUsage usage) in live[index].Uses)
            {
                if (!cached) { lifetimes[resource] = lifetimes.TryGetValue(resource, out var lifetime) ? (lifetime.First, index) : (index, index); }
                if (resource is NativePassTexture texture && !texture.IsImported)
                { texture.Description = texture.Description with { Usage = texture.Description.Usage | TextureUsage(usage.Access) }; }
            }
        }
        if (!cached)
        {
            schedules.Add(key, new(live.Select(pass => Passes.IndexOf(pass)).ToArray(),
                lifetimes.Select(pair => (resourceIndices[pair.Key], pair.Value.First, pair.Value.Last)).ToArray()));
        }
        foreach (GpuRenderGraphResource export in plan.Exports)
        {
            NativePassResource resource = Resources[export];
            if (!lifetimes.ContainsKey(resource)) { throw new InvalidOperationException($"Native output '{export.Name}' was not initialized."); }
        }
    }
    private string ScheduleKey(Dictionary<NativePassResource, int> resources)
    {
        var key = new System.Text.StringBuilder();
        foreach (var resource in resources.Keys) { key.Append(resource.IsImported ? 'i' : 't'); }
        key.Append('|');
        foreach (NativePassBuildContext feature in Features)
        {
            key.Append(feature.Declaration.IsPreserved ? 'p' : '-');
            foreach (GpuRenderGraphUse use in feature.Declaration.Uses)
            {
                key.Append(Resources.TryGetValue(use.Resource, out var resource) ? resources[resource] : -1)
                    .Append(':').Append((int)use.Access).Append(',');
            }
            key.Append(';');
        }
        key.Append('|');
        foreach (NativePassBuilder pass in Passes)
        {
            key.Append(Features.IndexOf(pass.Feature)).Append(pass.IsPreserved ? 'p' : '-');
            foreach (var use in pass.Accesses) { key.Append(resources[use.Key]).Append(':').Append((int)use.Value).Append(','); }
            key.Append(';');
        }
        key.Append('|');
        foreach (GpuRenderGraphResource output in plan.Outputs)
        { if (Resources.TryGetValue(output, out var resource)) { key.Append(resources[resource]).Append(','); } }
        key.Append('|');
        foreach (var dependency in ContentDependencies)
        {
            key.Append(Features.IndexOf(dependency.Feature)).Append(':').Append(dependency.Start).Append(':');
            foreach (NativePassBuilder writer in dependency.Writers) { key.Append(Passes.IndexOf(writer)).Append(','); }
            key.Append(';');
        }
        return key.ToString();
    }
    private void ValidateDeclarations()
    {
        foreach (NativePassBuildContext feature in Features)
        {
            foreach (GpuRenderGraphUse declared in feature.Declaration.Uses)
            {
                if (!Resources.TryGetValue(declared.Resource, out NativePassResource? resource)) { continue; }
                GpuRenderGraphAccess[] actual = feature.Passes.Where(pass => pass.Accesses.ContainsKey(resource))
                    .Select(pass => pass.Accesses[resource]).ToArray();
                if (declared.Access == GpuRenderGraphAccess.Read && actual.Any(access => access != GpuRenderGraphAccess.Read))
                { throw new InvalidOperationException($"Native feature '{feature.Declaration.Name}' writes read-only '{resource.Name}'."); }
                if (declared.Access == GpuRenderGraphAccess.Write && actual.Length != 0 && actual[0] != GpuRenderGraphAccess.Write)
                { throw new InvalidOperationException($"Native feature '{feature.Declaration.Name}' reads the preceding content of write-only '{resource.Name}'."); }
                if (declared.Access != GpuRenderGraphAccess.Read && !actual.Any(access => access != GpuRenderGraphAccess.Read))
                { throw new InvalidOperationException($"Native feature '{feature.Declaration.Name}' did not implement declared output '{resource.Name}'."); }
            }
        }
    }
    internal void Allocate(GpuResourceScope scope)
    {
        var slots = new List<(NativePassResource Resource, int Last)>();
        HashSet<NativePassResource> exported = plan.Outputs.Where(Resources.ContainsKey).Select(resource => Resources[resource]).ToHashSet();
        foreach (NativePassResource resource in lifetimes.Keys.OrderBy(resource => lifetimes[resource].First))
        {
            if (resource.IsImported) { continue; }
            var lifetime = lifetimes[resource];
            int reusable = exported.Contains(resource) ? -1 : slots.FindIndex(slot => slot.Last < lifetime.First && Compatible(slot.Resource, resource));
            if (reusable >= 0)
            {
                NativePassResource previous = slots[reusable].Resource;
                if (resource is NativePassTexture texture) { texture.Value = (GpuTextureRef)previous.Reference; }
                else { ((NativePassBuffer)resource).Value = (GpuBufferRef)previous.Reference; }
                slots[reusable] = (resource, lifetime.Last);
            }
            else
            {
                if (resource is NativePassTexture texture) { texture.Value = scope.CreateTexture(texture.Description); }
                else { var buffer = (NativePassBuffer)resource; buffer.Value = scope.CreateBuffer(buffer.Description); }
                if (!exported.Contains(resource)) { slots.Add((resource, lifetime.Last)); }
            }
        }
        foreach (NativePassView view in Views.Where(view => lifetimes.ContainsKey(view.Texture)))
        { view.Value = scope.GetView((GpuTextureRef)view.Texture.Reference, view.Description); }
    }
    private static bool Compatible(NativePassResource first, NativePassResource second) => (first, second) switch
    {
        (NativePassBuffer left, NativePassBuffer right) => left.Description == right.Description,
        (NativePassTexture left, NativePassTexture right) => left.Description == right.Description,
        _ => false,
    };
    internal void CheckDependencies()
    {
        foreach (NativeContentEntry entry in UsedContent) { entry.CheckFailure(); }
        foreach (Task result in ResultDependencies) { if (result.IsCompleted) { result.GetAwaiter().GetResult(); } }
    }
    internal void Accept(GpuSubmissionToken token)
    {
        Task[] dependencies = ResultDependencies;
        foreach (NativeContentEntry entry in Registered)
        {
            if (entry.Writers.All(live.Contains)) { entry.Accept(token, dependencies); }
        }
    }
    internal void Reject(Exception error)
    { foreach (NativeContentEntry entry in Registered) { if (!entry.Accepted) { entry.Invalidate(error); } } }
    internal void Record(NativeGpuCommandBuffer commands)
    {
        bool explicitLayouts = Services.Backend.Capabilities.ExplicitTextureTransitions;
        var textures = new Dictionary<GpuTextureRef, (NativeGpuTextureDescription Description, GpuTextureLayout Layout)>();
        NativePassUsage previous = new(GpuStage.All, GpuAccess.ShaderWrite | GpuAccess.CopyWrite);
        foreach (NativePassBuilder pass in live)
        {
            CheckDependencies();
            NativePassUsage next = pass.Uses.Where(use => !explicitLayouts || use.Key is NativePassBuffer)
                .Select(use => use.Value).Aggregate(new NativePassUsage(), static (value, use) => new(value.Stages | use.Stages, value.Access | use.Access));
            if (next.Stages != 0) { commands.Barrier(previous.Stages, previous.Access, next.Stages, next.Access); }
            foreach ((NativePassResource resource, NativePassUsage usage) in pass.Uses)
            {
                if (resource is not NativePassTexture texture) { continue; }
                GpuTextureLayout layout = explicitLayouts ? TextureLayout(usage.Access) : GpuTextureLayout.General;
                var reference = (GpuTextureRef)texture.Reference;
                NativeGpuTextureView view = WholeView(Services.Resources.GetTextureHandle(reference), texture.Description);
                if (textures.TryGetValue(reference, out var state))
                { if (explicitLayouts) { commands.TextureTransition(view, state.Layout, layout); } }
                else if (!texture.IsImported) { commands.DiscardTexture(view, layout); }
                else if (explicitLayouts) { commands.TextureTransition(view, GpuTextureLayout.General, layout); }
                textures[reference] = (texture.Description, layout);
            }
            var context = new NativePassRecordContext(commands, Services.Resources, pass);
            try { pass.Record(context); } finally { context.Close(); }
            if (next.Stages != 0) { previous = next; }
        }
        foreach ((GpuTextureRef reference, var state) in textures)
        {
            if (state.Layout != GpuTextureLayout.General)
            { commands.TextureTransition(WholeView(Services.Resources.GetTextureHandle(reference), state.Description), state.Layout, GpuTextureLayout.General); }
        }
        if (previous.Stages != 0) { commands.Barrier(previous.Stages, previous.Access, GpuStage.All, GpuAccess.ShaderRead | GpuAccess.CopyRead); }
    }
    internal static NativeGpuTextureView WholeView(NativeGpuTextureHandle texture, NativeGpuTextureDescription description)
        => new(texture, description.Dimension switch
        {
            NativeGpuTextureDimension.OneD => NativeGpuTextureViewDimension.OneD,
            NativeGpuTextureDimension.ThreeD => NativeGpuTextureViewDimension.ThreeD,
            _ => description.LayerCount == 1 ? NativeGpuTextureViewDimension.TwoD : NativeGpuTextureViewDimension.TwoDArray,
        }, description.Format, Aspect(description.Format), 0, description.MipCount, 0, description.LayerCount);
    internal static NativeGpuTextureAspect Aspect(GpuFormat format) => format switch
    {
        GpuFormat.D32Float => NativeGpuTextureAspect.Depth,
        GpuFormat.Depth24PlusStencil8 => NativeGpuTextureAspect.Depth | NativeGpuTextureAspect.Stencil,
        _ => NativeGpuTextureAspect.Color,
    };
    private static NativeGpuTextureUsage TextureUsage(GpuAccess access)
    {
        NativeGpuTextureUsage usage = 0;
        if ((access & GpuAccess.CopyRead) != 0) { usage |= NativeGpuTextureUsage.CopySource; }
        if ((access & GpuAccess.CopyWrite) != 0) { usage |= NativeGpuTextureUsage.CopyDestination; }
        if ((access & GpuAccess.ShaderRead) != 0) { usage |= NativeGpuTextureUsage.Sampled; }
        if ((access & GpuAccess.ShaderWrite) != 0) { usage |= NativeGpuTextureUsage.Storage; }
        if ((access & (GpuAccess.ColorRead | GpuAccess.ColorWrite)) != 0) { usage |= NativeGpuTextureUsage.ColorAttachment; }
        if ((access & (GpuAccess.DepthStencilRead | GpuAccess.DepthStencilWrite)) != 0) { usage |= NativeGpuTextureUsage.DepthStencilAttachment; }
        return usage;
    }
    private static GpuTextureLayout TextureLayout(GpuAccess access)
    {
        if ((access & GpuAccess.ColorWrite) != 0) { return GpuTextureLayout.ColorAttachment; }
        if ((access & (GpuAccess.DepthStencilRead | GpuAccess.DepthStencilWrite)) != 0) { return GpuTextureLayout.DepthStencilWrite; }
        if ((access & GpuAccess.CopyWrite) != 0) { return GpuTextureLayout.CopyDestination; }
        if ((access & GpuAccess.CopyRead) != 0) { return GpuTextureLayout.CopySource; }
        if ((access & GpuAccess.ShaderWrite) != 0) { return GpuTextureLayout.General; }
        return GpuTextureLayout.ShaderRead;
    }
}
