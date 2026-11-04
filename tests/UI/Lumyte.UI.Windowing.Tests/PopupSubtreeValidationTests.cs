using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Checks popup validation remains surface-wide while ordinary widget updates stay local.</summary>
public sealed class PopupSubtreeValidationTests
{
    /// <summary>Checks retained popup declarations see candidate anchors across separate parent slots.</summary>
    [Fact]
    public void PopupValidationUsesAllProposedAnchors()
    {
        var key = Signal.Create<string?>("other");
        var width = Signal.Create<float?>(10);
        Widget popup = Lumyte.UI.Windowing.Compose.Popup(key: "menu", anchor: "anchor")[Panel()];
        Lumyte.UI.Compose.Definitions.Panel branch = Panel()[Compute(() => Button(key: key.Value, width: width))];
        Lumyte.UI.Compose.Definitions.Panel root = Panel()[Panel()[Button(key: "anchor")], branch, popup];
        using var surface = new UiSurface(root);
        width.Value = 20;
        surface.UpdateTree();
        Assert.Same(root, surface.Root);
        Assert.Same(popup, root.Children[2]);
        UiElement previous = Assert.Single(surface.RootElement.Children[1].Children);
        Assert.Equal(20, previous.Widget.Width);
        key.Value = "anchor";
        Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.Same(previous, Assert.Single(surface.RootElement.Children[1].Children));
        Assert.Equal("other", previous.Widget.Key);
    }
}
