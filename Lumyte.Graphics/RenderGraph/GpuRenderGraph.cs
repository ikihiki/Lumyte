namespace Lumyte.Graphics.RenderGraph;

public sealed partial class GpuRenderGraph
{
    private static int s_nextResourceId;
    private const GpuStage AllStages = GpuStage.DrawIndirect | GpuStage.VertexShader | GpuStage.PixelShader
        | GpuStage.ComputeShader | GpuStage.ColorOutput | GpuStage.DepthStencil | GpuStage.Copy
        | GpuStage.AllGraphics | GpuStage.All;
    private const GpuBarrierHazards AllHazards = GpuBarrierHazards.Descriptors
        | GpuBarrierHazards.IndirectArguments | GpuBarrierHazards.DepthCaches;
    private readonly List<GpuRenderGraphResourceDeclaration> resources = [];
    private readonly List<GpuRenderGraphPassDeclaration> passes = [];
    private readonly Dictionary<string, GpuRenderGraphResource> resourcesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<(GpuRenderGraphResourceKind Kind, ulong Value), GpuRenderGraphResource> importedResources = [];
    private readonly HashSet<GpuRenderGraphResource> outputs = [];
    private readonly HashSet<string> passNames = new(StringComparer.Ordinal);

    public GpuRenderGraphResource ImportTexture(string name, GpuTextureHandle texture)
        => ImportTexture(name, texture, null);

    public GpuRenderGraphResource ImportTexture(
        string name,
        GpuTextureHandle texture,
        GpuTextureDescription description)
        => ImportTexture(name, texture, (GpuTextureDescription?)description);

    public GpuRenderGraphResource ImportTexture(
        string name,
        GpuRenderGraphExportedTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        texture.RequireAlive();
        return Import(
            name,
            GpuRenderGraphResourceKind.Texture,
            texture.Texture.Value,
            texture.Texture,
            default,
            texture.Description,
            null,
            GpuMemoryKind.DeviceLocal,
            false,
            texture,
            null);
    }

    public GpuRenderGraphResource ImportBuffer(string name, GpuBufferHandle buffer)
        => ImportBuffer(name, buffer, null);

    public GpuRenderGraphResource ImportBuffer(
        string name,
        GpuBufferHandle buffer,
        GpuBufferDescription description)
        => ImportBuffer(name, buffer, (GpuBufferDescription?)description);

    public GpuRenderGraphResource ImportBuffer(
        string name,
        GpuRenderGraphExportedBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.RequireAlive();
        return Import(
            name,
            GpuRenderGraphResourceKind.Buffer,
            buffer.Buffer.Value,
            default,
            buffer.Buffer,
            null,
            buffer.Description,
            GpuMemoryKind.DeviceLocal,
            false,
            null,
            buffer);
    }

    public GpuRenderGraphResource CreateTexture(
        string name,
        GpuTextureDescription description)
    {
        description.Validate();
        return Import(
            name,
            GpuRenderGraphResourceKind.Texture,
            0,
            default,
            default,
            description,
            null,
            GpuMemoryKind.DeviceLocal,
            true,
            null,
            null);
    }

    public GpuRenderGraphResource CreateBuffer(
        string name,
        GpuBufferDescription description,
        GpuMemoryKind memoryKind = GpuMemoryKind.DeviceLocal)
    {
        description.Validate();
        if (!Enum.IsDefined(memoryKind))
        {
            throw new ArgumentOutOfRangeException(nameof(memoryKind));
        }
        return Import(
            name,
            GpuRenderGraphResourceKind.Buffer,
            0,
            default,
            default,
            null,
            description,
            memoryKind,
            true,
            null,
            null);
    }

