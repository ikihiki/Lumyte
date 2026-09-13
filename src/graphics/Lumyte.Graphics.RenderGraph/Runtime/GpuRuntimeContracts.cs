using System.Collections.ObjectModel;

namespace Lumyte.Graphics.RenderGraph;

public sealed record GpuRenderRuntimeOptions
{
    public string ProviderId { get; init; } = "Auto";
    public IReadOnlyList<GpuRenderPassId> RequiredPasses { get; init; } = [];
    public bool EnableValidation { get; init; }
}

public interface IGpuRenderProvider
{
    string Id { get; }
    int ContractVersion { get; }
    ValueTask<IGpuRenderRuntime> CreateAsync(GpuRenderRuntimeOptions options, CancellationToken cancellationToken = default);
}

public interface IGpuRenderRuntime : IAsyncDisposable
{
    Guid Id { get; }
    IGpuGraphResources Resources { get; }
    ValueTask<GpuRenderGraphExecution> SubmitAsync(GpuRenderGraphPlan plan,
        GpuRenderGraphBindings? bindings = null, CancellationToken cancellationToken = default);
    /// <summary>Close new work and resource acquisition; existing owners can still release their holds.</summary>
    void StopAccepting();
    ValueTask WaitIdleAsync(CancellationToken cancellationToken = default);
}

public sealed class GpuRenderProviderRegistry
{
    private readonly object gate = new();
    private readonly List<IGpuRenderProvider> providers = [];
    public void Register(IGpuRenderProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (gate)
        {
            if (provider.ContractVersion != 1)
            {
                throw new NotSupportedException($"Provider '{provider.Id}' uses unsupported contract version {provider.ContractVersion}.");
            }
            if (providers.Any(existing => existing.Id == provider.Id))
            {
                throw new ArgumentException($"Provider '{provider.Id}' is already registered.", nameof(provider));
            }
            providers.Add(provider);
        }
    }

    public ValueTask<IGpuRenderRuntime> CreateAsync(GpuRenderRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        IGpuRenderProvider selected;
        lock (gate)
        {
            selected = (options.ProviderId == "Auto" ? providers.FirstOrDefault() : providers.Find(p => p.Id == options.ProviderId))
                ?? throw new NotSupportedException($"No render provider is registered for '{options.ProviderId}'.");
        }
        return selected.CreateAsync(options with { RequiredPasses = Array.AsReadOnly(options.RequiredPasses.ToArray()) }, cancellationToken);
    }
}

/// <summary>Separates GPU use completion from successful diagnostics; cancellation only cancels the observer.</summary>
public sealed class GpuGraphCompletion
{
    // A broken retirement callback cannot make accepted GPU work safe to release.
    // Keep failed handoffs rooted until an explicit observation proves use has ended.
    private static readonly object quarantineGate = new();
    private static readonly Dictionary<GpuGraphCompletion, List<IDisposable>> quarantine = [];
    private readonly Func<bool> isComplete;
    private readonly Func<CancellationToken, ValueTask> wait;
    private readonly Action<IDisposable>? retainUntilUseEnds;
    private int succeeded;
    public GpuGraphCompletion(Func<bool> isComplete, Func<CancellationToken, ValueTask> wait,
        Action<IDisposable>? retainUntilUseEnds = null)
    {
        this.isComplete = isComplete ?? throw new ArgumentNullException(nameof(isComplete));
        this.wait = wait ?? throw new ArgumentNullException(nameof(wait));
        this.retainUntilUseEnds = retainUntilUseEnds;
    }
    public bool IsComplete
    {
        get
        {
            if (!isComplete()) { return false; }
            lock (quarantineGate)
            {
                if (quarantine.TryGetValue(this, out var leases))
                {
                    while (leases.Count != 0) { leases[^1].Dispose(); leases.RemoveAt(leases.Count - 1); }
                    quarantine.Remove(this);
                }
            }
            return true;
        }
    }
    internal bool Succeeded => Volatile.Read(ref succeeded) != 0;
    /// <summary>Consumes a lease, including if the provider's registration throws. Failed handoffs remain retained until IsComplete or WaitAsync observes GPU use termination.</summary>
    public void RetainUntilUseEnds(IDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var transferred = new CompletionLease(lease);
        try
        {
            if (IsComplete) { transferred.Dispose(); }
            else if (retainUntilUseEnds is not null) { retainUntilUseEnds(transferred); }
            else { throw new NotSupportedException("This completion does not provide GPU retirement for external leases."); }
        }
        catch
        {
            lock (quarantineGate)
            {
                if (!quarantine.TryGetValue(this, out var leases)) { quarantine.Add(this, leases = []); }
                leases.Add(transferred);
            }
            throw;
        }
    }
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await wait(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref succeeded, 1);
        }
        finally
        {
            bool pending;
            lock (quarantineGate) { pending = quarantine.ContainsKey(this); }
            if (pending)
            {
                try { _ = IsComplete; }
                catch { /* Failed observation leaves ownership quarantined and preserves the original diagnostics. */ }
            }
        }
    }
    private sealed class CompletionLease(IDisposable lease) : IDisposable
    {
        private IDisposable? value = lease;
        public void Dispose() => Interlocked.Exchange(ref value, null)?.Dispose();
    }
}

/// <summary>Queue handoff may have occurred. Retain targets until Completion proves GPU use has ended.</summary>
public sealed class GpuRenderGraphSubmissionException : InvalidOperationException
{
    public GpuRenderGraphSubmissionException(GpuGraphCompletion completion, Exception innerException)
        : base("Render graph submission failed after queue handoff may have occurred.", innerException) => Completion = completion;
    public GpuGraphCompletion Completion { get; }
}

public sealed class GpuRenderGraphExecution : IDisposable
{
    private readonly IReadOnlyDictionary<GpuRenderGraphResource, GpuGraphResourceRef> exports;
    private IDisposable? ownership;
    private readonly object gate = new();
    public GpuRenderGraphExecution(GpuGraphCompletion completion,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuGraphResourceRef> exports, IDisposable ownership)
    {
        Completion = completion;
        this.exports = new ReadOnlyDictionary<GpuRenderGraphResource, GpuGraphResourceRef>(new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>(exports));
        this.ownership = ownership;
    }
    public GpuGraphCompletion Completion { get; }
    public bool IsComplete => Completion.IsComplete;
    internal void RetainOwnership(IDisposable lease)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(ownership is null, this);
            var holds = new GpuUseSet();
            holds.Add(ownership);
            holds.Add(lease);
            ownership = holds;
        }
    }
    public ValueTask WaitForCompletionAsync(CancellationToken cancellationToken = default) => Completion.WaitAsync(cancellationToken);
    public GpuGraphTextureRef GetExportedTexture(GpuRenderGraphTexture resource) => (GpuGraphTextureRef)GetExport(resource);
    public GpuGraphBufferRef GetExportedBuffer(GpuRenderGraphBuffer resource) => (GpuGraphBufferRef)GetExport(resource);
    private GpuGraphResourceRef GetExport(GpuRenderGraphResource resource)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(ownership is null, this);
            if (!Completion.Succeeded)
            {
                throw new InvalidOperationException("Wait for successful completion before retrieving an export.");
            }
            return exports[resource];
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (ownership is null) { return; }
            ownership.Dispose();
            ownership = null;
        }
    }
}
