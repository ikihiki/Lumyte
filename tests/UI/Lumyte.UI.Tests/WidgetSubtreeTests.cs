using System.Numerics;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using PanelWidget = Lumyte.UI.Compose.Definitions.Panel;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks local reactive proposals preserve element state and validate coherent batches.</summary>
public sealed class WidgetSubtreeTests
{
    /// <summary>Checks ordinary argument changes replace only their own configuration.</summary>
    [Fact]
    public void RectangleArgumentsDoNotReconstructTheirParentOrSiblings()
    {
        var width = Signal.Create<float?>(10);
        Definitions.Rectangle before = Rectangle(width: width, height: 10);
        Widget sibling = Button();
        PanelWidget root = Panel()[before, sibling];
        using var surface = new UiSurface(root);
        UiElement instance = surface.RootElement.Children[0];
        UiElement siblingInstance = surface.RootElement.Children[1];
        width.Value = 30;
        surface.UpdateTree();
        Assert.Same(root, surface.Root);
        Assert.Same(instance, surface.RootElement.Children[0]);
        Assert.Same(siblingInstance, surface.RootElement.Children[1]);
        Assert.Same(sibling, siblingInstance.Widget);
        Assert.NotSame(before, instance.Widget);
        Assert.Equal(30, instance.Widget.Width);
        Assert.Equal(10, before.Width);
    }

    /// <summary>Checks a handwritten parent keeps the same child instance without exchanging references.</summary>
    [Fact]
    public void HandwrittenParentKeepsItsCachedChildReference()
    {
        var width = Signal.Create<float?>(10);
        Widget child = Rectangle(width: width, height: 10);
        using var surface = new UiSurface(new ManualPanel(100, child));
        var parent = (ManualPanelElement)surface.RootElement;
        UiElement? cached = parent.Cached;
        int commits = parent.ChildCommits;
        width.Value = 30;
        surface.UpdateTree();
        Assert.Same(cached, parent.Cached);
        Assert.Equal(commits, parent.ChildCommits);
        Assert.Equal(30, parent.Cached!.Widget.Width);
    }

    /// <summary>Checks a dirty parent configuration retains and updates its independently dirty child in the same batch.</summary>
    [Fact]
    public void HandwrittenParentAndChildUpdateTogether()
    {
        var parentWidth = Signal.Create<float?>(80);
        var childWidth = Signal.Create<float?>(10);
        Widget child = Rectangle(width: childWidth, height: 10);
        using var surface = new UiSurface(Panel()[new ManualPanel(parentWidth, child)]);
        var parent = (ManualPanelElement)surface.RootElement.Children[0];
        UiElement? cached = parent.Cached;
        object state = parent.State;
        Signal.Batch(() =>
        {
            parentWidth.Value = 100;
            childWidth.Value = 30;
        });
        surface.UpdateTree();
        Assert.Same(parent, surface.RootElement.Children[0]);
        Assert.Equal(100, parent.Widget.Width);
        Assert.Equal(30, parent.Cached!.Widget.Width);
        Assert.Same(cached, parent.Cached);
        Assert.Same(parent, parent.Cached.Parent);
        Assert.Same(state, parent.State);
    }

    /// <summary>Checks a rejected identity change keeps the old topology and handwritten index.</summary>
    [Fact]
    public void FailedCustomExchangeDoesNotPublishTheNewChild()
    {
        var key = Signal.Create<string?>("old");
        Widget child = Compute(() => Rectangle(key: key.Value));
        using var surface = new UiSurface(new ManualPanel(100, child) { RejectExchange = true });
        var parent = (ManualPanelElement)surface.RootElement;
        UiElement? cached = parent.Cached;
        int commits = parent.ChildCommits;
        key.Value = "new";
        Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.Same(cached, parent.Cached);
        Assert.Same(cached, Assert.Single(parent.Children));
        Assert.Equal(commits, parent.ChildCommits);
    }

    /// <summary>Checks a global validator sees the proposed configuration without publishing it.</summary>
    [Fact]
    public void GlobalValidationRejectsAChangedAnchorWithoutPublishing()
    {
        var key = Signal.Create<string?>("anchor");
        Widget child = Compute(() => Rectangle(key: key.Value));
        var root = new ValidatingPanel();
        root.Children.Initialize(child);
        using var surface = new UiSurface(root);
        UiElement instance = surface.RootElement.Children[0];
        key.Value = "missing";
        Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.Same(instance, Assert.Single(surface.RootElement.Children));
        Assert.Equal("anchor", instance.Widget.Key);
        Assert.Equal("missing", root.ProposedKey);
    }

