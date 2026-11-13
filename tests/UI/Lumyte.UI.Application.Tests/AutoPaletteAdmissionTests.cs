using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Lumyte.UI.TextEditing;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks semantic palette conversion at actual application, text and target admission boundaries.</summary>
public sealed class AutoPaletteAdmissionTests
{
    /// <summary>Precomputed normalized values satisfy the same unit checks without a drawing-specific bridge.</summary>
    [Fact]
    public void PaletteValuesEnterTargetsOptionsAndTextStylesDirectly()
    {
        SRgba<float> cornflower = SRgba<float>.CornflowerBlue;
        var target = new RenderTarget2D { View = null!, ClearColor = SRgba<float>.CornflowerBlue };
        var info = new DrawingTargetInfo2D { Description = null!, ClearColor = SRgba<float>.CornflowerBlue };
        var binding = new RenderTargetBinding2D { Description = null!, Texture = null!, ClearColor = SRgba<float>.CornflowerBlue };
        Assert.Equal(cornflower, target.ClearColor);
        Assert.Equal(cornflower, info.ClearColor);
        Assert.Equal(cornflower, binding.ClearColor);
        var options = new UiApplicationOptions { Background = SRgba<float>.White };
        Assert.Equal(new SRgba<float>(1, 1, 1, 1), options.Background);
        var style = new ImeCandidateStyle(SRgba<float>.White, SRgba<float>.Black, SRgba<float>.Blue, SRgba<float>.White);
        Assert.Equal(SRgba<float>.Blue, style.SelectedBackground);
        Assert.Equal(SRgba<float>.Black, style.Foreground);
        var raw = new SRgba<float>(2, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => target with { ClearColor = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => options with { Background = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => style with { Foreground = raw });
        Assert.Equal(cornflower, target.ClearColor);
        Assert.Equal(SRgba<float>.Black, style.Foreground);
    }
}
