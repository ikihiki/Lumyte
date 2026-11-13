using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies the explicit palette-code normalization into existing normalized drawing colors.</summary>
public sealed class CssPaletteTests
{
    /// <summary>Every byte value is normalized once without applying gamma or premultiplication.</summary>
    [Fact]
    public void ExplicitUnormNormalizationNormalizesAllComponentAndAlphaCodes()
    {
        for (int i = 0; i <= 255; i++)
        {
            var codes = new Rgba<byte>((byte)i, (byte)(255 - i), (byte)(i ^ 127), (byte)i);
            var expected = new Rgba<float>((float)(i / 255d), (float)((255 - i) / 255d), (float)((i ^ 127) / 255d), (float)(i / 255d));
            Assert.Equal(expected, codes.NormalizeUnorm<float>(255));
        }

        Assert.Equal(new Rgba<float>(1, 0, 0, 1), CssColors.Red.NormalizeUnorm<float>(255));
        Assert.Equal(default, CssColors.Transparent.NormalizeUnorm<float>(255));
        Assert.Equal(128 / 255f, CssColors.Green.NormalizeUnorm<float>(255).G);
        Assert.Equal(1, CssColors.Lime.NormalizeUnorm<float>(255).G);
        Assert.Equal(new Rgba<float>(100 / 255f, 149 / 255f, 237 / 255f, 128 / 255f), CssColors.CornflowerBlue.WithAlpha(128).NormalizeUnorm<float>(255));
    }

    /// <summary>The normalized palette composes directly with an ordinary canvas recording.</summary>
    [Fact]
    public void NamedPaletteCanRecordCanvasFillWithoutManualComponents()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new Rect2D(0, 0, 10, 20), CssColors.Red.NormalizeUnorm<float>(255));
        DrawList2D list = canvas.End();
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(list.Commands));
        SolidPaintNode2D solid = Assert.IsType<SolidPaintNode2D>(command.Paint.Root);
        Assert.Equal(new Rgba<float>(1, 0, 0, 1), solid.Color);
    }
}
