using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks layout consumes declaration changes through the common tree update boundary.</summary>
public sealed class RebuildLayoutTests
{
    /// <summary>Checks new declarations are applied before geometry and explicit tree updates leave layout dirty.</summary>
    [Fact]
    public void LayoutAppliesTheQueuedTreeAndComputesNewGeometry()
    {
        Definitions.Panel child = Panel(width: 20, height: 20);
        using var surface = new UiSurface(Panel()[child]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(20, child.Size.X);
        Definitions.Panel next = Panel(width: 40, height: 30);
        surface.Rebuild(Panel()[next]);
        surface.UpdateTree();
        surface.UpdateLayout();
        Assert.Equal(40, next.Size.X);
        Assert.Equal(30, next.Size.Y);
        Definitions.Panel last = Panel(width: 60, height: 50);
        surface.Rebuild(Panel()[last]);
        surface.UpdateLayout();
        Assert.Equal(60, last.Size.X);
    }
}
