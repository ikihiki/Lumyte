using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks the shared partial-publication foundation before automatic signal subscriptions are introduced.</summary>
public sealed class ElementSubtreeTests
{
    /// <summary>Checks newly constructed child subtrees do not inherit omitted content from their previous declarations.</summary>
    [Fact]
    public void FreshChildSubtreesCanBecomeEmptyDuringPartialPublication()
    {
        using var surface = new UiSurface(Panel()[Panel()[Button()]]);
        UiElement branch = surface.RootElement.Children[0];
        surface.RebuildSubtree(surface.RootElement, Panel()[Panel()]);
        Assert.Same(branch, surface.RootElement.Children[0]);
        Assert.Empty(branch.Children);
    }

    /// <summary>Checks replacing one configuration preserves siblings and the actual mounted child slots.</summary>
    [Fact]
    public void PartialConfigurationUpdateRetainsChildrenAndSiblings()
    {
        var parent = new ChoicePanel();
        parent.Children.Initialize(Panel());
        using var surface = new UiSurface(Panel()[parent, Button()]);
        UiElement root = surface.RootElement;
        UiElement owner = root.Children[0];
        UiElement child = Assert.Single(owner.Children);
        UiElement sibling = root.Children[1];
        var next = new ChoicePanel();
        surface.RebuildSubtree(owner, next);
        Assert.Same(owner, root.Children[0]);
        Assert.Same(next, owner.Widget);
        Assert.Same(child, Assert.Single(owner.Children));
        Assert.Empty(next.Children);
        Assert.Same(sibling, root.Children[1]);
        Assert.Same(root, surface.RootElement);
    }

    /// <summary>Checks actual child identity replacement invokes the unchanged parent's custom exchange preparation.</summary>
    [Fact]
    public void ChildIdentityChangeUpdatesCustomParentState()
    {
        var parent = new ChoicePanel();
        parent.Children.Initialize(Panel(key: "before"));
        using var surface = new UiSurface(parent);
        ChoiceElement element = Assert.IsType<ChoiceElement>(surface.RootElement);
        UiElement previous = element.Selected!;
        surface.RebuildSubtree(previous, Panel(key: "after"));
        Assert.Same(parent, element.Widget);
        Assert.NotSame(previous, element.Selected);
        Assert.Same(Assert.Single(element.Children), element.Selected);
        Assert.False(previous.IsMounted);
        Assert.Equal(2, element.Prepared);
    }

    /// <summary>Checks global validators read pending configuration over unchanged runtime branches.</summary>
    [Fact]
    public void PartialValidationFailurePreservesTheCurrentTree()
    {
        var parent = new UniquePanel();
        parent.Children.Initialize(Panel(key: "unique"), Panel(key: "other"));
        using var surface = new UiSurface(parent);
        UiElement previous = surface.RootElement.Children[1];
        Assert.Throws<ArgumentException>(() => surface.RebuildSubtree(previous, Panel(key: "unique")));
        Assert.Same(previous, surface.RootElement.Children[1]);
        Assert.Equal("other", previous.Widget.Key);
        Assert.True(previous.IsMounted);
    }

    /// <summary>Checks a surface cannot publish into another surface's runtime instance.</summary>
    [Fact]
    public void SubtreeOwnerMustBelongToTheSurface()
    {
        using var surface = new UiSurface(Panel());
        using var other = new UiSurface(Panel());
        Assert.Throws<ArgumentException>(() => surface.RebuildSubtree(other.RootElement, Panel()));
    }

    /// <summary>Checks changing a child's key validates the unchanged parent's entire proposed slot.</summary>
    [Fact]
    public void PartialPublicationRejectsDuplicateSiblingKeys()
    {
        using var surface = new UiSurface(Panel()[Panel(key: "first"), Panel(key: "second")]);
        UiElement previous = surface.RootElement.Children[1];
        Assert.Throws<ArgumentException>(() => surface.RebuildSubtree(previous, Panel(key: "first")));
        Assert.Same(previous, surface.RootElement.Children[1]);
        Assert.Equal("second", previous.Widget.Key);
    }

    private sealed class ChoicePanel : Definitions.Panel
    {
        protected override UiElement CreateElement() => new ChoiceElement(this);
    }

    private sealed class ChoiceElement(ChoicePanel widget) : UiElement<ChoicePanel>(widget)
    {
        internal UiElement? Selected { get; private set; }

        internal int Prepared { get; private set; }

        protected override Action PrepareUpdate(ChoicePanel next, UiElementUpdateContext context)
        {
            UiElement? selected = context.GetChildren(next.Children).FirstOrDefault();
            return () =>
            {
                Prepared++;
                Selected = selected;
            };
        }
    }

    private sealed class UniquePanel : Definitions.Panel
    {
        protected override void ValidateDeclaration(UiWidgetValidationContext context)
        {
            if (context.Nodes.Count(node => node.Key == "unique") > 1)
            {
                throw new ArgumentException("Duplicate projected identity.");
            }
        }
    }
}
