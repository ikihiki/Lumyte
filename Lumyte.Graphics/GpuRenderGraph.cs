namespace Lumyte.Graphics;

public enum GpuRenderGraphResourceKind
{
    Texture,
    Buffer,
}

[Flags]
public enum GpuRenderGraphAccess
{
    Read = 1 << 0,
    Write = 1 << 1,
    ReadWrite = Read | Write,
}

[Flags]
public enum GpuRenderGraphPassFlags
{
    None = 0,
    NeverCull = 1 << 0,
}

public readonly record struct GpuRenderGraphResource(int Value)
{
    public bool IsNull => Value == 0;
}

public sealed record GpuRenderGraphResourceInfo(
    GpuRenderGraphResource Resource,
    string Name,
    GpuRenderGraphResourceKind Kind,
    GpuTextureHandle Texture,
    GpuBufferHandle Buffer)
{
    public GpuTextureDescription? TextureDescription { get; internal init; }
    public GpuBufferDescription? BufferDescription { get; internal init; }
    public GpuMemoryKind MemoryKind { get; internal init; } = GpuMemoryKind.DeviceLocal;
    public bool IsTransient { get; internal init; }
    public bool IsExported { get; internal set; }

    internal GpuRenderGraphExportedTexture? ImportedTexture { get; init; }
    internal GpuRenderGraphExportedBuffer? ImportedBuffer { get; init; }
}

public sealed record GpuRenderGraphResourceAccess(
    GpuRenderGraphResource Resource,
    GpuRenderGraphAccess Access,
    GpuStage Stage,
    GpuBarrierHazards Hazards);

public sealed class GpuRenderGraphPassPlan
{
    internal GpuRenderGraphPassPlan(string name, int declarationIndex, GpuRenderGraphResourceAccess[] accesses)
    {
        Name = name;
        DeclarationIndex = declarationIndex;
        Accesses = Array.AsReadOnly(accesses);
    }

    public string Name { get; }
    public int DeclarationIndex { get; }
    public IReadOnlyList<GpuRenderGraphResourceAccess> Accesses { get; }
}

public sealed class GpuRenderGraphBarrierPlan
{
    internal GpuRenderGraphBarrierPlan(
        string destinationPass,
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards,
        GpuRenderGraphResource[] resources)
    {
        DestinationPass = destinationPass;
        Before = before;
        After = after;
        Hazards = hazards;
        Resources = Array.AsReadOnly(resources);
    }

    public string DestinationPass { get; }
    public GpuStage Before { get; }
    public GpuStage After { get; }
    public GpuBarrierHazards Hazards { get; }
    public IReadOnlyList<GpuRenderGraphResource> Resources { get; }
}

public sealed class GpuRenderGraphPlan
{
    private readonly Action<GpuRenderGraphPassContext>[] recorders;
    private readonly IReadOnlyDictionary<int, GpuRenderGraphBarrierPlan> barriersByPass;

    internal GpuRenderGraphPlan(
        GpuRenderGraphResourceInfo[] resources,
        GpuRenderGraphPassPlan[] passes,
        GpuRenderGraphBarrierPlan[] barriers,
        Action<GpuRenderGraphPassContext>[] recorders)
    {
        Resources = Array.AsReadOnly(resources);
        Passes = Array.AsReadOnly(passes);
        Barriers = Array.AsReadOnly(barriers);
        this.recorders = recorders;
        barriersByPass = barriers.ToDictionary(
            barrier => Array.FindIndex(passes, pass => pass.Name == barrier.DestinationPass));
    }

    public IReadOnlyList<GpuRenderGraphResourceInfo> Resources { get; }
    public IReadOnlyList<GpuRenderGraphPassPlan> Passes { get; }
    public IReadOnlyList<GpuRenderGraphBarrierPlan> Barriers { get; }

    public GpuCommandBuffer Record(IGpuQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (Resources.Any(resource => resource.IsTransient))
        {
            throw new InvalidOperationException(
                "Plans with transient resources require Execute(IGpuBackend).");
        }

        Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> runtimes =
            Resources.ToDictionary(
                resource => resource.Resource,
                GpuRenderGraphResourceRuntime.Import);
        GpuCommandBuffer commands = queue.StartCommandRecording();
        RecordCommands(commands, null, runtimes);
        return commands;
    }

    public GpuRenderGraphExecution Execute(IGpuBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        var runtimes = new Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime>();
        try
        {
            HashSet<GpuRenderGraphResource> required = Passes
                .SelectMany(pass => pass.Accesses)
                .Select(access => access.Resource)
                .Concat(Resources
                    .Where(resource => resource.IsExported)
                    .Select(resource => resource.Resource))
                .ToHashSet();
            foreach (GpuRenderGraphResourceInfo resource in Resources.Where(
                resource => required.Contains(resource.Resource)))
            {
                resource.ImportedTexture?.RequireBackend(backend);
                resource.ImportedBuffer?.RequireBackend(backend);
                GpuRenderGraphResourceRuntime runtime = resource.IsTransient
                    ? GpuRenderGraphResourceRuntime.Create(backend, resource)
                    : GpuRenderGraphResourceRuntime.Import(resource);
                runtimes.Add(resource.Resource, runtime);
            }

            IGpuQueue queue = backend.MainQueue;
            GpuCommandBuffer commands = queue.StartCommandRecording();
            RecordCommands(commands, backend, runtimes);
            using GpuSemaphore completion = queue.CreateSemaphore();
            queue.Submit([commands], completion, 1);
            queue.Wait(completion, 1);
            DestroyViews(backend, runtimes.Values);

            Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> exported =
                runtimes
                    .Where(pair => pair.Value.Info.IsExported)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (GpuRenderGraphResourceRuntime runtime in runtimes.Values.Where(
                runtime => !runtime.Info.IsExported))
            {
                runtime.Dispose(backend);
            }
            return new(backend, exported);
        }
        catch
        {
            DestroyViews(backend, runtimes.Values);
            foreach (GpuRenderGraphResourceRuntime runtime in runtimes.Values)
            {
                runtime.Dispose(backend);
            }
            throw;
        }
    }

