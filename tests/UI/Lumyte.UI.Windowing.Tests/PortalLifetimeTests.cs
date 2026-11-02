using System.Numerics;
using Lumyte.Composition;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Checks measuring portal content preserves its single mounted lifetime.</summary>
public sealed class PortalLifetimeTests
{
    /// <summary>Checks content mounts once and releases resources even when opening is canceled before admission.</summary>
    /// <param name="cancelBeforeOpening">Whether to close before the manager advances.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasurementAndPresentationShareOneElementLifetime(bool cancelBeforeOpening)
    {
        using var fixture = new Fixture();
        var lifetime = new Lifetime();
        var open = Signal.Create(true);
        Widget Content()
        {
            var declaration = new Tracked(lifetime);
            ((ICompositionRebuildable)declaration).SetCompositionFactory(() => Content());
            return declaration;
        }

        Widget declaration = Lumyte.UI.Windowing.Compose.Popup(key: "menu", anchor: "anchor", isOpen: open)[Content()];
        fixture.Surface.Rebuild(Panel()[Button(key: "anchor", width: 20, height: 20), declaration]);
        fixture.Surface.UpdateLayout();
        using var portals = new UiPopupPortalCoordinator(fixture.Popups, fixture.Owner, fixture.Surface);
        Assert.Equal(0, lifetime.Mounts);
        portals.Advance();
        Assert.Equal(1, lifetime.Mounts);
        Assert.Equal(0, lifetime.Unmounts);
        if (!cancelBeforeOpening)
        {
            fixture.Pump();
            Assert.Equal(UiPopupState.Open, Assert.Single(fixture.Popups.CapturePopups()).State);
            Assert.Equal(1, lifetime.Mounts);
            Assert.Equal(0, lifetime.Unmounts);
        }

        open.Value = false;
        fixture.Surface.UpdateLayout();
        portals.Advance();
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
        Assert.Equal(1, lifetime.Mounts);
        Assert.Equal(1, lifetime.Unmounts);
    }

    private sealed class Lifetime
    {
        internal int Mounts { get; set; }

        internal int Unmounts { get; set; }
    }

    private sealed class Tracked(Lifetime lifetime) : Widget
    {
        internal Lifetime Lifetime { get; } = lifetime;

        protected override UiElement CreateElement() => new TrackedElement(this);
    }

    private sealed class TrackedElement(Tracked widget) : UiElement<Tracked>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(20, 10));

        protected override void OnMounted() => Widget.Lifetime.Mounts++;

        protected override void OnUnmounted() => Widget.Lifetime.Unmounts++;
    }
}
