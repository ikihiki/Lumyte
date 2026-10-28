using Lumyte.Composition;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Tests fixed content construction, ownership and one-time initialization through generated APIs.</summary>
public sealed class WidgetTreeTests
{
    /// <summary>Checks normal and background content share one ownership tree.</summary>
    [Fact]
    public void GeneratedCompositionInitializesNormalAndBackgroundContent()
    {
        Definitions.Panel content = Panel();
        Definitions.Panel background = Panel();
        Definitions.Button button = Button()[content, Button.Background()[background]];
        Definitions.Panel root = Panel()[button];
        Assert.Same(root, button.Parent);
        Assert.Same(button, content.Parent);
        Assert.Same(button, background.Parent);
        Assert.Same(content, Assert.Single(button.Children));
        Assert.Same(background, Assert.Single(button.BackgroundChildren));
        Assert.NotSame(Panel(), Panel());
    }

    /// <summary>Checks the public child collection is sealed, owner-created and cannot be replaced.</summary>
    [Fact]
    public void ContentUsesOnlyOwnerCreatedDedicatedStorage()
    {
        Assert.True(typeof(WidgetChildren).IsSealed);
        Assert.Empty(typeof(WidgetChildren).GetConstructors());
        Assert.Null(typeof(Definitions.Panel).GetProperty(nameof(Definitions.Panel.Children))!.SetMethod);
        Assert.Null(typeof(Definitions.Button).GetProperty(nameof(Definitions.Button.BackgroundChildren))!.SetMethod);
        Assert.IsAssignableFrom<ICompositionContent<Widget>>(Panel().Children);
        Assert.False((object)Panel().Children is IList<Widget>);
    }

    /// <summary>Checks constructor actions and derived panels use the same fixed child storage.</summary>
    [Fact]
    public void ActionsAndCustomPanelsCanInitializeContentOnce()
    {
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel(with: [panel => panel.Children.Initialize(child)]);
        Assert.Same(root, child.Parent);
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize());
        var custom = new CustomPanel();
        custom.Children.Initialize(Panel());
        Assert.Same(custom, Assert.Single(custom.Children).Parent);
    }

    /// <summary>Checks caller arrays are copied, preserving order without accepting arbitrary list implementations.</summary>
    [Fact]
    public void InputArraysAreCopiedAndOrderingIsFixed()
    {
        Definitions.Panel first = Panel();
        Definitions.Panel second = Panel();
        Widget[] input = [first, second];
        Definitions.Panel root = Panel()[input];
        input[0] = Panel();
        Assert.Same(first, root.Children[0]);
        Assert.Equal(new Widget[] { first, second }, root.Children);
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize(second, first));
        Assert.Same(first, root.Children[0]);
        Assert.Same(root, first.Parent);
    }

    /// <summary>Checks all later assignments fail, including identical, empty and null input.</summary>
    [Fact]
    public void EverySecondInitializationIsRejected()
    {
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel()[child];
        Assert.Throws<InvalidOperationException>(() => root[child]);
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize());
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize(null!));
        Assert.Same(child, Assert.Single(root.Children));
        Assert.Same(root, child.Parent);
    }

    /// <summary>Checks even a successful empty initialization consumes the assignment.</summary>
    [Fact]
    public void EmptyInitializationIsFinal()
    {
        Definitions.Panel root = Panel()[Array.Empty<Widget>()];
        Assert.Empty(root.Children);
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize(Panel()));
        Definitions.Button button = Button()[Button.Background()[Array.Empty<Widget>()]];
        Assert.Throws<InvalidOperationException>(() => button[Button.Background()[Panel()]]);
    }

    /// <summary>Checks failed validation leaves the collection uninitialized and every candidate unowned.</summary>
    [Fact]
    public void FailedInitializationCanBeRetriedWithoutPartialAdoption()
    {
        Definitions.Panel root = Panel();
        Definitions.Panel child = Panel();
        Assert.Throws<ArgumentNullException>(() => root.Children.Initialize(null!));
        Assert.Throws<ArgumentException>(() => root.Children.Initialize(child, null!));
        Assert.Throws<ArgumentException>(() => root.Children.Initialize(child, child));
        Assert.Empty(root.Children);
        Assert.Null(child.Parent);
        root.Children.Initialize(child);
        Assert.Same(root, child.Parent);
    }

    /// <summary>Checks cycles, cross-parent ownership and cross-slot ownership are rejected before commit.</summary>
    [Fact]
    public void OwnershipAndCyclesRemainValidated()
    {
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel()[child];
        Assert.Throws<ArgumentException>(() => child.Children.Initialize(root));
        Assert.Throws<ArgumentException>(() => child.Children.Initialize(child));
        Definitions.Panel other = Panel();
        Assert.Throws<ArgumentException>(() => other.Children.Initialize(child));
        Definitions.Button button = Button()[Panel()];
        Assert.Throws<ArgumentException>(() => button[Button.Background()[button.Children[0]]]);
        Assert.Empty(other.Children);
        Assert.Empty(child.Children);
        Assert.Empty(button.BackgroundChildren);
    }

    /// <summary>Checks the Button content limit and independent one-time background initialization.</summary>
    [Fact]
    public void ContentAndBackgroundHaveIndependentOneTimeAssignments()
    {
        Definitions.Button button = Button();
        Assert.Throws<ArgumentException>(() => button.Children.Initialize(Panel(), Panel()));
        Definitions.Panel content = Panel();
        _ = button[content];
        _ = button[Button.Background()[Panel(), Panel()]];
        Assert.Same(content, Assert.Single(button.Children));
        Assert.Equal(2, button.BackgroundChildren.Count);
        Assert.Throws<InvalidOperationException>(() => button[Button.Background()[Panel()]]);
        Assert.Throws<InvalidOperationException>(() => button[Panel()]);
    }

    /// <summary>Checks a later failed slot does not roll back earlier successful normal content or slots.</summary>
    [Fact]
    public void MixedConstructionRetainsEarlierSuccessfulAssignments()
    {
        Definitions.Button button = Button();
        Definitions.Panel content = Panel();
        Definitions.Panel first = Panel();
        Definitions.Panel later = Panel();
        Assert.Throws<InvalidOperationException>(() => button[content, Button.Background()[first], Button.Background()[later]]);
        Assert.Same(content, Assert.Single(button.Children));
        Assert.Same(first, Assert.Single(button.BackgroundChildren));
        Assert.Null(later.Parent);
    }

    /// <summary>Checks a captured slot can initialize separate targets with separate children, while shared children stay owned.</summary>
    [Fact]
    public void SlotSnapshotsDoNotPermitReparenting()
    {
        Definitions.Panel child = Panel();
        Widget[] input = [child];
        CompositionSlotAssignment<Definitions.Button> slot = Button.Background()[input];
        input[0] = Panel();
        Definitions.Button first = Button()[slot];
        Definitions.Button second = Button();
        Assert.Throws<ArgumentException>(() => second[slot]);
        Assert.Same(first, child.Parent);
        Assert.Empty(second.BackgroundChildren);
        _ = second[Button.Background()[Panel()]];
    }

    private sealed class CustomPanel : Definitions.Panel
    {
    }
}
