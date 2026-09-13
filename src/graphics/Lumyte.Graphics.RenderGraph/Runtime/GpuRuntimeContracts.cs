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
    private readonly Func<bool> isComplete;
    private readonly Func<CancellationToken, ValueTask> wait;
    private int succeeded;
    public GpuGraphCompletion(Func<bool> isComplete, Func<CancellationToken, ValueTask> wait)
    {
        this.isComplete = isComplete ?? throw new ArgumentNullException(nameof(isComplete));
        this.wait = wait ?? throw new ArgumentNullException(nameof(wait));
    }
    public bool IsComplete => isComplete();
    internal bool Succeeded => Volatile.Read(ref succeeded) != 0;
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        await wait(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref succeeded, 1);
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
