using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph;

public sealed class NativeRenderRuntime : IGpuRenderRuntime
{
    private readonly NativePassServices services;
    private readonly Dictionary<GpuRenderPassId, NativeRenderPassRegistry.Registration> registrations;
    private readonly Dictionary<GpuRenderPassId, NativeRenderPassRegistry.IPass> passes;
    private readonly SemaphoreSlim work = new(1, 1);
    private readonly object sync = new();
    private bool closing;
    private bool disposed;
    internal NativeRenderRuntime(NativePassServices services,
        Dictionary<GpuRenderPassId, NativeRenderPassRegistry.Registration> registrations,
        Dictionary<GpuRenderPassId, NativeRenderPassRegistry.IPass> passes)
    {
        this.services = services; this.registrations = registrations; this.passes = passes;
        NativeResources = new(Id, services.Resources, sync, work);
    }
    public Guid Id { get; } = Guid.NewGuid();
    public NativeGraphResources NativeResources { get; }
    public IGpuGraphResources Resources => NativeResources;
    public void StopAccepting()
    { lock (sync) { closing = true; _ = NativeResources.CloseAsync(); } }
    public ValueTask<GpuRenderGraphExecution> SubmitAsync(GpuRenderGraphPlan plan,
        GpuRenderGraphBindings? bindings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        GpuRenderGraphBindings values = plan.ValidateBindings(bindings);
        List<IDisposable> acquired = [];
        IDisposable operation;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(closing || disposed, this);
            foreach (GpuRenderGraphPass pass in plan.Passes)
            {
                if (!registrations.TryGetValue(pass.Id, out NativeRenderPassRegistry.Registration? registration)
                    || pass.RequestType != registration.RequestType || pass.ResultType != registration.ResultType)
                { throw new NotSupportedException($"No matching Native implementation is registered for pass '{pass.Name}' ({pass.Id})."); }
            }
            operation = NativeResources.BeginOperation();
            try
            {
                foreach (GpuRenderGraphResource resource in plan.Resources)
                { if (values.ResolveResource(resource) is { } reference) { acquired.Add(NativeResources.AcquireUse(reference)); } }
            }
            catch { foreach (IDisposable lease in acquired) { lease.Dispose(); } operation.Dispose(); throw; }
        }
        return SubmitCoreAsync(plan, values, acquired, operation, cancellationToken);
    }
    private async ValueTask<GpuRenderGraphExecution> SubmitCoreAsync(GpuRenderGraphPlan plan,
        GpuRenderGraphBindings bindings, List<IDisposable> acquired, IDisposable operation, CancellationToken cancellationToken)
    {
        bool entered = false;
        GpuResourceScope? transient = null;
        GpuResourceBatch? batch = null;
        NativeExecutionBuild build = new(services, bindings);
        List<IDisposable> exportPins = [];
        bool batchOwnsScope = false;
        Exception? failure = null;
        try
        {
            await work.WaitAsync(cancellationToken).ConfigureAwait(false); entered = true;
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                foreach (GpuRenderGraphResource resource in plan.Resources)
                {
                    GpuGraphResourceRef? reference = bindings.ResolveResource(resource);
                    if (resource is GpuRenderGraphTexture texture)
                    { build.Resources.Add(resource, new NativePassTexture(resource.Name,
                        NativeGraphResources.Describe(texture.Description, plan.Exports.Contains(resource) ? NativeGpuTextureUsage.CopySource : NativeGpuTextureUsage.None),
                        reference is null ? null : NativeResources.GetNativeTexture((GpuGraphTextureRef)reference))); }
                    else if (resource is GpuRenderGraphBuffer buffer)
                    { build.Resources.Add(resource, new NativePassBuffer(resource.Name, new(buffer.Description.Size), reference is null ? null : NativeResources.GetNativeBuffer((GpuGraphBufferRef)reference))); }
                }
            }
            foreach (GpuRenderGraphPass pass in plan.Passes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NativePassBuildContext context = new(build, pass);
                try { await passes[pass.Id].BuildAsync(context, pass.Request!, pass.Result!, cancellationToken).ConfigureAwait(false); }
                finally { context.Close(); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                transient = services.Resources.CreateScope(); build.Allocate(transient);
                batch = services.Resources.BeginBatch(); batch.Own(transient); batchOwnsScope = true;
                foreach (NativePassResource resource in build.AllResources) { batch.Use(resource.Reference); }
                while (acquired.Count != 0) { batch.Retain(acquired[0]); acquired.RemoveAt(0); }
                while (build.Leases.Count != 0) { batch.Retain(build.Leases[0]); build.Leases.RemoveAt(0); }
                Dictionary<GpuRenderGraphResource, GpuGraphResourceRef> exports = [];
                foreach (GpuRenderGraphResource resource in plan.Exports)
                {
                    GpuResourceRef reference = build.Resources[resource].Reference;
                    exportPins.Add(services.Resources.Pin(reference));
                    exports.Add(resource, NativeResources.Wrap(reference, resource));
                }
                NativeGpuCommandBuffer commands = batch.StartCommandRecording(); build.Record(commands);
                GpuSubmissionToken token;
                try { token = batch.Submit(); }
                catch (GpuSubmissionException error)
                {
                    GpuSubmissionToken uncertain = error.Completion;
                    throw new GpuRenderGraphSubmissionException(new(() => uncertain.IsComplete,
                        cancellation => new(uncertain.WaitAsync(cancellation))), error);
                }
                batch.Dispose(); batch = null;
                var completion = new GpuGraphCompletion(() => token.IsComplete, cancellation => new(token.WaitAsync(cancellation)));
                GpuRenderGraphExecution result = new(completion, exports, NativeResources.Own(new LeaseSet(exportPins.ToArray())));
                exportPins.Clear(); return result;
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try
            {
                lock (sync)
                {
                    batch?.Dispose();
                    if (!batchOwnsScope) { transient?.Dispose(); }
                    foreach (IDisposable lease in exportPins) { lease.Dispose(); }
                    foreach (IDisposable lease in acquired) { lease.Dispose(); }
                    foreach (IDisposable lease in build.Leases) { lease.Dispose(); }
                    if (!disposed && failure is null) { services.Resources.Collect(); }
                }
            }
            finally { if (entered) { work.Release(); } operation.Dispose(); }
        }
    }
    public async ValueTask WaitIdleAsync(CancellationToken cancellationToken = default)
    {
        await work.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await services.Resources.WaitIdleAsync(cancellationToken).ConfigureAwait(false); }
        finally { work.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        lock (sync) { if (disposed) { return; } }
        StopAccepting();
        await NativeResources.CloseAsync().ConfigureAwait(false);
        await work.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) { return; }
            await services.Resources.WaitIdleAsync().ConfigureAwait(false);
            foreach (NativeRenderPassRegistry.IPass pass in passes.Values.Reverse()) { await pass.DisposeAsync().ConfigureAwait(false); }
            await services.Resources.DisposeAsync().ConfigureAwait(false);
            services.Backend.Dispose(); disposed = true;
        }
        finally { work.Release(); }
    }
    private sealed class LeaseSet(IDisposable[] leases) : IDisposable
    { public void Dispose() { foreach (IDisposable lease in leases) { lease.Dispose(); } } }
}
