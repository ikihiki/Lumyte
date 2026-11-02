using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Tests.FunctionTestUi;
using PanelWidget = Lumyte.UI.Compose.Definitions.Panel;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks function children participate in the common widget replacement contracts.</summary>
public sealed class FunctionWidgetRetentionTests
{
    /// <summary>Checks a parent parameter keeps its child functions and their local state.</summary>
    [Fact]
    public void ParentArgumentsRetainChildFunctionsAndSubscriptions()
    {
        var width = Signal.Create<float?>(80);
        int builds = 0;
        PanelWidget parent = Panel(width: width)[Observed(10, () => builds++), Counter("state")];
        PanelWidget root = Panel()[parent];
        using var surface = new UiSurface(root);
        UiElement parentElement = surface.RootElement.Children[0];
        UiElement[] children = parentElement.Children.ToArray();
        width.Value = 100;
        surface.UpdateTree();
        PanelWidget next = Assert.IsType<PanelWidget>(parentElement.Widget);
        Assert.NotSame(parent, next);
        Assert.Same(children[0], parentElement.Children[0]);
        Assert.Same(children[1], parentElement.Children[1]);
        Assert.Same(parentElement, children[0].Parent);
        Assert.Equal(1, builds);
        Lumyte.UI.Compose.Definitions.Button button = Assert.IsType<Lumyte.UI.Compose.Definitions.Button>(Assert.IsAssignableFrom<UiFunctionElement>(parentElement.Children[1]).Content.Widget);
        button.OnClick!();
        surface.UpdateTree();
        Assert.Equal("state:3", Assert.IsAssignableFrom<UiFunctionElement>(parentElement.Children[1]).Content.Widget.AutomationName);
        using var independent = new UiSurface(root);
        Assert.NotSame(children[0], independent.RootElement.Children[0].Children[0]);
    }

    /// <summary>Checks shared visual identities can change locally and reject collisions across the surface.</summary>
    [Fact]
    public void SharedKeysAreValidatedAcrossRetainedSiblings()
    {
        var key = Signal.Create("first");
        Widget sibling = Rectangle(sharedElementKey: new UiSharedElementKey("reserved"));
        PanelWidget root = Panel()[SharedChoice(key), sibling];
        using var surface = new UiSurface(root);
        key.Value = "second";
        surface.UpdateTree();
        Assert.Same(root, surface.Root);
        Assert.Same(sibling, surface.RootElement.Children[1].Widget);
        UiElement current = surface.RootElement.Children[0];
        Assert.Equal("second", Assert.IsAssignableFrom<UiFunctionElement>(current).Content.Widget.SharedElementKey!.Value.Value);
        key.Value = "reserved";
        Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.Same(current, surface.RootElement.Children[0]);
    }
}