    /// <summary>Checks keys may move coherently across sibling proposals, without exposing an intermediate duplicate.</summary>
    [Fact]
    public void BatchValidatesAllSiblingProposalsTogether()
    {
        var first = Signal.Create<string?>("first");
        var second = Signal.Create<string?>("second");
        using var surface = new UiSurface(Panel()[Compute(() => Rectangle(key: first.Value)), Compute(() => Rectangle(key: second.Value))]);
        Signal.Batch(() =>
        {
            first.Value = "second";
            second.Value = "first";
        });
        surface.UpdateTree();
        Assert.Equal(["second", "first"], surface.RootElement.Children.Select(element => element.Widget.Key));
    }

    /// <summary>Checks a handwritten retention hook cannot borrow another declaration's children.</summary>
    [Fact]
    public void RetentionCannotStealForeignChildren()
    {
        var width = Signal.Create<float?>(80);
        PanelWidget foreign = Panel()[Rectangle()];
        using var other = new UiSurface(foreign);
        using var surface = new UiSurface(new ManualPanel(width, Rectangle()) { RetainFrom = foreign.Children });
        UiElement original = surface.RootElement.Children[0];
        width.Value = 100;
        Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.Same(original, Assert.Single(surface.RootElement.Children));
        Assert.Same(other.RootElement, Assert.Single(other.RootElement.Children).Parent);
    }

    /// <summary>Checks a fresh declaration can populate a previously uninitialized slot.</summary>
    [Fact]
    public void HandwrittenRebuildCanPopulateAnUninitializedSlot()
    {
        var width = Signal.Create<float?>(80);
        using var surface = new UiSurface(new ManualPanel(width) { AddChildOnRebuild = true });
        surface.SetViewport(new(200, 100));
        width.Value = 100;
        surface.UpdateLayout();
        var parent = (ManualPanelElement)surface.RootElement;
        Assert.Same(parent.Cached, Assert.Single(parent.Children));
        Assert.Same(parent, parent.Cached!.Parent);
        Assert.Equal(new Vector2(12, 10), parent.Cached.Size);
    }

    private sealed class ManualPanel : PanelWidget
    {
        private readonly UiArgument<float?> _width;

        internal ManualPanel(UiArgument<float?> width, Widget? child = null)
        {
            _width = width;
            Width = width.GetInitialValue();
            width.AttachTo(this, nameof(Width), Width, static (widget, value) => widget.Width = value);
            if (child is not null)
            {
                Children.Initialize(child);
            }
        }

        internal bool RejectExchange { get; init; }

        internal WidgetChildren? RetainFrom { get; init; }

        internal bool AddChildOnRebuild { get; init; }

        protected override UiElement CreateElement() => new ManualPanelElement(this);

        protected override Widget RebuildDeclaration(UiWidgetRebuildContext context)
        {
            var next = new ManualPanel(_width) { RejectExchange = RejectExchange, AddChildOnRebuild = AddChildOnRebuild, RetainFrom = RetainFrom };
            context.RetainChildren(RetainFrom ?? Children, next.Children);
            if (AddChildOnRebuild && Children.Count == 0)
            {
                next.Children.Initialize(Rectangle(width: 12, height: 10));
            }

            return next;
        }
    }

    private sealed class ManualPanelElement : UiElement<ManualPanel>
    {
        internal ManualPanelElement(ManualPanel widget)
            : base(widget)
        {
        }

        internal UiElement? Cached { get; private set; }

        internal object State { get; } = new();

        internal int ChildCommits { get; private set; }

        protected override Action? PrepareUpdate(ManualPanel next, UiElementUpdateContext context)
        {
            UiElement? child = context.GetChildren(next.Children).SingleOrDefault();
            if (ReferenceEquals(Cached, child))
            {
                return null;
            }

            if (IsMounted && next.RejectExchange)
            {
                throw new InvalidOperationException("Rejected replacement.");
            }

            return () =>
            {
                Cached = child;
                ChildCommits++;
            };
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = Cached is null ? Vector2.Zero : LayoutChild(Cached, constraints.Loosen());
            if (Cached is not null)
            {
                SetChildOffset(Cached, Vector2.Zero);
            }

            return constraints.Constrain(size);
        }
    }

    private sealed class ValidatingPanel : PanelWidget
    {
        internal string? ProposedKey { get; private set; }

        protected override void ValidateDeclaration(UiWidgetValidationContext context)
        {
            ProposedKey = Assert.Single(context.GetChildren(this)).Key;
            if (!context.Nodes.Any(node => node.Key == "anchor"))
            {
                throw new ArgumentException("An anchor is required.");
            }
        }
    }
}
