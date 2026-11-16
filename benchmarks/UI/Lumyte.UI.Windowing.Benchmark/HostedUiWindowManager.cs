using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Windowing.Benchmark;

/// <summary>CPU hosted-manager pipeline; metadata acknowledgements are not OS or GPU presentation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class HostedUiWindowManager
{
    private HostedWindowSession _session = null!;

    /// <summary>Creates the retained root.</summary>
    [GlobalSetup(Targets = new[] { nameof(RootLifecycle), nameof(PopupOpenCloseDrain), nameof(SubmenuOpenCloseDrain) })]
    public void Setup() => _session = new();

    /// <summary>Primes only the retained-input workload.</summary>
    [GlobalSetup(Target = nameof(FocusAndInputRouting))]
    public void SetupInput()
    {
        _session = new();
        _session.InputCycle();
    }

    /// <summary>Drains resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    /// <summary>Creates and drains a fresh root.</summary>
    [Benchmark]
    public void RootLifecycle()
    {
        using var session = new HostedWindowSession();
        session.VerifyRoot();
    }

    /// <summary>Opens, closes and drains a menu.</summary>
    /// <returns>The observed operation count.</returns>
    [Benchmark]
    public int PopupOpenCloseDrain() => _session.PopupCycle(false);

    /// <summary>Opens and drains a two-level menu.</summary>
    /// <returns>The observed operation count.</returns>
    [Benchmark]
    public int SubmenuOpenCloseDrain() => _session.PopupCycle(true);

    /// <summary>Routes keyboard focus and input on a retained menu.</summary>
    /// <returns>The observed operation count.</returns>
    [Benchmark]
    public int FocusAndInputRouting() => _session.InputCycle();
}
