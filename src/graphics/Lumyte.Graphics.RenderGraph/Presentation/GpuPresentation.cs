namespace Lumyte.Graphics.RenderGraph;

/// <summary>The presentation connection owns the target token until Present or Discard returns it.</summary>
public sealed class GpuGraphPresentationTarget
{
    public GpuGraphPresentationTarget(GpuGraphTextureRef texture, IDisposable ownership)
    { Texture = texture; Ownership = ownership; }
    public GpuGraphTextureRef Texture { get; }
    public GpuGraphTextureDescription Description => Texture.Description;
    public IDisposable Ownership { get; }
}

public interface IGpuGraphPresentation
{
    ValueTask<GpuGraphPresentationTarget> AcquireNextTargetAsync(CancellationToken cancellationToken = default);
    /// <summary>Consumes an accepted target, including when presentation fails. Completion alone does not end presentation use.</summary>
    void Present(GpuGraphPresentationTarget target, GpuGraphCompletion completion);
    /// <summary>Consumes a possibly submitted target without presenting its contents. Return only after GPU and presentation use have ended.</summary>
    void Retire(GpuGraphPresentationTarget target, GpuGraphCompletion completion);
    void Discard(GpuGraphPresentationTarget target);
}

public sealed class GpuPresentationTargetChangedException : InvalidOperationException
{
    public GpuPresentationTargetChangedException(GpuGraphTextureDescription description)
        : base("The presentation target shape changed. Rebuild the plan using Description.") => Description = description;
    public GpuGraphTextureDescription Description { get; }
}

/// <summary>Borrows runtime and presentation; frame pacing and window/canvas ownership stay with the host.</summary>
public sealed class GpuRenderContext : IDisposable
{
    private readonly IGpuRenderRuntime runtime;
    private readonly IGpuGraphPresentation presentation;
    private readonly object gate = new();
    private readonly HashSet<GpuFrame> frames = [];
    private readonly GpuRenderGraphPlanCache cache;
    private bool disposed;
    public GpuRenderContext(IGpuRenderRuntime runtime, IGpuGraphPresentation presentation, int cacheMaximumEntries = 64)
    { this.runtime = runtime; this.presentation = presentation; cache = new(cacheMaximumEntries); }

    public ValueTask<GpuRenderGraphExecution> SubmitAsync(GpuRenderGraphPlan plan, GpuRenderGraphBindings bindings,
        GpuGraphTextureInput presentationTargetInput, CancellationToken cancellationToken = default)
    {
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); }
        var targetResource = presentationTargetInput.Texture;
        if (!plan.Resources.Contains(targetResource) || !plan.Outputs.Contains(targetResource)
            || !plan.Passes.Any(pass => pass.Uses.Any(use => ReferenceEquals(use.Resource, targetResource) && use.Access != GpuRenderGraphAccess.Read)))
        {
            throw new ArgumentException("Presentation target must be a written output of this plan.", nameof(presentationTargetInput));
        }
        bindings = plan.ValidateBindingsExcept(bindings, targetResource);
        var holds = new GpuUseSet();
        try
        {
            // Synchronous prelude: caller scopes may end as soon as this method returns its awaitable.
            foreach (var resource in plan.Resources)
            {
                if (!ReferenceEquals(resource, targetResource) && bindings.ResolveResource(resource) is { } reference)
                {
                    if (reference.RuntimeId != runtime.Id) { throw new ArgumentException("Resource belongs to another runtime.", nameof(bindings)); }
                    holds.Add(runtime.Resources.AcquireUse(reference));
                }
            }
            return SubmitCoreAsync(plan, bindings, presentationTargetInput, holds, cancellationToken);
        }
        catch { holds.Dispose(); throw; }
    }

    private async ValueTask<GpuRenderGraphExecution> SubmitCoreAsync(GpuRenderGraphPlan plan, GpuRenderGraphBindings bindings,
        GpuGraphTextureInput input, GpuUseSet holds, CancellationToken cancellationToken)
    {
        using (holds)
        {
            var target = await presentation.AcquireNextTargetAsync(cancellationToken).ConfigureAwait(false);
            GpuRenderGraphExecution? execution = null;
            try
            {
                lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); }
                if (target.Description != input.Texture.Description) { throw new GpuPresentationTargetChangedException(target.Description); }
                var builder = bindings.ToBuilder();
                builder.Set(input, target.Texture);
                execution = await runtime.SubmitAsync(plan, builder.Build(), cancellationToken).ConfigureAwait(false);
            }
            catch (GpuRenderGraphSubmissionException error) { presentation.Retire(target, error.Completion); throw; }
            catch { presentation.Discard(target); throw; }
            try { presentation.Present(target, execution.Completion); return execution; }
            catch { execution.Dispose(); throw; }
        }
    }

    public async ValueTask<GpuFrame> BeginFrameAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); }
        var target = await presentation.AcquireNextTargetAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var frame = new GpuFrame(runtime, presentation, target, cache, RemoveFrame);
                frames.Add(frame);
                return frame;
            }
        }
        catch { presentation.Discard(target); throw; }
    }

    private void RemoveFrame(GpuFrame frame) { lock (gate) { frames.Remove(frame); } }
    public void Dispose()
    {
        GpuFrame[] outstanding;
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            outstanding = frames.ToArray();
            cache.Clear();
        }
        List<Exception> errors = [];
        foreach (var frame in outstanding)
        {
            try { frame.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) { throw new AggregateException("One or more frames failed to release.", errors); }
    }
}

