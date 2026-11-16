using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Automation.Benchmark;

/// <summary>CPU semantic capture, query and ordinary input; no OS events or rendering.</summary>
[MemoryDiagnoser]
public class AutomationBenchmarks
{
    private AutomationFixture _fixture = null!;

    /// <summary>Gets or sets the actual number of ordinary controls.</summary>
    [Params(100, 1000)]
    public int Elements { get; set; }

    /// <summary>Constructs and lays out a reusable control tree.</summary>
    [GlobalSetup]
    public void Setup() => _fixture = new(Elements);

    /// <summary>Releases the host and its owned surface.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    /// <summary>Copies the current public semantic tree.</summary>
    /// <returns>The detached snapshot.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison")]
    public UiAutomationSnapshot UiSurfaceCaptureAutomationSnapshot() => _fixture.Surface.CaptureAutomationSnapshot();

    /// <summary>Resolves scoped role/name filtering against a fresh snapshot.</summary>
    /// <returns>The actual match count.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison")]
    public Task<int> UiLocatorScopedRoleFilter() => _fixture.Page.GetByTestId("controls").GetByRole(UiRole.Button, "Primary").CountAsync();

    /// <summary>Waits for stable geometry and dispatches move/down/up through a lease.</summary>
    /// <returns>The complete action including reservation cleanup.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison")]
    public Task UiLocatorClick() => _fixture.Page.GetByTestId("control-0").ClickAsync();

    /// <summary>Alternates focus between two real controls using ordinary Tab input.</summary>
    /// <returns>The completed focus action.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison")]
    public Task UiLocatorFocus() => _fixture.Page.GetByTestId(_fixture.NextTarget()).FocusAsync();

    /// <summary>Activates alternating targets using ordinary focus and Enter transitions.</summary>
    /// <returns>The completed key action.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison")]
    public Task UiLocatorPress() => _fixture.Page.GetByTestId(_fixture.NextTarget()).PressAsync(Key.Enter);
}