    /// <summary>
    /// Adds a pass whose static callback receives explicit state and a stack-only context.
    /// This avoids closure and per-record context allocations.
    /// </summary>
    public GpuRenderGraphPassBuilder AddPass<TState>(
        string name,
        TState state,
        GpuRenderGraphPassAction<TState> record,
        GpuRenderGraphPassFlags flags = GpuRenderGraphPassFlags.None)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidatePass(name, flags);
        return AddPass(new GpuRenderGraphStatefulPassDeclaration<TState>(name, state, record, flags));
    }

    private GpuRenderGraphPassBuilder AddPass(GpuRenderGraphPassDeclaration declaration)
    {
        passes.Add(declaration);
        return new(this, declaration);
    }

    private void ValidatePass(string name, GpuRenderGraphPassFlags flags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if ((flags & ~GpuRenderGraphPassFlags.NeverCull) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }
        if (!passNames.Add(name))
        {
            throw new ArgumentException($"A pass named '{name}' already exists.", nameof(name));
        }
    }

    public GpuRenderGraph MarkOutput(GpuRenderGraphResource resource)
    {
        RequireResource(resource);
        outputs.Add(resource);
        return this;
    }

    private GpuRenderGraphResourceDeclaration RequireResource(GpuRenderGraphResource resource)
    {
        GpuRenderGraphResourceDeclaration? declaration = resources.FirstOrDefault(
            candidate => candidate.Info.Resource == resource);
        if (resource.IsNull || declaration is null)
        {
            throw new ArgumentException("Resource does not belong to this render graph.", nameof(resource));
        }
        return declaration;
    }

    public GpuRenderGraphResource ExportTexture(GpuRenderGraphResource resource)
        => Export(resource, GpuRenderGraphResourceKind.Texture);

    public GpuRenderGraphResource ExportBuffer(GpuRenderGraphResource resource)
        => Export(resource, GpuRenderGraphResourceKind.Buffer);

    public GpuRenderGraphPlan Compile() => CompileUncached();

    public GpuRenderGraphPlan Compile(GpuRenderGraphPlanCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return cache.Compile(this);
    }

    internal GpuRenderGraphPlan CompileUncached()
    {
        var dataDependencies = new HashSet<int>[passes.Count];
        for (int index = 0; index < passes.Count; index++) { dataDependencies[index] = []; }
        var lastWriter = new Dictionary<GpuRenderGraphResource, int>();

        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            foreach (GpuRenderGraphResourceAccess access in passes[passIndex].Accesses)
            {
                if ((access.Access & GpuRenderGraphAccess.Read) != 0
                    && lastWriter.TryGetValue(access.Resource, out int writer))
                {
                    dataDependencies[passIndex].Add(writer);
                }
                if ((access.Access & GpuRenderGraphAccess.Write) != 0)
                {
                    lastWriter[access.Resource] = passIndex;
                }
            }
        }

        foreach (GpuRenderGraphResourceDeclaration resource in resources.Where(
            resource => resource.Info.IsExported))
        {
            if (!lastWriter.ContainsKey(resource.Info.Resource))
            {
                throw new InvalidOperationException(
                    $"Exported resource '{resource.Info.Name}' has no writer.");
            }
        }

        var live = new HashSet<int>();
        var pending = new Stack<int>();
        foreach (GpuRenderGraphResource output in outputs)
        {
            if (lastWriter.TryGetValue(output, out int writer)) { pending.Push(writer); }
        }
        for (int index = 0; index < passes.Count; index++)
        {
            if ((passes[index].Flags & GpuRenderGraphPassFlags.NeverCull) != 0) { pending.Push(index); }
        }
        while (pending.TryPop(out int passIndex))
        {
            if (!live.Add(passIndex)) { continue; }
            foreach (int dependency in dataDependencies[passIndex]) { pending.Push(dependency); }
        }

        int[] ordered = OrderLivePasses(live);
        GpuRenderGraphPassPlan[] passPlans = ordered.Select(index => new GpuRenderGraphPassPlan(
            passes[index].Name,
            index,
            [.. passes[index].Accesses])).ToArray();
        IGpuRenderGraphPassRecorder[] recorders = ordered
            .Select(index => (IGpuRenderGraphPassRecorder)passes[index])
            .ToArray();
        GpuRenderGraphBarrierPlan[] barriers = PlanBarriers(ordered);
        (GpuRenderGraphTransientResourcePlan[] transientResources,
            GpuRenderGraphTransientSlotPlan[] transientSlots,
            GpuRenderGraphAliasBarrierPlan[] aliasBarriers) = PlanTransients(ordered);
        GpuRenderGraphResourceInfo[] resourceInfos = resources
            .Select(resource => resource.Info with { })
            .ToArray();
        return new(
            resourceInfos,
            passPlans,
            barriers,
            transientResources,
            transientSlots,
            aliasBarriers,
            recorders);
    }

    internal void AddAccess(
        GpuRenderGraphPassDeclaration pass,
        GpuRenderGraphResource resource,
        GpuRenderGraphAccess access,
        GpuStage stage,
        GpuBarrierHazards hazards)
    {
        RequireResource(resource);
        if (access is not (GpuRenderGraphAccess.Read or GpuRenderGraphAccess.Write or GpuRenderGraphAccess.ReadWrite))
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }
        if (stage == GpuStage.None || (stage & ~AllStages) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        if ((hazards & ~AllHazards) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hazards));
        }
        if (pass.Accesses.Any(candidate => candidate.Resource == resource))
        {
            throw new InvalidOperationException("A pass may declare each resource once; use ReadWrite for combined access.");
        }
        pass.Accesses.Add(new(resource, access, stage, hazards));
    }

    private GpuRenderGraphResource ImportTexture(
        string name,
        GpuTextureHandle texture,
        GpuTextureDescription? description)
    {
        if (texture.IsNull) { throw new ArgumentException("Texture cannot be null.", nameof(texture)); }
        if (description is { } value) { value.Validate(); }
        return Import(
            name,
            GpuRenderGraphResourceKind.Texture,
            texture.Value,
            texture,
            default,
            description,
            null,
            GpuMemoryKind.DeviceLocal,
            false,
            null,
            null);
    }

    private GpuRenderGraphResource ImportBuffer(
        string name,
        GpuBufferHandle buffer,
        GpuBufferDescription? description)
    {
        if (buffer.IsNull) { throw new ArgumentException("Buffer cannot be null.", nameof(buffer)); }
        if (description is { } value) { value.Validate(); }
        return Import(
            name,
            GpuRenderGraphResourceKind.Buffer,
            buffer.Value,
            default,
            buffer,
            null,
            description,
            GpuMemoryKind.DeviceLocal,
            false,
            null,
            null);
    }

    private GpuRenderGraphResource Import(
        string name,
        GpuRenderGraphResourceKind kind,
        ulong nativeValue,
        GpuTextureHandle texture,
        GpuBufferHandle buffer,
        GpuTextureDescription? textureDescription,
        GpuBufferDescription? bufferDescription,
        GpuMemoryKind memoryKind,
        bool isTransient,
        GpuRenderGraphExportedTexture? importedTexture,
        GpuRenderGraphExportedBuffer? importedBuffer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (resourcesByName.ContainsKey(name))
        {
            throw new ArgumentException($"A resource named '{name}' already exists.", nameof(name));
        }
        if (!isTransient && importedResources.ContainsKey((kind, nativeValue)))
        {
            throw new ArgumentException("The native resource is already imported.", nameof(nativeValue));
        }

        var resource = new GpuRenderGraphResource(Interlocked.Increment(ref s_nextResourceId));
        var info = new GpuRenderGraphResourceInfo(resource, name, kind, texture, buffer)
        {
            TextureDescription = textureDescription,
            BufferDescription = bufferDescription,
            MemoryKind = memoryKind,
            IsTransient = isTransient,
            ImportedTexture = importedTexture,
            ImportedBuffer = importedBuffer,
        };
        resources.Add(new(info));
        resourcesByName.Add(name, resource);
        if (!isTransient) { importedResources.Add((kind, nativeValue), resource); }
        return resource;
    }

    private GpuRenderGraphResource Export(
        GpuRenderGraphResource resource,
        GpuRenderGraphResourceKind kind)
    {
        GpuRenderGraphResourceDeclaration declaration = RequireResource(resource);
        if (declaration.Info.Kind != kind)
        {
            throw new ArgumentException(
                $"Resource is not a {kind.ToString().ToLowerInvariant()}.",
                nameof(resource));
        }
        if (!declaration.Info.IsTransient)
        {
            throw new InvalidOperationException("Only graph-created resources can be exported.");
        }
        declaration.Info.IsExported = true;
        outputs.Add(resource);
        return resource;
    }

    private int[] OrderLivePasses(HashSet<int> live)
    {
        var edges = new HashSet<int>[passes.Count];
        var indegree = new int[passes.Count];
        for (int index = 0; index < passes.Count; index++) { edges[index] = []; }
        var lastWriter = new Dictionary<GpuRenderGraphResource, int>();
        var readers = new Dictionary<GpuRenderGraphResource, List<int>>();

        foreach (int passIndex in Enumerable.Range(0, passes.Count).Where(live.Contains))
        {
            foreach (GpuRenderGraphResourceAccess access in passes[passIndex].Accesses)
            {
                readers.TryGetValue(access.Resource, out List<int>? priorReaders);
                lastWriter.TryGetValue(access.Resource, out int priorWriter);
                bool hasWriter = lastWriter.ContainsKey(access.Resource);
                if ((access.Access & GpuRenderGraphAccess.Read) != 0)
                {
                    if (hasWriter) { AddEdge(priorWriter, passIndex); }
                    priorReaders ??= [];
                    priorReaders.Add(passIndex);
                    readers[access.Resource] = priorReaders;
                }
                if ((access.Access & GpuRenderGraphAccess.Write) != 0)
                {
                    if (hasWriter) { AddEdge(priorWriter, passIndex); }
                    if (priorReaders is not null)
                    {
                        foreach (int reader in priorReaders) { AddEdge(reader, passIndex); }
                        priorReaders.Clear();
                    }
                    lastWriter[access.Resource] = passIndex;
                }
            }
        }

        var ready = new SortedSet<int>(live.Where(index => indegree[index] == 0));
        var result = new List<int>(live.Count);
        while (ready.Count != 0)
        {
            int passIndex = ready.Min;
            ready.Remove(passIndex);
            result.Add(passIndex);
            foreach (int next in edges[passIndex])
            {
                if (--indegree[next] == 0) { ready.Add(next); }
            }
        }
        if (result.Count != live.Count)
        {
            throw new InvalidOperationException("Render graph contains a dependency cycle.");
        }
        return [.. result];

        void AddEdge(int from, int to)
        {
            if (from != to && edges[from].Add(to)) { indegree[to]++; }
        }
    }

    private GpuRenderGraphBarrierPlan[] PlanBarriers(int[] ordered)
    {
        var previous = new Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceAccess>();
        var result = new List<GpuRenderGraphBarrierPlan>();
        foreach (int passIndex in ordered)
        {
            GpuStage before = GpuStage.None;
            GpuStage after = GpuStage.None;
            GpuBarrierHazards hazards = GpuBarrierHazards.None;
            var transitioned = new List<GpuRenderGraphResource>();
            foreach (GpuRenderGraphResourceAccess current in passes[passIndex].Accesses)
            {
                bool requiresBarrier = !previous.TryGetValue(current.Resource, out GpuRenderGraphResourceAccess? prior)
                    || (prior.Access & GpuRenderGraphAccess.Write) != 0
                    || (current.Access & GpuRenderGraphAccess.Write) != 0;
                if (requiresBarrier)
                {
                    before |= prior?.Stage ?? GpuStage.None;
                    after |= current.Stage;
                    hazards |= current.Hazards | (prior?.Hazards ?? GpuBarrierHazards.None);
                    transitioned.Add(current.Resource);
                }
                previous[current.Resource] = current;
            }
            if (transitioned.Count != 0)
            {
                result.Add(new(
                    passes[passIndex].Name,
                    before,
                    after,
                    hazards,
                    [.. transitioned]));
            }
        }
        return [.. result];
    }

    private (
        GpuRenderGraphTransientResourcePlan[] Resources,
        GpuRenderGraphTransientSlotPlan[] Slots,
        GpuRenderGraphAliasBarrierPlan[] AliasBarriers) PlanTransients(int[] ordered)
    {
        var candidates = new List<GpuRenderGraphTransientCandidate>();
        for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
        {
            GpuRenderGraphResourceInfo info = resources[resourceIndex].Info;
            if (!info.IsTransient) { continue; }

            int firstPass = int.MaxValue;
            int lastPass = -1;
            for (int executionIndex = 0; executionIndex < ordered.Length; executionIndex++)
            {
                if (passes[ordered[executionIndex]].Accesses.Any(access => access.Resource == info.Resource))
                {
                    firstPass = Math.Min(firstPass, executionIndex);
                    lastPass = executionIndex;
                }
            }
            if (lastPass < 0) { continue; }
            if (info.IsExported) { lastPass = ordered.Length - 1; }

            candidates.Add(new(
                resourceIndex,
                info,
                new GpuTransientLifetime(firstPass, lastPass)));
        }

        var slots = new List<GpuRenderGraphTransientSlot>();
        var plans = new List<GpuRenderGraphTransientResourcePlan>(candidates.Count);
        foreach (GpuRenderGraphTransientCandidate candidate in candidates
            .OrderBy(candidate => candidate.Lifetime.FirstPass)
            .ThenBy(candidate => candidate.DeclarationIndex))
        {
            GpuRenderGraphTransientSlot? slot = slots.FirstOrDefault(existing =>
                existing.IsCompatible(candidate.Info)
                && existing.Lifetimes.All(lifetime => !lifetime.Overlaps(candidate.Lifetime)));
            if (slot is null)
            {
                slot = new(slots.Count, candidate.Info);
                slots.Add(slot);
            }
            slot.Resources.Add(candidate.Info.Resource);
            slot.Lifetimes.Add(candidate.Lifetime);
            plans.Add(new(candidate.Info.Resource, candidate.Lifetime, slot.Index));
        }

        var aliasBarriers = new List<GpuRenderGraphAliasBarrierPlan>();
        foreach (GpuRenderGraphTransientSlot slot in slots)
        {
            GpuRenderGraphTransientCandidate[] assigned = slot.Resources
                .Select(resource => candidates.Single(candidate => candidate.Info.Resource == resource))
                .OrderBy(candidate => candidate.Lifetime.FirstPass)
                .ToArray();
            for (int index = 1; index < assigned.Length; index++)
            {
                GpuRenderGraphTransientCandidate before = assigned[index - 1];
                GpuRenderGraphTransientCandidate after = assigned[index];
                GpuRenderGraphResourceAccess beforeAccess = passes[ordered[before.Lifetime.LastPass]].Accesses
                    .Single(access => access.Resource == before.Info.Resource);
                GpuRenderGraphResourceAccess afterAccess = passes[ordered[after.Lifetime.FirstPass]].Accesses
                    .Single(access => access.Resource == after.Info.Resource);
                aliasBarriers.Add(new(
                    passes[ordered[after.Lifetime.FirstPass]].Name,
                    slot.Index,
                    before.Info.Resource,
                    after.Info.Resource,
                    beforeAccess.Stage,
                    afterAccess.Stage,
                    beforeAccess.Hazards | afterAccess.Hazards));
            }
        }

        return (
            [.. plans],
            [.. slots.Select(slot => new GpuRenderGraphTransientSlotPlan(
                slot.Index,
                slot.Kind,
                slot.MemoryKind,
                slot.TextureDescription,
                slot.BufferDescription,
                [.. slot.Resources]))],
            [.. aliasBarriers]);
    }

}
