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
    private readonly NativeContentStore content;
    private readonly NativeScheduleCache schedules = new();
    private readonly List<Task> results = [];
    private bool closing;
    private bool disposed;
    internal NativeRenderRuntime(NativePassServices services,
        Dictionary<GpuRenderPassId, NativeRenderPassRegistry.Registration> registrations,
        Dictionary<GpuRenderPassId, NativeRenderPassRegistry.IPass> passes)
    {
        this.services = services; this.registrations = registrations; this.passes = passes;
        NativeResources = new(Id, services.Resources, sync, work);
        content = new(services.Resources, sync);
        NativeResources.DrainRetained = content.Drain;
    }
    public Guid Id { get; } = Guid.NewGuid();
    public NativeGraphResources NativeResources { get; }
    public IGpuGraphResources Resources => NativeResources;
    public NativeRenderPreparationStatistics PreparationStatistics { get { lock (sync) { return schedules.Statistics; } } }
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
        NativeExecutionBuild build = new(services, bindings, plan, content, schedules);
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
                    { build.Resources.Add(resource, new NativePassTexture(build, resource.Name,
                        NativeGraphResources.Describe(texture.Description, plan.Exports.Contains(resource) ? NativeGpuTextureUsage.CopySource : NativeGpuTextureUsage.None),
                        reference is null ? null : NativeResources.GetNativeTexture((GpuGraphTextureRef)reference))); }
                    else if (resource is GpuRenderGraphBuffer buffer)
                    { build.Resources.Add(resource, new NativePassBuffer(build, resource.Name, new(buffer.Description.Size), reference is null ? null : NativeResources.GetNativeBuffer((GpuGraphBufferRef)reference))); }
                    if (reference is not null && build.Resources.TryGetValue(resource, out NativePassResource? imported))
                    { build.Imported.Add(imported.Reference, imported); build.Predecessors.Add(NativeResources.GetReadiness(reference)); }
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
                build.Compile(); build.CheckDependencies();
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
                build.CheckDependencies();
                GpuSubmissionToken token;
                try { token = batch.Submit(); }
                catch (GpuSubmissionException error)
                {
                    GpuSubmissionToken uncertain = error.Completion;
                    throw new GpuRenderGraphSubmissionException(new(() => uncertain.IsComplete,
                        cancellation => new(uncertain.WaitAsync(cancellation)),
                        lease => Retain(uncertain, lease)), error);
                }
                build.Accept(token);
                batch.Dispose(); batch = null;
                Task completed = WaitForResultsAsync(build.ResultDependencies.Append(token.WaitAsync()).ToArray());
                results.RemoveAll(static result => result.IsCompletedSuccessfully); results.Add(completed);
                _ = completed.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                var completion = new GpuGraphCompletion(() => token.IsComplete, cancellation => new(completed.WaitAsync(cancellation)),
                    lease => Retain(token, lease));
                GpuRenderGraphExecution result = new(completion, exports, NativeResources.Own(new LeaseSet(exportPins.ToArray())));
                exportPins.Clear(); return result;
            }
        }
        catch (Exception error) { failure = error; build.Reject(error); throw; }
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
                    content.Drain();
                    if (!disposed && failure is null) { NativeResources.Collect(); }
                }
            }
            finally { if (entered) { work.Release(); } operation.Dispose(); }
        }
    }
    public async ValueTask WaitIdleAsync(CancellationToken cancellationToken = default)
    {
        await work.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await services.Resources.WaitIdleAsync(cancellationToken).ConfigureAwait(false);
            NativeResources.Collect();
            await WaitForResultsAsync(results.ToArray()).WaitAsync(cancellationToken).ConfigureAwait(false);
            results.RemoveAll(static result => result.IsCompletedSuccessfully);
        }
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
            Exception? resultFailure = null;
            try { await WaitForResultsAsync(results.ToArray()).ConfigureAwait(false); }
            catch (Exception error) { resultFailure = error; }
            content.DisposeOwners();
            content.Drain();
            schedules.Clear();
            foreach (NativeRenderPassRegistry.IPass pass in passes.Values.Reverse()) { await pass.DisposeAsync().ConfigureAwait(false); }
            content.Drain();
            await services.Resources.DisposeAsync().ConfigureAwait(false);
            services.Backend.Dispose(); disposed = true;
            if (resultFailure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(resultFailure).Throw(); }
        }
        finally { work.Release(); }
    }
    private sealed class LeaseSet(IDisposable[] leases) : IDisposable
    { public void Dispose() { foreach (IDisposable lease in leases) { lease.Dispose(); } } }
    private void Retain(GpuSubmissionToken token, IDisposable lease)
    { lock (sync) { services.Resources.RetainUntilSubmissionEnds(token, lease); } }
    private static async Task WaitForResultsAsync(Task[] results)
    {
        var pending = results.ToList();
        foreach (Task result in results)
        { _ = result.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default); }
        while (pending.Count != 0)
        {
            Task completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed); await completed.ConfigureAwait(false);
        }
    }
}
