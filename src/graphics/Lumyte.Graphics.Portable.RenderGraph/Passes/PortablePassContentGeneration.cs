using Lumyte.Graphics.Portable.Resources;

namespace Lumyte.Graphics.Portable.RenderGraph;

/// <summary>Opaque ownership of immutable GPU content. Resolve through a current build; disposal ends future acquisitions.</summary>
public sealed class PortablePassContentGeneration<TContent> : IDisposable
{
    internal PortablePassContentGeneration(PortableContentState state, TContent content) { State = state; Content = content; }
    internal PortableContentState State { get; }
    internal TContent Content { get; }
    public void Dispose() => State.ReleaseOwner();
}

internal sealed class PortableContentState
{
    private readonly IDisposable lease;
    private int holds = 1;
    private bool ownerClosed;
    private bool invalid;
    private readonly List<WeakReference<PortableContentState>> dependents = [];
    private PortableContentState[] dependencies = [];
    internal PortableContentState(PortableExecutionBuild build, IDisposable lease, PortablePassBuilder[] writers)
    { BuildIdentity = build.Identity; Runtime = build.Runtime; Runtime.ClaimContentLease(this, lease); this.lease = lease; Writers = writers; }
    internal PortableRenderRuntime Runtime { get; }
    internal object? BuildIdentity { get; private set; }
    internal PortablePassBuilder[] Writers { get; private set; }
    internal Task? Result { get; private set; }
    internal bool IsIndependent { get; private set; }
    internal bool Failed => invalid || Result is { IsFaulted: true } or { IsCanceled: true } || dependencies.Any(item => item.Failed);
    internal bool CanUse(PortableExecutionBuild build) => !ownerClosed && !Failed &&
        (Result is not null || ReferenceEquals(BuildIdentity, build.Identity));
    internal IDisposable Hold() { holds = checked(holds + 1); return new Use(this); }
    internal void Accept(Task result, bool independent = false, PortableContentState[]? prerequisites = null)
    {
        Result = result; IsIndependent = independent; BuildIdentity = null; Writers = [];
        dependencies = prerequisites ?? [];
        foreach (PortableContentState dependency in dependencies)
        {
            dependency.dependents.RemoveAll(reference => !reference.TryGetTarget(out _));
            dependency.dependents.Add(new(this));
        }
        if (Failed) { Invalidate(); }
        _ = ObserveAsync(result);
    }
    private async Task ObserveAsync(Task result)
    {
        try { await result.ConfigureAwait(false); lock (Runtime.Gate) { dependencies = []; } }
        catch { lock (Runtime.Gate) { Invalidate(); } }
    }
    internal void Invalidate()
    {
        if (invalid) { return; }
        invalid = true; BuildIdentity = null; Writers = [];
        foreach (WeakReference<PortableContentState> reference in dependents)
        { if (reference.TryGetTarget(out PortableContentState? dependent)) { dependent.Invalidate(); } }
        dependents.Clear(); dependencies = []; ReleaseOwner();
    }
    internal void ReleaseOwner()
    {
        lock (Runtime.Gate)
        {
            if (ownerClosed) { return; }
            ownerClosed = true; Runtime.ReleaseContentOwner(this); Release();
        }
    }
    private void Release()
    {
        if (--holds == 0)
        { Runtime.ReturnContentLease(lease); }
    }
    private sealed class Use(PortableContentState state) : IDisposable
    {
        private bool ended;
        public void Dispose()
        {
            lock (state.Runtime.Gate)
            { if (ended) { return; } ended = true; state.Release(); }
        }
    }
}
