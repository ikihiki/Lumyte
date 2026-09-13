using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources;

namespace Lumyte.Graphics.Portable.RenderGraph;

public sealed class PortableRenderProvider : IGpuRenderProvider
{
    private readonly PortableRenderBackendFactory createBackend;
    private readonly Dictionary<GpuRenderPassId, PassRegistration> passes;
    public PortableRenderProvider(string id, PortableRenderBackendFactory createBackend, PortableRenderPassRegistry passes)
    {
        ArgumentException.ThrowIfNullOrEmpty(id); ArgumentNullException.ThrowIfNull(createBackend); ArgumentNullException.ThrowIfNull(passes);
        Id = id; this.createBackend = createBackend; this.passes = passes.Snapshot();
    }
    public string Id { get; }
    public int ContractVersion => 1;
    public async ValueTask<IGpuRenderRuntime> CreateAsync(GpuRenderRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options); cancellationToken.ThrowIfCancellationRequested();
        foreach (GpuRenderPassId required in options.RequiredPasses)
        {
            if (!passes.ContainsKey(required)) { throw new NotSupportedException($"Portable pass '{required}' is not registered."); }
        }
        IPortableGpuBackend backend = await createBackend(options, cancellationToken).ConfigureAwait(false);
        GpuResourceManager? manager = null;
        var instances = new Dictionary<GpuRenderPassId, PassInstance>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); manager = new(backend);
            var services = new PortablePassServices(backend, manager);
            foreach (var registration in passes) { instances.Add(registration.Key, registration.Value.Create(services)); }
            return new PortableRenderRuntime(services, passes, instances);
        }
        catch
        {
            foreach (PassInstance instance in instances.Values.Reverse()) { await instance.DisposeAsync().ConfigureAwait(false); }
            if (manager is not null) { await manager.DisposeAsync().ConfigureAwait(false); }
            backend.Dispose(); throw;
        }
    }
}

