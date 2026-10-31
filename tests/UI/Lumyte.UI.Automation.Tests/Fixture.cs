using System.Numerics;
using Lumyte.UI.SampleSupport;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Automation.Tests;

internal sealed class Fixture : IAsyncDisposable
{
    internal Fixture(Widget root)
    {
        Surface = new(root);
        Surface.SetViewport(new(100, 80));
        Surface.UpdateLayout();
        Host = new(Surface, NextFrameAsync, Clock);
        Page = new(Host, new() { Timeout = TimeSpan.FromSeconds(1) });
    }

    internal ManualClock Clock { get; } = new();

    internal UiSurface Surface { get; }

    internal UiSampleAutomationHost Host { get; }

    internal UiAutomationPage Page { get; }

    internal int Frames { get; private set; }

    internal Action? OnFrame { get; set; }

    internal Task? WaitFrame { get; set; }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        Surface.Dispose();
    }

    private async ValueTask NextFrameAsync(CancellationToken cancellationToken)
    {
        if (WaitFrame is not null)
        {
            await WaitFrame.WaitAsync(cancellationToken);
        }

        Clock.Advance(TimeSpan.FromMilliseconds(10));
        Frames++;
        OnFrame?.Invoke();
        Surface.UpdateLayout();
    }

    internal sealed class RuntimeButton : Definitions.Button
    {
        internal void Change(Action<RuntimeButton> change)
        {
            change(this);
            InvalidateLayout();
            InvalidateInput();
        }
    }

    internal sealed class OffsetPanel : Definitions.Panel
    {
        internal Vector2 Offset { get; set; }

        internal void Move(Vector2 offset)
        {
            Offset = offset;
            InvalidateLayout();
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (Widget child in Children)
            {
                LayoutChild(child, new UiBoxConstraints(0, float.PositiveInfinity, 0, float.PositiveInfinity));
                SetChildOffset(child, Offset);
            }

            return constraints.Constrain(new(100, 80));
        }
    }
}
