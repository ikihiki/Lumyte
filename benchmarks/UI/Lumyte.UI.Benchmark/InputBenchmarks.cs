using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Benchmark;

/// <summary>Prepared input routing; hit-test, capture and focus are separate operations.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class InputBenchmarks
{
    private UiSurface _surface = null!;
    private bool _alternate;

    /// <summary>Gets the number of actual button activations.</summary>
    public int Clicks { get; private set; }

    /// <summary>Gets the prepared public surface.</summary>
    public UiSurface Surface => _surface;

    /// <summary>Mounts sixteen actual focusable buttons and prepares geometry.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _surface = new UiSurface(Stack()[Enumerable.Range(0, 16).Select(_ => Button(width: 40, height: 16, onClick: () => Clicks++)).ToArray()]);
        _surface.SetViewport(new(512, 512));
        _surface.UpdateLayout();
    }

    /// <summary>Hit-tests an uncaptured Down and cancels its capture.</summary>
    /// <returns>Whether Down found a target.</returns>
    [Benchmark]
    public bool HitTest()
    {
        _alternate = !_alternate;
        bool handled = _surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Down, new(2, _alternate ? 2 : 18)));
        _surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Cancel, new(2, 2)));
        return handled;
    }

    /// <summary>Includes Down, captured move and Up, completing a real activation.</summary>
    /// <returns>Whether the captured move was routed.</returns>
    [Benchmark]
    public bool CaptureClick()
    {
        _surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Down, new(2, 2)));
        bool handled = _surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Move, new(3, 3)));
        _surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Up, new(3, 3)));
        return handled;
    }

    /// <summary>Advances actual focus and releases the Tab key.</summary>
    /// <returns>The newly focused element.</returns>
    [Benchmark]
    public UiElement? Focus()
    {
        _surface.Dispatch(new UiKeyInput(Key.Tab, true));
        _surface.Dispatch(new UiKeyInput(Key.Tab, false));
        return _surface.FocusedElement;
    }

    /// <summary>Disposes routing and tree state.</summary>
    [GlobalCleanup]
    public void Cleanup() => _surface.Dispose();
}
