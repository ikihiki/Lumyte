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
        Connect(Surface.RootElement);
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

    internal UiElement Element(Widget widget) => Walk(Surface.RootElement).Single(element => ReferenceEquals(element.Widget, widget));

    private static IEnumerable<UiElement> Walk(UiElement root)
    {
        yield return root;
        foreach (UiElement child in root.Children)
        {
            foreach (UiElement element in Walk(child))
            {
                yield return element;
            }
        }
    }

    private static void Connect(UiElement root)
    {
        foreach (UiElement element in Walk(root))
        {
            if (element.Widget is RuntimeButton button)
            {
                button.Runtime = (RuntimeButtonElement)element;
            }
            else if (element.Widget is OffsetPanel panel)
            {
                panel.Runtime = (OffsetPanelElement)element;
            }
        }
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
        internal RuntimeButtonElement? Runtime { get; set; }

        internal bool IsPressed => Runtime?.IsPressed ?? false;

        internal void Change(Action<RuntimeButton> change)
        {
            change(this);
            Runtime?.Refresh();
        }

        protected override UiElement CreateElement() => new RuntimeButtonElement(this);
    }

    internal sealed class RuntimeButtonElement : ButtonElement
    {
        internal RuntimeButtonElement(RuntimeButton widget)
            : base(widget)
        {
        }

        internal void Refresh()
        {
            InvalidateLayout();
            InvalidateInput();
        }
    }

    internal sealed class OffsetPanel : Definitions.Panel
    {
        internal Vector2 Offset { get; set; }

        internal OffsetPanelElement? Runtime { get; set; }

        internal void Move(Vector2 offset)
        {
            Offset = offset;
            Runtime?.Refresh();
        }

        protected override UiElement CreateElement() => new OffsetPanelElement(this);
    }

    internal sealed class OffsetPanelElement : UiElement<OffsetPanel>
    {
        internal OffsetPanelElement(OffsetPanel widget)
            : base(widget)
        {
        }

        internal void Refresh() => InvalidateLayout();

        protected override void OnMounted() => Widget.Runtime = this;

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (UiElement child in Children)
            {
                LayoutChild(child, new UiBoxConstraints(0, float.PositiveInfinity, 0, float.PositiveInfinity));
                SetChildOffset(child, Widget.Offset);
            }

            return constraints.Constrain(new(100, 80));
        }
    }
}
