using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Navigation.Benchmark;

/// <summary>Real navigator CPU state machine with a surface-owning public host protocol fixture.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class UiNavigator
{
    private NavigationSession _session = null!;

    /// <summary>Creates a warmed initial page.</summary>
    [GlobalSetup]
    public void Setup() => _session = new();

    /// <summary>Releases every activation and surface.</summary>
    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    /// <summary>Pushes typed arguments into bounded history and drains retirement.</summary>
    /// <returns>The observed operation count.</returns>
    [Benchmark]
    public int PushDrain() => _session.Navigate(UiNavigationMode.Push);

    /// <summary>Replaces a page with fresh arguments and drains retirement.</summary>
    /// <returns>The observed operation count.</returns>
    [Benchmark]
    public int ReplaceDrain() => _session.Navigate(UiNavigationMode.Replace);

    /// <summary>Recreates backward and forward activations without accumulating history.</summary>
    /// <returns>The observed operation count.</returns>
    [Benchmark]
    public int BackForwardDrain() => _session.BackForward();

    /// <summary>Stages then rejects a candidate through a real leave guard.</summary>
    /// <returns>The actual navigation status.</returns>
    [Benchmark]
    public UiNavigationStatus GuardRejection() => _session.Reject();

    /// <summary>Cancels an admitted request before preparation and drains its outcome.</summary>
    /// <returns>The actual navigation status.</returns>
    [Benchmark]
    public UiNavigationStatus Cancellation() => _session.Cancel();
}
