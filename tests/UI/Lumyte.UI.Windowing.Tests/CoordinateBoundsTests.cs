using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Checks window domain adapters preserve existing arithmetic precision.</summary>
public sealed class CoordinateBoundsTests
{
    /// <summary>Checks input uses double origin-size sums rather than rounded drawing edges.</summary>
    [Fact]
    public void InputPreservesSmallSizeAtLargeOrigin()
    {
        Assert.True(UiWindowManagerCore.Contains(new(16777216, 0, 1, 1), new(16777216, 0)));
        Assert.False(UiWindowManagerCore.Contains(new(16777216, 0, 1, 1), new(16777218, 0)));
        Assert.False(UiWindowManagerCore.Contains(new(0, 0, 0, 1), Vector2.Zero));
        Assert.False(UiWindowManagerCore.Contains(new(0, 0, 1, 1), new(1, 0)));
    }

    /// <summary>Checks float clipping preserves empty anchors and independent axis extents.</summary>
    [Fact]
    public void DrawingIntersectionPreservesFloatAndEmptyContracts()
    {
        Assert.Equal(new Rect2D(20, 0, 0, 5), UiWindowManagerCore.Intersect(new(0, 0, 10, 10), new(20, 0, 10, 5)));
        Assert.Equal(new Rect2D(16777216, 0, 0, 1), UiWindowManagerCore.Intersect(new(16777216, 0, 1, 1), new(16777216, 0, 1, 1)));
    }
}
