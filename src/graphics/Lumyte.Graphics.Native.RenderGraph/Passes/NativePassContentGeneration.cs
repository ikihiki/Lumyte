using Lumyte.Graphics.Native.Resources;

namespace Lumyte.Graphics.Native.RenderGraph;

/// <summary>An opaque immutable GPU content generation owned by one runtime.</summary>
public sealed class NativePassContentGeneration<TContent> : IDisposable
{
    internal NativePassContentGeneration(NativeContentEntry entry) => Entry = entry;
    internal NativeContentEntry Entry { get; }
    public void Dispose() => Entry.DisposeOwner();
}

internal sealed class NativeContentStore(GpuResourceManager resources, object sync)
{
    internal object Sync { get; } = sync;
    internal GpuResourceManager Resources { get; } = resources;
    private readonly HashSet<NativeContentEntry> entries = [];
    private readonly Queue<IDisposable> pendingReleases = [];
    private readonly List<(IDisposable Lease, Exception Error)> failedReleases = [];
    internal NativeContentEntry Create(object? content, IDisposable lease, NativeExecutionBuild? origin, NativePassBuilder[] writers)
    {
        lock (Sync)
        {
            if (entries.Any(entry => entry.Owns(lease))) { throw new ArgumentException("This runtime already owns the content lease.", nameof(lease)); }
            NativeContentEntry entry = new(this, content, lease, origin, writers); entries.Add(entry); return entry;
        }
    }
    internal void Forget(NativeContentEntry entry) { lock (Sync) { entries.Remove(entry); } }
    internal void DisposeOwners() { lock (Sync) { foreach (NativeContentEntry entry in entries.ToArray()) { entry.DisposeOwner(); } } }
    internal void Return(IDisposable lease) { lock (Sync) { pendingReleases.Enqueue(lease); } }
    internal void Drain()
    {
        lock (Sync)
        {
            while (pendingReleases.TryDequeue(out IDisposable? lease))
            { try { lease.Dispose(); } catch (Exception error) { failedReleases.Add((lease, error)); } }
            if (failedReleases.Count != 0)
            { throw new AggregateException("Content lease cleanup failed; unresolved ownership is quarantined.", failedReleases.Select(failure => failure.Error)); }
        }
    }
}

internal sealed class NativeContentEntry(NativeContentStore store, object? content, IDisposable lease,
    NativeExecutionBuild? origin, NativePassBuilder[] writers)
{
    internal NativeContentStore Store { get; } = store;
    internal NativeExecutionBuild? Origin { get; private set; } = origin;
    internal NativePassBuilder[] Writers { get; private set; } = writers;
    private readonly TaskCompletionSource result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Result => result.Task;
    private IDisposable? ownerLease = lease;
    private int references = 1;
    private bool ownerClosed;
    private bool invalid;
    internal bool Accepted { get; private set; }
    private Task[] dependencies = [];
    internal bool Owns(IDisposable value) => ReferenceEquals(ownerLease, value);
    internal bool TryAcquire(NativeExecutionBuild build, out object? value, out IDisposable? hold)
    {
        lock (Store.Sync)
        {
            value = null; hold = null;
            CheckFailure();
            if (ownerClosed || invalid || (!Accepted && !ReferenceEquals(Origin, build))) { return false; }
            value = content; hold = Acquire(); return true;
        }
    }
    internal IDisposable Acquire()
    { lock (Store.Sync) { references++; return new Hold(this); } }
    internal void Accept(GpuSubmissionToken token, Task[] dependencies)
    {
        lock (Store.Sync)
        {
            if (invalid) { return; }
            this.dependencies = dependencies.Append(token.WaitAsync()).ToArray();
            Accepted = true; Origin = null; Writers = [];
        }
        _ = ObserveAsync();
    }
    internal void CheckFailure()
    {
        lock (Store.Sync)
        {
            foreach (Task dependency in dependencies)
            {
                if (!dependency.IsFaulted && !dependency.IsCanceled) { continue; }
                try { dependency.GetAwaiter().GetResult(); }
                catch (Exception error) { Invalidate(error); }
                return;
            }
        }
    }
    private async Task ObserveAsync()
    {
        try
        {
            var pending = dependencies.ToList();
            while (pending.Count != 0)
            {
                Task completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed); await completed.ConfigureAwait(false);
            }
            result.TrySetResult();
            lock (Store.Sync) { dependencies = []; }
        }
        catch (Exception error) { Invalidate(error); }
    }
    internal void Invalidate(Exception error)
    {
        lock (Store.Sync)
        {
            if (invalid) { return; }
            invalid = true; Origin = null; Writers = []; dependencies = [];
            result.TrySetException(error); _ = result.Task.Exception;
            DisposeOwner();
        }
    }
    internal void Abandon()
    { lock (Store.Sync) { ownerLease = null; ownerClosed = true; Store.Forget(this); } }
    internal void DisposeOwner()
    { lock (Store.Sync) { if (ownerClosed) { return; } ownerClosed = true; Release(); } }
    private void Release()
    {
        lock (Store.Sync)
        {
            if (--references != 0) { return; }
            if (ownerLease is { } lease) { Store.Return(lease); }
            ownerLease = null; Store.Forget(this);
        }
    }
    private sealed class Hold(NativeContentEntry owner) : IDisposable
    {
        private NativeContentEntry? current = owner;
        public void Dispose() => Interlocked.Exchange(ref current, null)?.Release();
    }
}
