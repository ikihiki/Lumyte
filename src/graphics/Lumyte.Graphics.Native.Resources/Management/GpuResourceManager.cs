using System.Runtime.ExceptionServices;

namespace Lumyte.Graphics.Native.Resources;

/// <summary>Owns managed Native resources and explicit dependencies; the backend is borrowed.</summary>
/// <remarks>Callers serialize operations and retain scopes/pins/uses through all external raw uses.</remarks>
public sealed partial class GpuResourceManager : IAsyncDisposable
{
    internal readonly INativeGpuBackend Backend;
    private readonly GpuResourceManagerOptions options;
    internal readonly HashSet<GpuResourceScope> Scopes = [];
    internal readonly HashSet<GpuResourceBatch> Batches = [];
    internal int ExternalUses;
    private readonly HashSet<ResourceRecord> records = [];
    private readonly List<Submission> submissions = [];
    private readonly List<Exception> failures = [];
    private bool closing;
    private bool disposed;
    private readonly Dictionary<string, GpuMemoryArena> arenas = [];
    private readonly Dictionary<NativeGpuHeap, int> heaps = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GpuBufferDescription, NativeGpuMemoryRequirements> bufferRequirements = [];
    private readonly Dictionary<(NativeGpuTextureDescription, NativeGpuMemoryKind), NativeGpuMemoryRequirements> textureRequirements = [];
    private readonly Dictionary<NativeGpuQueue, QueueTimeline> timelines = new(ReferenceEqualityComparer.Instance);

    public GpuResourceManager(INativeGpuBackend backend, GpuResourceManagerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        Backend = backend;
        this.options = options ?? new();
        if (this.options.BlockSize == 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
        if (this.options.ResourceDescriptorCapacity == 0 || this.options.SamplerDescriptorCapacity == 0)
        { throw new ArgumentOutOfRangeException(nameof(options)); }
    }
    internal void RequireOpen()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (closing) { throw new InvalidOperationException("The resource manager is closing."); }
    }
    internal ResourceRecord Require(GpuResourceRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ObjectDisposedException.ThrowIf(disposed, this);
        ResourceRecord record = reference.Record;
        if (!ReferenceEquals(record.Owner, this)) { throw new ArgumentException("The reference belongs to another manager.", nameof(reference)); }
        if (!record.Alive) { throw new InvalidOperationException("The resource generation is no longer available."); }
        return record;
    }
    internal Submission Require(GpuSubmissionToken token)
    {
        Submission submission = token.RequireSubmission();
        if (!ReferenceEquals(submission.Owner, this)) { throw new ArgumentException("The token belongs to another manager.", nameof(token)); }
        return submission;
    }
    internal ResourceRecord Register(Action destroy, params ResourceRecord[] dependencies)
    {
        ResourceRecord record = new(this, destroy);
        foreach (ResourceRecord dependency in dependencies) { record.Dependencies.Add(dependency); }
        records.Add(record);
        foreach (ResourceRecord dependency in record.Dependencies) { dependency.Holds++; }
        return record;
    }
    internal void AddDependency(ResourceRecord record, ResourceRecord dependency)
    {
        if (ReferenceEquals(record, dependency) || Reaches(dependency, record, []))
        { throw new ArgumentException("Resource dependencies cannot contain a cycle."); }
        if (record.Dependencies.Add(dependency)) { dependency.Holds++; }
    }
    private static bool Reaches(ResourceRecord start, ResourceRecord target, HashSet<ResourceRecord> visited)
        => ReferenceEquals(start, target) || visited.Add(start) && start.Dependencies.Any(child => Reaches(child, target, visited));
    public GpuResourceScope CreateScope()
    { RequireOpen(); GpuResourceScope scope = new(this); Scopes.Add(scope); return scope; }
    public GpuResourcePin Pin(GpuResourceRef reference) => new(AcquireUse(reference));
    public GpuResourceUse AcquireUse(GpuResourceRef reference)
    { RequireOpen(); return new(this, Require(reference)); }
    public GpuResourceBatch BeginBatch() => BeginBatch(Backend.MainQueue);
    internal GpuResourceBatch BeginBatch(NativeGpuQueue queue)
    { RequireOpen(); GpuResourceBatch batch = new(this, queue); Batches.Add(batch); return batch; }
    public NativeGpuRange GetBufferRange(GpuBufferRef reference, ulong offset = 0, ulong? length = null)
    {
        NativeGpuRange range = Require(reference).Buffer!.Value;
        if (offset > range.Size) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        return range.Slice(offset, length ?? range.Size - offset);
    }
    public ulong GetGpuAddress(GpuBufferRef reference) => GetBufferRange(reference).GpuAddress;
    public NativeGpuTextureHandle GetTextureHandle(GpuTextureRef reference) => Require(reference).Texture!;
    public NativeGpuTextureView GetTextureView(GpuViewRef reference) => Require(reference).TextureView
        ?? throw new ArgumentException("This is a buffer view.", nameof(reference));
    public NativeGpuTextureView GetTextureView(GpuTextureRef reference) => Require(reference).TextureView
        ?? throw new ArgumentException("No default view was requested.", nameof(reference));
    public uint GetShaderIndex(GpuViewRef reference) => Require(reference).ShaderIndex
        ?? throw new ArgumentException("This is an attachment view.", nameof(reference));
    public uint GetShaderIndex(GpuSamplerRef reference) => Require(reference).ShaderIndex!.Value;
    public NativeGpuRenderViewHandle GetRenderViewHandle(GpuViewRef reference) => Require(reference).RenderView
        ?? throw new ArgumentException("This is a shader view.", nameof(reference));
    public GpuResourceStatistics Statistics => new(records.Count, records.Count(record => record.Holds != 0),
        submissions.Count, heaps.Keys.Aggregate(0ul, (sum, heap) => checked(sum + heap.Size)), resourceSlots.Used, samplerSlots.Used,
        textureViews.Count + bufferViews.Count, samplers.Count);