public sealed class GpuFrame : IDisposable
{
    private readonly IGpuRenderRuntime runtime;
    private readonly IGpuGraphPresentation presentation;
    private readonly GpuGraphPresentationTarget target;
    private readonly Action<GpuFrame> finished;
    private readonly GpuRenderGraphPlanCache cache;
    private readonly GpuFrameOwnership ownership = new();
    private readonly object gate = new();
    private bool submitting;
    private bool accepted;
    private bool disposed;
    private bool returned;
    internal GpuFrame(IGpuRenderRuntime runtime, IGpuGraphPresentation presentation, GpuGraphPresentationTarget target,
        GpuRenderGraphPlanCache cache, Action<GpuFrame> finished)
    {
        this.runtime = runtime; this.presentation = presentation; this.target = target; this.cache = cache; this.finished = finished;
        TargetResource = Graph.ImportTexture("presentation.target", target.Texture);
    }
    public GpuRenderGraph Graph { get; } = new();
    public GpuRenderGraphTexture TargetResource { get; }

    /// <summary>Transfer an external lease to this frame and its accepted execution, including GPU retirement.</summary>
    public void Retain(IDisposable lease)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (submitting || accepted) { throw new InvalidOperationException("Cannot add ownership after submission starts."); }
            ownership.Add(lease);
        }
    }

    public ValueTask<GpuRenderGraphExecution> SubmitAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (submitting || accepted) { throw new InvalidOperationException("This frame has already been submitted."); }
            Graph.MarkOutput(TargetResource);
            var plan = Graph.Compile(cache);
            submitting = true;
            // Invoke Submit before suspending, preserving the common synchronous retention contract.
            try { return FinishSubmissionAsync(runtime.SubmitAsync(plan, cancellationToken: cancellationToken)); }
            catch (GpuRenderGraphSubmissionException error)
            {
                submitting = false;
                accepted = true;
                returned = true;
                finished(this);
                RetireUnknown(error);
                throw;
            }
            catch { submitting = false; throw; }
        }
    }

    private async ValueTask<GpuRenderGraphExecution> FinishSubmissionAsync(ValueTask<GpuRenderGraphExecution> submission)
    {
        GpuRenderGraphExecution execution;
        try { execution = await submission.ConfigureAwait(false); }
        catch (GpuRenderGraphSubmissionException error)
        {
            lock (gate) { submitting = false; accepted = true; returned = true; }
            finished(this);
            RetireUnknown(error);
            throw;
        }
        catch
        {
            lock (gate) { submitting = false; if (disposed) { ReturnTarget(); } }
            throw;
        }
        lock (gate) { submitting = false; accepted = true; returned = true; }
        finished(this);
        try
        {
            ownership.RetainForGpu(execution.Completion);
            execution.RetainOwnership(ownership);
        }
        catch (Exception error)
        {
            List<Exception> errors = [error];
            try { ownership.Dispose(); } catch (Exception failure) { errors.Add(failure); }
            try { execution.Dispose(); } catch (Exception failure) { errors.Add(failure); }
            try { presentation.Retire(target, execution.Completion); } catch (Exception failure) { errors.Add(failure); }
            throw new GpuRenderGraphSubmissionException(execution.Completion,
                errors.Count == 1 ? error : new AggregateException(errors));
        }
        try { presentation.Present(target, execution.Completion); return execution; }
        catch { execution.Dispose(); throw; }
    }

    public void Dispose()
    {
        try
        {
            lock (gate)
            {
                if (disposed) { return; }
                disposed = true;
                if (!submitting && !accepted) { ReturnTarget(); }
            }
        }
        finally { finished(this); }
    }
    private void ReturnTarget()
    {
        if (returned) { return; }
        returned = true;
        try { presentation.Discard(target); }
        finally { ownership.Dispose(); }
    }
    private void RetireUnknown(GpuRenderGraphSubmissionException error)
    {
        List<Exception> errors = [];
        try { ownership.RetainForGpu(error.Completion); } catch (Exception failure) { errors.Add(failure); }
        try { ownership.Dispose(); } catch (Exception failure) { errors.Add(failure); }
        try { presentation.Retire(target, error.Completion); } catch (Exception failure) { errors.Add(failure); }
        if (errors.Count != 0)
        { throw new GpuRenderGraphSubmissionException(error.Completion, new AggregateException([error, .. errors])); }
    }
}

