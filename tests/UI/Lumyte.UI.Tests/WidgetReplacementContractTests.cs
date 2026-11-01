using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks custom runtime child indexes and proposed configuration validation.</summary>
public sealed class WidgetReplacementContractTests
{
    /// <summary>Checks child configuration replacement does not require repairing a cached instance reference.</summary>
    [Fact]
    public void FullReplacementRetainsComponentOwnedChildReferences()
    {
        using var surface = new UiSurface(new CachedParent(Panel(key: "child")));
        CachedElement element = Assert.IsType<CachedElement>(surface.RootElement);
        UiElement child = element.Selected!;
        Widget nextChild = Panel(key: "child");
        surface.Rebuild(new CachedParent(nextChild));
        surface.UpdateTree();
        Assert.Same(child, element.Selected);
        Assert.Same(nextChild, child.Widget);
    }

    /// <summary>Checks custom child selection can respond to actual identity changes.</summary>
    [Fact]
    public void ChangedChildIdentityUpdatesCustomSelection()
    {
        using var surface = new UiSurface(new CachedParent(Panel(key: "before")));
        CachedElement element = Assert.IsType<CachedElement>(surface.RootElement);
        UiElement previous = element.Selected!;
        surface.Rebuild(new CachedParent(Panel(key: "after")));
        surface.UpdateTree();
        Assert.Same(Assert.Single(element.Children), element.Selected);
        Assert.NotSame(previous, element.Selected);
        Assert.False(previous.IsMounted);
    }

    /// <summary>Checks rejecting prepared child changes leaves live runtime fields and topology untouched.</summary>
    [Fact]
    public void FailedExchangePreparationKeepsTheCurrentDeclaration()
    {
        var before = new CachedParent(Panel());
        using var surface = new UiSurface(before);
        CachedElement element = Assert.IsType<CachedElement>(surface.RootElement);
        UiElement selected = element.Selected!;
        surface.Rebuild(new CachedParent(Panel()) { Reject = true });
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(before, element.Widget);
        Assert.Same(selected, element.Selected);
        Assert.Same(element, selected.Parent);
    }

    /// <summary>Checks initial validation reads the proposed configuration tree before mounting.</summary>
    [Fact]
    public void InitialValidationReceivesTheCompleteProposedTree()
    {
        var root = new CachedParent(Panel());
        using var surface = new UiSurface(root);
        Assert.Equal(2, root.ValidatedNodes);
        Assert.Same(root, root.ValidatedParent);
    }

    private sealed class CachedParent : Definitions.Panel
    {
        internal CachedParent(Widget child)
        {
            Children.Initialize(child);
        }

        internal bool Reject { get; init; }

        internal int ValidatedNodes { get; private set; }

        internal Widget? ValidatedParent { get; private set; }

        protected override UiElement CreateElement() => new CachedElement(this);

        protected override void ValidateDeclaration(UiWidgetValidationContext context)
        {
            ValidatedNodes = context.Nodes.Count;
            ValidatedParent = context.GetParent(Children[0]);
        }
    }

    private sealed class CachedElement(CachedParent widget) : UiElement<CachedParent>(widget)
    {
        internal UiElement? Selected { get; private set; }

        protected override Action PrepareUpdate(CachedParent next, UiElementUpdateContext context)
        {
            if (next.Reject)
            {
                throw new InvalidOperationException("Rejected exchange.");
            }

            UiElement selected = context.GetChildren(next.Children)[0];
            return () => Selected = selected;
        }
    }
}
