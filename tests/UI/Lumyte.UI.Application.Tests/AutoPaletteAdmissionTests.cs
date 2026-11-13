using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Lumyte.UI.TextEditing;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks semantic palette conversion at actual application, text and target admission boundaries.</summary>
public sealed class AutoPaletteAdmissionTests
{
    /// <summary>Automatic normalization satisfies the same unit checks without a drawing-specific bridge.</summary>
    [Fact]
    public void PaletteValuesEnterTargetsOptionsAndTextStylesDirectly()
    {
        Rgba<float> cornflower = CssColors.CornflowerBlue;
        var target = new RenderTarget2D { View = null!, ClearColor = CssColors.CornflowerBlue };
        var info = new DrawingTargetInfo2D { Description = null!, ClearColor = CssColors.CornflowerBlue };
        var binding = new RenderTargetBinding2D { Description = null!, Texture = null!, ClearColor = CssColors.CornflowerBlue };
        Assert.Equal(cornflower, target.ClearColor);
        Assert.Equal(cornflower, info.ClearColor);
        Assert.Equal(cornflower, binding.ClearColor);
        var options = new UiApplicationOptions { Background = CssColors.White };
        Assert.Equal(new Rgba<float>(1, 1, 1, 1), options.Background);
        var style = new ImeCandidateStyle(CssColors.White, CssColors.Black, CssColors.Blue, CssColors.White);
        Assert.Equal((Rgba<float>)CssColors.Blue, style.SelectedBackground);
        Assert.Equal((Rgba<float>)CssColors.Black, style.Foreground);
        var raw = new Rgba<float>(2, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => target with { ClearColor = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => options with { Background = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => style with { Foreground = raw });
        Assert.Equal(cornflower, target.ClearColor);
        Assert.Equal((Rgba<float>)CssColors.Black, style.Foreground);
    }
}
