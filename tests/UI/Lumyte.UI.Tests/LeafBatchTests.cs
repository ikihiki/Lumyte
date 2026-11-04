using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks compatible leaf updates retain coherent validation and publication.</summary>
public sealed class LeafBatchTests
{
    /// <summary>Checks a whole-tree validator observes all leaf proposals together.</summary>
    [Fact]
    public void SharedSourcePublishesEveryLeafInOneValidatedBatch()
    {
        var width = Signal.Create<float?>(10);
        var observed = new List<float?[]>();
        var root = new InspectingPanel(observed.Add);
        root.Children.Initialize(Enumerable.Range(0, 128).Select(_ => new Leaf(width)).ToArray());
        using var surface = new UiSurface(root);
        UiElement[] retained = surface.RootElement.Children.ToArray();
        ulong revision = surface.TreeRevision;
        observed.Clear();
        width.Value = 20;
        surface.UpdateTree();
        Assert.Equal(revision + 1, surface.TreeRevision);
        Assert.Equal(retained, surface.RootElement.Children);
        Assert.All(Assert.Single(observed), value => Assert.Equal(20, value));
        Assert.All(retained, element => Assert.Equal(20, element.Widget.Width));
    }

    /// <summary>Checks failure in a later leaf preserves every previous configuration.</summary>
    [Fact]
    public void RejectedLeafDoesNotPublishEarlierLeaves()
    {
        var width = Signal.Create<float?>(10);
        using var surface = new UiSurface(Panel()[new Leaf(width), new Leaf(width, true), new Leaf(width)]);
        Widget[] previous = surface.RootElement.Children.Select(element => element.Widget).ToArray();
        ulong revision = surface.TreeRevision;
        width.Value = 20;
        Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.Equal(revision, surface.TreeRevision);
        Assert.Equal(previous, surface.RootElement.Children.Select(element => element.Widget));
        Assert.All(surface.RootElement.Children, element => Assert.Equal(10, element.Widget.Width));
    }

    private sealed class InspectingPanel(Action<float?[]> observe) : Definitions.Panel
    {
        protected override void ValidateDeclaration(UiWidgetValidationContext context) => observe(context.Nodes.OfType<Leaf>().Select(widget => widget.Width).ToArray());
    }

    private sealed class Leaf : Widget
    {
        private readonly UiArgument<float?> _width;

        internal Leaf(UiArgument<float?> width, bool reject = false)
        {
            _width = width;
            Reject = reject;
            Width = width.GetInitialValue();
            width.AttachTo(this, nameof(Width), Width, static (widget, value) => widget.Width = value);
        }

        internal bool Reject { get; }

        protected override UiElement CreateElement() => new LeafElement(this);

        protected override Widget RebuildDeclaration(UiWidgetRebuildContext context) => new Leaf(_width, Reject);
    }

    private sealed class LeafElement(Leaf widget) : UiElement<Leaf>(widget)
    {
        protected override Action? PrepareUpdate(Leaf next, UiElementUpdateContext context)
        {
            if (next.Reject && next.Width == 20)
            {
                throw new InvalidOperationException("Rejected candidate.");
            }

            return null;
        }
    }
}