internal sealed class GpuFrameOwnership : IDisposable
{
    private readonly object gate = new();
    private readonly List<IDisposable> leases = [];
    private int owners = 1;
    private bool disposed;
    internal void Add(IDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (leases.Any(existing => ReferenceEquals(existing, lease)))
            { throw new ArgumentException("The frame already owns this lease.", nameof(lease)); }
            leases.Add(lease);
        }
    }
    internal void RetainForGpu(GpuGraphCompletion completion)
    {
        lock (gate)
        {
            if (leases.Count == 0) { return; }
            owners++;
        }
        var use = new Use(this);
        completion.RetainUntilUseEnds(use);
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            Release();
        }
    }
    private void Release()
    {
        lock (gate)
        {
            if (--owners != 0) { return; }
            List<Exception> errors = [];
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                try { leases[index].Dispose(); }
                catch (Exception error) { errors.Add(error); }
            }
            leases.Clear();
            if (errors.Count != 0) { throw new AggregateException("One or more frame leases failed to release.", errors); }
        }
    }
    private sealed class Use(GpuFrameOwnership owner) : IDisposable
    {
        private GpuFrameOwnership? value = owner;
        public void Dispose() => Interlocked.Exchange(ref value, null)?.Release();
    }
}

internal sealed class GpuUseSet : IDisposable
{
    private readonly List<IDisposable> holds = [];
    public void Add(IDisposable hold) => holds.Add(hold);
    public void Dispose()
    {
        List<Exception> errors = [];
        for (var i = holds.Count - 1; i >= 0; i--)
        {
            try { holds[i].Dispose(); }
            catch (Exception error) { errors.Add(error); }
            holds.RemoveAt(i);
        }
        if (errors.Count != 0) { throw new AggregateException("One or more GPU ownership leases failed to release.", errors); }
    }
}
