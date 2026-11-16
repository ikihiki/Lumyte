using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Application.Navigation.Benchmark;

/// <summary>Motion admission policies run through actual public application navigation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class UiPageTransitions
{
    private PageApplicationSession _session = null!;

    /// <summary>Gets or sets a value indicating whether motion policy disables effects before headless capture admission.</summary>
    [Params(false, true)]
    public bool DisableMotion { get; set; }

    /// <summary>Builds the matching application.</summary>
    [GlobalSetup]
    public void Setup() => _session = PageApplicationSession.CreateAsync(true, DisableMotion).GetAwaiter().GetResult();

    /// <summary>Releases its scopes and content.</summary>
    [GlobalCleanup]
    public void Cleanup() => _session.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>Runs transition admission; headless mode cannot capture GPU transition layers.</summary>
    /// <returns>The visual result code.</returns>
    [Benchmark]
    public Task<string> AdmissionAndRetirementAsync() => _session.TransitionAsync();
}