    private static void DestroyViews(
        IGpuBackend backend,
        IEnumerable<GpuRenderGraphResourceRuntime> runtimes)
    {
        foreach (GpuRenderGraphResourceRuntime runtime in runtimes)
        {
            if (runtime.View is { } view)
            {
                backend.DestroyTextureView(view);
                runtime.View = null;
            }
        }
    }

    private void RecordCommands(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> runtimes)
    {
        for (int index = 0; index < Passes.Count; index++)
        {
            if (barriersByPass.TryGetValue(index, out GpuRenderGraphBarrierPlan? barrier))
            {
                commands.Barrier(barrier.Before, barrier.After, barrier.Hazards);
            }
            IReadOnlySet<GpuRenderGraphResource> allowedResources = Passes[index].Accesses
                .Select(access => access.Resource)
                .ToHashSet();
            recorders[index](new(commands, backend, runtimes, allowedResources));
        }
    }
}

public sealed class GpuRenderGraph
{
    private static int s_nextResourceId;
    private const GpuStage AllStages = GpuStage.DrawIndirect | GpuStage.VertexShader | GpuStage.PixelShader
        | GpuStage.ComputeShader | GpuStage.ColorOutput | GpuStage.DepthStencil | GpuStage.Copy
        | GpuStage.AllGraphics | GpuStage.All;
    private const GpuBarrierHazards AllHazards = GpuBarrierHazards.Descriptors
        | GpuBarrierHazards.IndirectArguments | GpuBarrierHazards.DepthCaches;
    private readonly List<ResourceDeclaration> resources = [];
    private readonly List<PassDeclaration> passes = [];
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

    public GpuRenderGraphPassBuilder AddPass(
        string name,
        Action<GpuRenderGraphPassContext> record,
        GpuRenderGraphPassFlags flags = GpuRenderGraphPassFlags.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(record);
        if ((flags & ~GpuRenderGraphPassFlags.NeverCull) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }
        if (!passNames.Add(name))
        {
            throw new ArgumentException($"A pass named '{name}' already exists.", nameof(name));
        }

        var declaration = new PassDeclaration(name, record, flags);
        passes.Add(declaration);
        return new(this, declaration);
    }

    public GpuRenderGraph MarkOutput(GpuRenderGraphResource resource)
    {
        RequireResource(resource);
        outputs.Add(resource);
        return this;
    }

    public GpuRenderGraphResource ExportTexture(GpuRenderGraphResource resource)
        => Export(resource, GpuRenderGraphResourceKind.Texture);

    public GpuRenderGraphResource ExportBuffer(GpuRenderGraphResource resource)
        => Export(resource, GpuRenderGraphResourceKind.Buffer);

    public GpuRenderGraphPlan Compile()
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

        foreach (ResourceDeclaration resource in resources.Where(
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
        Action<GpuRenderGraphPassContext>[] recorders = ordered
            .Select(index => passes[index].Record)
            .ToArray();
        GpuRenderGraphBarrierPlan[] barriers = PlanBarriers(ordered);
        GpuRenderGraphResourceInfo[] resourceInfos = resources
            .Select(resource => resource.Info with { })
            .ToArray();
        return new(resourceInfos, passPlans, barriers, recorders);
    }

    internal void AddAccess(
        PassDeclaration pass,
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
        ResourceDeclaration declaration = RequireResource(resource);
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

    private ResourceDeclaration RequireResource(GpuRenderGraphResource resource)
    {
        ResourceDeclaration? declaration = resources.FirstOrDefault(
            candidate => candidate.Info.Resource == resource);
        if (resource.IsNull || declaration is null)
        {
            throw new ArgumentException("Resource does not belong to this render graph.", nameof(resource));
        }
        return declaration;
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

    private sealed record ResourceDeclaration(GpuRenderGraphResourceInfo Info);

    internal sealed class PassDeclaration(
        string name,
        Action<GpuRenderGraphPassContext> record,
        GpuRenderGraphPassFlags flags)
    {
        public string Name { get; } = name;
        public Action<GpuRenderGraphPassContext> Record { get; } = record;
        public GpuRenderGraphPassFlags Flags { get; } = flags;
        public List<GpuRenderGraphResourceAccess> Accesses { get; } = [];
    }
}

public sealed class GpuRenderGraphPassBuilder
{
    private readonly GpuRenderGraph owner;
    private readonly GpuRenderGraph.PassDeclaration pass;

    internal GpuRenderGraphPassBuilder(GpuRenderGraph owner, GpuRenderGraph.PassDeclaration pass)
    {
        this.owner = owner;
        this.pass = pass;
    }

    public GpuRenderGraphPassBuilder Read(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.Read, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder Write(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.Write, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder ReadWrite(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.ReadWrite, stage, hazards);
        return this;
    }
}
