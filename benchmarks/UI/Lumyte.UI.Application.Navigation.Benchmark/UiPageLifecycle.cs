using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Application.Navigation.Benchmark;

/// <summary>CPU-only headless application, real DI scopes and public content advancement.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class UiPageLifecycle
{
    private PageApplicationSession _session = null!;

    /// <summary>Initializes a retained running application.</summary>
    [GlobalSetup]
    public void Setup() => _session = PageApplicationSession.CreateAsync(false).GetAwaiter().GetResult();

    /// <summary>Drains application shutdown.</summary>
    [GlobalCleanup]
    public void Cleanup() => _session.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>Includes initial DI construction, publication and shutdown.</summary>
    /// <returns>The asynchronous lifecycle.</returns>
    [Benchmark]
    public async Task InitialLifecycleAsync()
    {
        await using PageApplicationSession session = await PageApplicationSession.CreateAsync(false);
        await session.StableFrameAsync();
    }

    /// <summary>Alternates finite registered pages and drains logical and visual completion separately.</summary>
    /// <returns>The page build count.</returns>
    [Benchmark]
    public Task<int> ReplaceAndRetireAsync() => _session.ReplaceAsync();

    /// <summary>Mutates real retained UI and waits for an observed next frame.</summary>
    /// <returns>The public advance count after the frame.</returns>
    [Benchmark]
    public Task<int> MutationFrameAsync() => _session.MutateAsync();

    /// <summary>Checks a stable retained frame without changing the model.</summary>
    /// <returns>The observed content advance count.</returns>
    [Benchmark]
    public Task<int> StableFrameAsync() => _session.StableFrameAsync();
}