    public void Collect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (Submission submission in submissions.ToArray())
        {
            if (!submission.CanRelease) { continue; }
            if (submission.ReleaseFailure is not null) { continue; }
            try { submission.Release(); submissions.Remove(submission); }
            catch (Exception error) { failures.Add(error); }
        }
        bool changed;
        do
        {
            changed = false;
            foreach (ResourceRecord record in records.ToArray())
            {
                if (!record.Alive || record.Holds != 0) { continue; }
                record.Alive = false;
                try
                {
                    record.Destroy();
                    foreach (ResourceRecord dependency in record.Dependencies) { dependency.Holds--; }
                    records.Remove(record);
                    changed = true;
                }
                catch (Exception error) { record.Failure = error; failures.Add(error); }
            }
        } while (changed);
        ThrowFailures();
    }
    private void ThrowFailures()
    {
        if (failures.Count == 1) { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1) { throw new AggregateException("Resource cleanup failed; unresolved ownership is retained.", failures); }
    }
    public void Trim()
    {
        RequireOpen(); Collect();
        List<Exception> errors = [];
        foreach (GpuMemoryArena arena in arenas.Values)
        { try { arena.Trim(); } catch (Exception error) { errors.Add(error); } }
        if (errors.Count != 0) { failures.AddRange(errors); ThrowFailures(); }
        foreach (NativeGpuHeap heap in heaps.Where(entry => entry.Value == 0).Select(entry => entry.Key).ToArray()) { heaps.Remove(heap); }
    }
    public async Task WaitIdleAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        List<Task> pending = submissions.Select(submission => submission.Succeeded).ToList();
        Exception? primary = null;
        try
        {
            while (pending.Count != 0)
            {
                Task completed = await Task.WhenAny(pending).WaitAsync(cancellationToken);
                pending.Remove(completed);
                await completed;
            }
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try { Collect(); }
            catch (Exception cleanup) when (primary is not null)
            { throw new AggregateException("GPU observation and independent cleanup failed.", primary, cleanup); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) { return; }
        if (Scopes.Any(scope => !scope.Owned) || ExternalUses != 0 || Batches.Count != 0)
        { throw new InvalidOperationException("Dispose every external scope, pin, use and batch before closing the manager."); }
        closing = true;
        await WaitIdleAsync();
        Collect();
        if (records.Count != 0) { throw new InvalidOperationException("Unresolved resource ownership remains."); }
        List<Exception> errors = [];
        foreach (GpuMemoryArena arena in arenas.Values) { try { arena.Dispose(); } catch (Exception error) { errors.Add(error); } }
        foreach (QueueTimeline timeline in timelines.Values) { try { timeline.Semaphore.Dispose(); } catch (Exception error) { errors.Add(error); } }
        try { ReleaseDescriptorHeaps(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) { failures.AddRange(errors); ThrowFailures(); }
        heaps.Clear(); arenas.Clear(); timelines.Clear();
        disposed = true;
    }
    private GpuMemorySlice Allocate(string purpose, NativeGpuMemoryRequirements requirements, NativeGpuMemoryKind kind, ulong alignment)
    {
        if (!arenas.TryGetValue(purpose, out GpuMemoryArena? arena)) { arenas.Add(purpose, arena = new(Backend, options.BlockSize)); }
        GpuMemorySlice slice = arena.Allocate(requirements.Size, alignment, kind, [requirements.Compatibility]);
        heaps.TryGetValue(slice.Heap, out int count); heaps[slice.Heap] = count + 1;
        return slice;
    }
    private void Release(string purpose, GpuMemorySlice slice)
    { arenas[purpose].Release(slice); heaps[slice.Heap]--; }
    internal static ulong Align(ulong value, ulong alignment)
    {
        if (alignment == 0) { throw new ArgumentOutOfRangeException(nameof(alignment)); }
        ulong remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }
    internal static ulong CommonAlignment(ulong left, ulong right)
    {
        if (left == 0 || right == 0) { throw new ArgumentOutOfRangeException(nameof(right)); }
        ulong a = left, b = right;
        while (b != 0) { (a, b) = (b, a % b); }
        return checked(left / a * right);
    }
}