/// <summary>One independently owned Portable runtime. Feature builds are serialized and submissions remain asynchronous.</summary>
public sealed class PortableRenderRuntime : IGpuRenderRuntime
{
    private readonly PortablePassServices services;
    private readonly Dictionary<GpuRenderPassId, PassRegistration> registrations;
    private readonly Dictionary<GpuRenderPassId, PassInstance> passes;
    private readonly SemaphoreSlim builds = new(1, 1);
    private readonly HashSet<IDisposable> executionOwners = [];
    private bool stopping;
    private bool disposed;
    private int operationCount;
    private TaskCompletionSource? operationsEnded;
    internal object Gate { get; } = new();
    internal SemaphoreSlim Work => builds;
    internal PortableRenderRuntime(PortablePassServices services, Dictionary<GpuRenderPassId, PassRegistration> registrations,
        Dictionary<GpuRenderPassId, PassInstance> passes)
    {
        this.services = services; this.registrations = registrations; this.passes = passes;
        Resources = new PortableGraphResources(this, services.Resources);
    }
    public Guid Id { get; } = Guid.NewGuid();
    public PortableGraphResources Resources { get; }
    IGpuGraphResources IGpuRenderRuntime.Resources => Resources;
    public IPortableGpuBackend Backend => services.Backend;
    public void StopAccepting() { lock (Gate) { stopping = true; } }
    internal void CheckOpen() { ObjectDisposedException.ThrowIf(stopping || disposed, this); }
    internal IDisposable BeginOperation()
    {
        lock (Gate)
        {
            CheckOpen();
            if (operationCount++ == 0) { operationsEnded = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            return new Operation(this);
        }
    }
    private sealed class Operation(PortableRenderRuntime runtime) : IDisposable
    {
        private bool ended;
        public void Dispose()
        {
            lock (runtime.Gate)
            {
                if (ended) { return; } ended = true;
                if (--runtime.operationCount == 0) { runtime.operationsEnded!.TrySetResult(); }
            }
        }
    }

    public ValueTask<GpuRenderGraphExecution> SubmitAsync(GpuRenderGraphPlan plan,
        GpuRenderGraphBindings? bindings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        GpuRenderGraphBindings resolved = plan.ValidateBindings(bindings);
        lock (Gate)
        {
            CheckOpen();
            foreach (GpuRenderGraphPass pass in plan.Passes)
            {
                if (!registrations.TryGetValue(pass.Id, out PassRegistration? registration) || !registration.Matches(pass))
                { throw new NotSupportedException($"No matching Portable implementation is registered for '{pass.Id}'."); }
            }
            // Holds are obtained synchronously, before waiting for another build or invoking an async pass.
            GpuResourceBatch batch = services.Resources.BeginBatch();
            try
            {
                foreach (GpuRenderGraphResource resource in plan.Resources)
                {
                    GpuGraphResourceRef? reference = resolved.ResolveResource(resource);
                    if (reference is not null) { batch.Use(Resources.Resolve(reference)); }
                }
                return SubmitCoreAsync(plan, resolved, batch, BeginOperation(), cancellationToken);
            }
            catch { batch.Dispose(); throw; }
        }
    }
    private async ValueTask<GpuRenderGraphExecution> SubmitCoreAsync(GpuRenderGraphPlan plan, GpuRenderGraphBindings bindings,
        GpuResourceBatch batch, IDisposable operation, CancellationToken cancellationToken)
    {
        bool entered = false;
        GpuResourceScope? scope = null;
        List<IDisposable> exportPins = [];
        try
        {
            await builds.WaitAsync(cancellationToken).ConfigureAwait(false); entered = true;
            ObjectDisposedException.ThrowIf(disposed, this); scope = services.Resources.CreateScope();
            var build = new PortableExecutionBuild(services, bindings, batch);
            foreach (GpuRenderGraphResource resource in plan.Resources)
            {
                switch (resource)
                {
                    case GpuRenderGraphTexture texture:
                        GpuTextureRef? textureRef = bindings.ResolveResource(texture) is { } textureInput ? (GpuTextureRef)Resources.Resolve(textureInput) : null;
                        build.Resources.Add(texture, new PortablePassTexture(build, texture.Name,
                            textureRef?.Description ?? PortableGraphResources.Describe(texture.Description,
                                plan.Exports.Contains(texture) ? GpuTextureUsage.CopySource : GpuTextureUsage.None), textureRef)); break;
                    case GpuRenderGraphBuffer buffer:
                        GpuBufferRef? bufferRef = bindings.ResolveResource(buffer) is { } bufferInput ? (GpuBufferRef)Resources.Resolve(bufferInput) : null;
                        build.Resources.Add(buffer, new PortablePassBuffer(build, buffer.Name,
                            bufferRef?.Description ?? new(buffer.Description.Size,
                                plan.Exports.Contains(buffer) ? GpuBufferUsage.CopySource : GpuBufferUsage.None), bufferRef)); break;
                }
            }
            foreach (GpuRenderGraphPass pass in plan.Passes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var context = new PortablePassBuildContext(build, pass);
                try { await passes[pass.Id].BuildAsync(context, pass, cancellationToken).ConfigureAwait(false); }
                finally { context.End(); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (Gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this); build.Prepare(scope);
                var record = new PortablePassRecordContext(build, batch.StartCommandRecording());
                foreach (PortablePassBuilder pass in build.Passes) { pass.Record(record); }
                var exports = new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>();
                foreach (GpuRenderGraphResource export in plan.Exports)
                {
                    GpuGraphResourceRef reference = build.Resources[export] switch
                    {
                        PortablePassTexture texture => Resources.Wrap(texture.Reference!),
                        PortablePassBuffer buffer => Resources.Wrap(buffer.Reference!),
                        _ => throw new InvalidOperationException("Only physical resources can be exported."),
                    };
                    exports.Add(export, reference);
                    exportPins.Add(services.Resources.Pin(Resources.Resolve(reference)));
                }
                batch.Own(scope); scope = null;
                GpuSubmissionToken token;
                try { token = batch.Submit(); }
                catch (Resources.GpuSubmissionException failure)
                {
                    GpuSubmissionToken failedToken = failure.Completion;
                    throw new GpuRenderGraphSubmissionException(new(() => failedToken.IsComplete, failedToken.WaitAsync), failure);
                }
                batch.Dispose();
                var completion = new GpuGraphCompletion(() => token.IsComplete, token.WaitAsync);
                var ownership = new LockedOwnership(this, exportPins);
                var execution = new GpuRenderGraphExecution(completion, exports, ownership);
                exportPins = []; executionOwners.Add(ownership); return execution;
            }
        }
        finally
        {
            try
            {
                lock (Gate)
                {
                    // A failed Submit still leaves its batch's resource uses retained by the manager token.
                    batch.Dispose(); scope?.Dispose();
                    foreach (IDisposable pin in exportPins) { pin.Dispose(); }
                }
            }
            finally { if (entered) { builds.Release(); } operation.Dispose(); }
        }
    }
    public async ValueTask WaitIdleAsync(CancellationToken cancellationToken = default)
    {
        await builds.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await services.Resources.WaitIdleAsync(cancellationToken).ConfigureAwait(false); }
        finally { builds.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (Gate)
        { if (disposed) { return; } stopping = true; pending = operationCount == 0 ? Task.CompletedTask : operationsEnded!.Task; }
        await pending.ConfigureAwait(false);
        await builds.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) { return; }
            await services.Resources.WaitIdleAsync().ConfigureAwait(false);
            lock (Gate)
            {
                if (executionOwners.Count != 0)
                { throw new InvalidOperationException("Dispose graph executions before disposing their runtime."); }
                Resources.RequireOwnersReturned(); services.Resources.Collect();
            }
            foreach (PassInstance pass in passes.Values.Reverse()) { await pass.DisposeAsync().ConfigureAwait(false); }
            await services.Resources.DisposeAsync().ConfigureAwait(false);
            services.Backend.Dispose(); Resources.ClearReferences(); disposed = true;
        }
        finally { builds.Release(); }
    }
    private sealed class LockedOwnership(PortableRenderRuntime runtime, List<IDisposable> pins) : IDisposable
    {
        public void Dispose()
        {
            lock (runtime.Gate)
            { foreach (IDisposable pin in pins) { pin.Dispose(); } pins.Clear(); runtime.executionOwners.Remove(this); }
        }
    }
}
