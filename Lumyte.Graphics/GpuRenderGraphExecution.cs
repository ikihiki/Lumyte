namespace Lumyte.Graphics;

/// <summary>Low-allocation render-graph callback with explicit state.</summary>
public delegate void GpuRenderGraphPassAction<TState>(
    GpuRenderGraphPassContextView context,
    TState state);

/// <summary>
/// Stack-only pass context used by render-graph callbacks without allocating a context object.
/// </summary>
public readonly ref struct GpuRenderGraphPassContextView
{
    private readonly IGpuBackend? backend;
    private readonly IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources;
    private readonly IReadOnlySet<GpuRenderGraphResource> allowedResources;

    internal GpuRenderGraphPassContextView(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources)
    {
        Commands = commands;
        this.backend = backend;
        this.resources = resources;
        this.allowedResources = allowedResources;
    }

    public GpuCommandBuffer Commands { get; }

    public GpuTextureHandle GetTexture(GpuRenderGraphResource resource)
        => RequireResource(resource, GpuRenderGraphResourceKind.Texture).Texture;

    public GpuBufferHandle GetBuffer(GpuRenderGraphResource resource)
        => RequireResource(resource, GpuRenderGraphResourceKind.Buffer).Buffer;

    public GpuTextureView GetTextureView(GpuRenderGraphResource resource)
    {
        GpuRenderGraphResourceRuntime runtime = RequireResource(
            resource,
            GpuRenderGraphResourceKind.Texture);
        if (runtime.View is { } view) { return view; }
        if (backend is null)
        {
            throw new InvalidOperationException(
                "Texture views for graph resources require Execute(IGpuBackend).");
        }
        GpuTextureDescription description = runtime.Info.TextureDescription
            ?? throw new InvalidOperationException(
                "The imported texture has no description for view creation.");
        view = backend.CreateTextureView(runtime.Texture, new(description.Format));
        runtime.View = view;
        return view;
    }

    public GpuMemoryAddress GetBufferMemoryAddress(
        GpuRenderGraphResource resource,
        ulong offset = 0,
        ulong length = 0)
    {
        GpuRenderGraphResourceRuntime runtime = RequireResource(
            resource,
            GpuRenderGraphResourceKind.Buffer);
        if (backend is null)
        {
            throw new InvalidOperationException(
                "Buffer addresses for graph resources require Execute(IGpuBackend).");
        }
        if (offset > runtime.Buffer.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        ulong resolvedLength = length == 0 ? runtime.Buffer.Size - offset : length;
        if (resolvedLength > runtime.Buffer.Size - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        return backend.GetBufferMemoryAddress(runtime.Buffer, offset, resolvedLength);
    }

    private GpuRenderGraphResourceRuntime RequireResource(
        GpuRenderGraphResource resource,
        GpuRenderGraphResourceKind kind)
    {
        if (!allowedResources.Contains(resource))
        {
            throw new InvalidOperationException(
                "A pass may only resolve resources declared in its access list.");
        }
        if (!resources.TryGetValue(resource, out GpuRenderGraphResourceRuntime? runtime))
        {
            throw new ArgumentException(
                "Resource is not available in this graph execution.",
                nameof(resource));
        }
        if (runtime.Info.Kind != kind)
        {
            throw new ArgumentException(
                $"Resource is not a {kind.ToString().ToLowerInvariant()}.",
                nameof(resource));
        }
        return runtime;
    }
}

public sealed class GpuRenderGraphExecution : IDisposable
{
    private IGpuBackend? backend;
    private readonly Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> exported;
    private readonly GpuPersistentArena? arena;
    private readonly GpuMemoryAllocation[] retainedAllocations;
    private readonly bool ownsArena;
    private readonly GpuRetirementQueue? retirementQueue;
    private int importLeaseCount;
    private IReadOnlyList<Action>? deferredDisposal;

    internal GpuRenderGraphExecution(
        IGpuBackend backend,
        Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> exported,
        GpuPersistentArena? arena,
        GpuMemoryAllocation[] retainedAllocations,
        bool ownsArena,
        GpuRetirementQueue? retirementQueue = null,
        GpuSubmissionToken completion = default)
    {
        this.backend = backend;
        this.exported = exported;
        this.arena = arena;
        this.retainedAllocations = retainedAllocations;
        this.ownsArena = ownsArena;
        this.retirementQueue = retirementQueue;
        Completion = completion;
    }

    /// <summary>The GPU submission associated with this execution. Synchronous executions are already complete.</summary>
    public GpuSubmissionToken Completion { get; }
    public bool IsComplete => Completion.IsComplete;

    public void WaitForCompletion() => Completion.Wait();

    public GpuRenderGraphExportedTexture GetTexture(GpuRenderGraphResource resource)
    {
        GpuRenderGraphResourceRuntime runtime = RequireExport(
            resource,
            GpuRenderGraphResourceKind.Texture);
        GpuTextureDescription description = runtime.Info.TextureDescription
            ?? throw new InvalidOperationException("The exported texture has no description.");
        return new(this, backend!, runtime.Texture, description);
    }

    public GpuRenderGraphExportedBuffer GetBuffer(GpuRenderGraphResource resource)
    {
        GpuRenderGraphResourceRuntime runtime = RequireExport(
            resource,
            GpuRenderGraphResourceKind.Buffer);
        GpuBufferDescription description = runtime.Info.BufferDescription
            ?? throw new InvalidOperationException("The exported buffer has no description.");
        return new(this, backend!, runtime.Buffer, description);
    }

    public void Dispose()
    {
        if (backend is not { } owner) { return; }
        GpuRenderGraphResourceRuntime[] resources = exported.Values.ToArray();
        exported.Clear();
        backend = null;

        var releases = new List<Action>();
        foreach (GpuRenderGraphResourceRuntime runtime in resources)
        {
            releases.Add(() => runtime.Dispose(owner));
        }
        if (arena is not null)
        {
            foreach (GpuMemoryAllocation allocation in retainedAllocations)
            {
                releases.Add(() => arena.Release(allocation));
            }
            if (ownsArena) { releases.Add(arena.Dispose); }
        }

        if (importLeaseCount == 0) { ScheduleDisposal(releases); }
        else { deferredDisposal = releases; }
    }

    internal void RequireAlive()
        => ObjectDisposedException.ThrowIf(backend is null, this);

    internal IDisposable AcquireImportLease()
    {
        RequireAlive();
        importLeaseCount++;
        return new GpuRenderGraphImportLease(this);
    }

    private void ReleaseImportLease()
    {
        if (importLeaseCount <= 0)
        {
            throw new InvalidOperationException("Render-graph import lease was released more than once.");
        }
        importLeaseCount--;
        if (importLeaseCount == 0 && deferredDisposal is { } releases)
        {
            deferredDisposal = null;
            ScheduleDisposal(releases);
        }
    }

    private void ScheduleDisposal(IReadOnlyList<Action> releases)
    {
        if (retirementQueue is null)
        {
            foreach (Action release in releases) { release(); }
        }
        else { retirementQueue.Retire(Completion, releases); }
    }

    private sealed class GpuRenderGraphImportLease(GpuRenderGraphExecution owner) : IDisposable
    {
        private GpuRenderGraphExecution? owner = owner;

        public void Dispose()
        {
            GpuRenderGraphExecution? current = Interlocked.Exchange(ref owner, null);
            current?.ReleaseImportLease();
        }
    }

    private GpuRenderGraphResourceRuntime RequireExport(
        GpuRenderGraphResource resource,
        GpuRenderGraphResourceKind kind)
    {
        RequireAlive();
        if (!exported.TryGetValue(resource, out GpuRenderGraphResourceRuntime? runtime))
        {
            throw new ArgumentException(
                "Resource was not exported by this execution.",
                nameof(resource));
        }
        if (runtime.Info.Kind != kind)
        {
            throw new ArgumentException(
                $"Resource is not a {kind.ToString().ToLowerInvariant()}.",
                nameof(resource));
        }
        return runtime;
    }
}

public sealed class GpuRenderGraphExportedTexture
{
    private readonly GpuRenderGraphExecution execution;
    private readonly IGpuBackend backend;

    internal GpuRenderGraphExportedTexture(
        GpuRenderGraphExecution execution,
        IGpuBackend backend,
        GpuTextureHandle texture,
        GpuTextureDescription description)
    {
        this.execution = execution;
        this.backend = backend;
        Texture = texture;
        Description = description;
    }

    public GpuTextureHandle Texture { get; }
    public GpuTextureDescription Description { get; }

    internal void RequireAlive() => execution.RequireAlive();

    internal void RequireBackend(IGpuBackend candidate)
    {
        execution.RequireAlive();
        if (!ReferenceEquals(backend, candidate))
        {
            throw new InvalidOperationException(
                "An exported texture must be imported by its owning backend.");
        }
    }

    internal IDisposable AcquireImportLease(IGpuBackend candidate)
    {
        RequireBackend(candidate);
        return execution.AcquireImportLease();
    }
}

public sealed class GpuRenderGraphExportedBuffer
{
    private readonly GpuRenderGraphExecution execution;
    private readonly IGpuBackend backend;

    internal GpuRenderGraphExportedBuffer(
        GpuRenderGraphExecution execution,
        IGpuBackend backend,
        GpuBufferHandle buffer,
        GpuBufferDescription description)
    {
        this.execution = execution;
        this.backend = backend;
        Buffer = buffer;
        Description = description;
    }

    public GpuBufferHandle Buffer { get; }
    public GpuBufferDescription Description { get; }

    internal void RequireAlive() => execution.RequireAlive();

    internal void RequireBackend(IGpuBackend candidate)
    {
        execution.RequireAlive();
        if (!ReferenceEquals(backend, candidate))
        {
            throw new InvalidOperationException(
                "An exported buffer must be imported by its owning backend.");
        }
    }

    internal IDisposable AcquireImportLease(IGpuBackend candidate)
    {
        RequireBackend(candidate);
        return execution.AcquireImportLease();
    }
}

internal sealed class GpuRenderGraphResourceRuntime
{
    private GpuMemoryAllocation? allocation;
    private bool ownsResource;

    private GpuRenderGraphResourceRuntime(GpuRenderGraphResourceInfo info)
    {
        Info = info;
        Texture = info.Texture;
        Buffer = info.Buffer;
    }

    public GpuRenderGraphResourceInfo Info { get; }
    public GpuTextureHandle Texture { get; private set; }
    public GpuBufferHandle Buffer { get; private set; }
    public GpuTextureView? View { get; set; }

    public static GpuRenderGraphResourceRuntime Import(GpuRenderGraphResourceInfo info)
        => new(info);

    public static GpuRenderGraphResourceRuntime Create(
        IGpuBackend backend,
        GpuRenderGraphResourceInfo info)
    {
        var result = new GpuRenderGraphResourceRuntime(info)
        {
            ownsResource = true,
        };
        try
        {
            if (info.Kind == GpuRenderGraphResourceKind.Texture)
            {
                result.CreateTexture(backend);
            }
            else
            {
                result.CreateBuffer(backend);
            }
            return result;
        }
        catch
        {
            result.Dispose(backend);
            throw;
        }
    }

    public static GpuRenderGraphResourceRuntime Create(
        IGpuBackend backend,
        GpuRenderGraphResourceInfo info,
        GpuMemoryAllocation allocation)
    {
        var result = new GpuRenderGraphResourceRuntime(info)
        {
            ownsResource = true,
        };
        try
        {
            if (info.Kind == GpuRenderGraphResourceKind.Texture)
            {
                GpuTextureDescription description = info.TextureDescription
                    ?? throw new InvalidOperationException("Transient texture has no description.");
                result.Texture = backend.CreatePlacedTexture(description, allocation);
            }
            else
            {
                GpuBufferDescription description = info.BufferDescription
                    ?? throw new InvalidOperationException("Transient buffer has no description.");
                result.Buffer = backend.CreatePlacedBuffer(description, allocation);
            }
            return result;
        }
        catch
        {
            result.Dispose(backend);
            throw;
        }
    }

    public void Dispose(IGpuBackend backend)
    {
        if (!ownsResource) { return; }
        if (!Texture.IsNull)
        {
            backend.DestroyTexture(Texture);
            Texture = default;
        }
        if (!Buffer.IsNull)
        {
            backend.DestroyBuffer(Buffer);
            Buffer = default;
        }
        if (allocation is { } memory)
        {
            backend.FreeMemory(memory);
            allocation = null;
        }
        ownsResource = false;
    }

    private void CreateTexture(IGpuBackend backend)
    {
        GpuTextureDescription description = Info.TextureDescription
            ?? throw new InvalidOperationException("Transient texture has no description.");
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            Texture = backend.CreateTexture(description);
            return;
        }
        if ((backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) == 0)
        {
            throw new NotSupportedException("The backend cannot create render-graph textures.");
        }

        GpuTextureMemoryRequirements requirements =
            backend.GetTextureMemoryRequirements(description);
        allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            Info.MemoryKind);
        Texture = backend.CreatePlacedTexture(description, allocation.Value);
    }

    private void CreateBuffer(IGpuBackend backend)
    {
        GpuBufferDescription description = Info.BufferDescription
            ?? throw new InvalidOperationException("Transient buffer has no description.");
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            Buffer = backend.CreateBuffer(description);
            return;
        }
        if ((backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) == 0)
        {
            throw new NotSupportedException("The backend cannot create render-graph buffers.");
        }

        GpuBufferMemoryRequirements requirements =
            backend.GetBufferMemoryRequirements(description);
        allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            Info.MemoryKind);
        Buffer = backend.CreatePlacedBuffer(description, allocation.Value);
    }
}
