using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies the automatic palette-code normalization into existing normalized drawing colors.</summary>
public sealed class CssPaletteTests
{
    /// <summary>Every byte value is normalized once without applying gamma or premultiplication.</summary>
    [Fact]
    public void ImplicitConversionMatchesRetainedNormalizationForAllCodes()
    {
        for (int i = 0; i <= 255; i++)
        {
            var codes = new Srgb8((byte)i, (byte)(255 - i), (byte)(i ^ 127), (byte)i);
            var expected = new Rgba<float>((float)(i / 255d), (float)((255 - i) / 255d), (float)((i ^ 127) / 255d), (float)(i / 255d));
            Rgba<float> automatic = codes;
            Assert.Equal(expected, automatic);
            Assert.Equal(codes.ToCodes().NormalizeUnorm<float>(255), automatic);
        }

        Assert.Equal(new Rgba<float>(1, 0, 0, 1), (Rgba<float>)CssColors.Red);
        Assert.Equal(default, (Rgba<float>)CssColors.Transparent);
        Assert.Equal(128 / 255f, ((Rgba<float>)CssColors.Green).G);
        Assert.Equal(1, ((Rgba<float>)CssColors.Lime).G);
        Assert.Equal(new Rgba<float>(100 / 255f, 149 / 255f, 237 / 255f, 128 / 255f), (Rgba<float>)CssColors.CornflowerBlue.WithAlpha(128));
    }

    /// <summary>The normalized palette composes directly with an ordinary canvas recording.</summary>
    [Fact]
    public void NamedPaletteCanRecordCanvasFillWithoutManualComponents()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new Rect2D(0, 0, 10, 20), CssColors.Red);
        DrawList2D list = canvas.End();
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(list.Commands));
        SolidPaintNode2D solid = Assert.IsType<SolidPaintNode2D>(command.Paint.Root);
        Assert.Equal(new Rgba<float>(1, 0, 0, 1), solid.Color);
    }
}
