using System.Drawing;

using Xunit;

namespace Lumyte.Platform.Tests;

public sealed class WindowOptionsTests
{
    [Fact]
    public void DefaultsDescribeAVisibleApplicationWindow()
    {
        var options = new WindowOptions();

        Assert.Equal("Lumyte", options.Title);
        Assert.Equal(new Size(1280, 720), options.ClientSize);
        Assert.Null(options.Position);
        Assert.Equal(WindowState.Normal, options.State);
        Assert.True(options.IsVisible);
    }
}
