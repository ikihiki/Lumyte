using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Exercises static argument sources and declaration reconciliation at real surface boundaries.</summary>
public sealed class SignalTests
{
    /// <summary>Checks a source reconstructs declarations and preserves a with override and child slots.</summary>
    [Fact]
    public void SourcesReconstructFreshDeclarations()
    {
        var width = Signal.Create<float?>(20);
        Definitions.Rectangle original = Rectangle(width: width, height: 10);
        using var surface = new UiSurface(Panel()[Button()[Button.Background()[original]]]);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        width.Value = 40;
        surface.UpdateLayout();
        Definitions.Button button = Assert.IsType<Definitions.Button>(Assert.Single(Assert.IsType<Definitions.Panel>(surface.Root).Children));
        Definitions.Rectangle rectangle = Assert.IsType<Definitions.Rectangle>(Assert.Single(button.BackgroundChildren));
        Assert.NotSame(original, rectangle);
        Assert.Equal(40, rectangle.Width);
        Assert.Equal(Vector2.Zero, rectangle.Size);
        Assert.Equal(20, original.Width);
    }

    /// <summary>Checks captured direct reads rebuild dynamic children and release their observers.</summary>
    [Fact]
    public void ConstructionReadsRebuildDynamicChildren()
    {
        var show = Signal.Create(false);
        int builds = 0;
        using var surface = new UiSurface(() =>
        {
            builds++;
            return show.Value ? Panel()[Rectangle(width: 12)] : Panel();
        });
        Assert.Empty(Assert.IsType<Definitions.Panel>(surface.Root).Children);
        show.Value = true;
        surface.UpdateTree();
        Assert.Single(Assert.IsType<Definitions.Panel>(surface.Root).Children);
        Assert.Equal(2, builds);
        surface.Dispose();
        show.Value = false;
        Assert.Equal(2, builds);
        Assert.False(show.Value);
    }

    /// <summary>Checks a paused surface still reports every source revision and explicit layout reads latest values.</summary>
    [Fact]
    public void PausedUpdatesPreserveRevisionAndLatestValues()
    {
        var width = Signal.Create<float?>(20);
        using var surface = new UiSurface(Rectangle(width: width));
        surface.SetSignalUpdatesEnabled(false);
        UiSignalRevision initial = surface.SignalRevision;
        width.Value = 30;
        UiSignalRevision changed = surface.SignalRevision;
        width.Value = 40;
        Assert.True(changed.Values > initial.Values);
        Assert.True(surface.SignalRevision.Values > changed.Values);
        Assert.Equal(20, surface.Root.Width);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        Assert.Equal(40, surface.Root.Width);
    }

    /// <summary>Checks paint-only changes reuse layout/input while size changes invalidate both.</summary>
    [Fact]
    public void PaintChangesReuseInputGeometry()
    {
        var fill = Signal.Create(new Color2D(1, 0, 0, 1));
        var width = Signal.Create<float?>(20);
        using var surface = new UiSurface(Panel()[Rectangle(width: width, fill: fill)]);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        object? before = surface.InputVersionForWindowing;
        fill.Value = new(0, 1, 0, 1);
        surface.UpdateLayout();
        Assert.Same(before, surface.InputVersionForWindowing);
        width.Value = 30;
        surface.UpdateLayout();
        Assert.NotSame(before, surface.InputVersionForWindowing);
    }

    /// <summary>Checks generated recipes capture and reconstruct deep trees without using the call stack.</summary>
    [Fact]
    public void DeepSignalTreesRebuildWithoutRecursiveRecipes()
    {
        var width = Signal.Create<float?>(20);
        Definitions.Widget root = Rectangle(width: width);
        for (int depth = 0; depth < 5000; depth++)
        {
            root = Panel()[root];
        }

        using var surface = new UiSurface(root);
        width.Value = 40;
        surface.UpdateTree();
        Definitions.Widget leaf = surface.Root;
        while (leaf is Definitions.Panel panel)
        {
            leaf = Assert.Single(panel.Children);
        }

        Assert.Equal(40, Assert.IsType<Definitions.Rectangle>(leaf).Width);
        Assert.NotSame(root, surface.Root);
    }

    /// <summary>Checks UI subscriptions remain owned by the surface even when attachment occurs inside a business scope.</summary>
    [Fact]
    public void BusinessScopesDoNotOwnAutomaticUiSubscriptions()
    {
        var width = Signal.Create<float?>(20);
        UiSurface? surface = null;
        using (var business = new SignalScope())
        {
            business.Run(() => surface = new(Rectangle(width: width)));
        }

        using UiSurface connected = surface ?? throw new InvalidOperationException("The test surface was not created.");
        width.Value = 40;
        connected.UpdateTree();
        Assert.Equal(40, connected.Root.Width);
    }

    /// <summary>Checks generated reconstruction replays with after the argument initial values.</summary>
    [Fact]
    public void WithOverridesArePreserved()
    {
        var width = Signal.Create<float?>(20);
        using var surface = new UiSurface(Rectangle(width: width, with: [rectangle => rectangle.Width = 99]));
        width.Value = 40;
        surface.UpdateTree();
        Assert.Equal(99, surface.Root.Width);
    }

    /// <summary>Checks a reentrant queued declaration owns its observers independently of the applying candidate.</summary>
    [Fact]
    public void ReentrantCandidateTransfersOnlyItsOwnSubscriptions()
    {
        var original = Signal.Create<float?>(10);
        var intermediate = Signal.Create<float?>(20);
        var latest = Signal.Create<float?>(30);
        using var surface = new UiSurface(() => Build(original));
        surface.SetSignalUpdatesEnabled(false);
        surface.Rebuild(() => Build(intermediate, () => surface.Rebuild(() => Build(latest))));
        surface.UpdateTree();
        Assert.Equal(20, Assert.Single(Assert.IsType<ReentrantPanel>(surface.Root).Children).Width);
        surface.UpdateTree();
        surface.SetSignalUpdatesEnabled(true);
        UiSignalRevision revision = surface.SignalRevision;
        original.Value = 11;
        intermediate.Value = 21;
        Assert.Equal(revision, surface.SignalRevision);
        latest.Value = 31;
        surface.UpdateTree();
        Assert.Equal(31, Assert.Single(Assert.IsType<ReentrantPanel>(surface.Root).Children).Width);

        static ReentrantPanel Build(Signal<float?> source, Action? prepared = null)
        {
            var panel = new ReentrantPanel { Prepared = prepared };
            panel.Children.Initialize(Rectangle(width: source));
            return panel;
        }
    }

    private sealed class ReentrantPanel : Definitions.Panel
    {
        internal Action? Prepared { get; init; }

        protected override void OnReconcile(Definitions.Widget previous) => Prepared?.Invoke();
    }
}
