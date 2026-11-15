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
            var codes = new SRgba8((byte)i, (byte)(255 - i), (byte)(i ^ 127), (byte)i);
            var expected = new SRgba<float>((float)(i / 255d), (float)((255 - i) / 255d), (float)((i ^ 127) / 255d), (float)(i / 255d));
            SRgba<float> automatic = codes;
            Assert.Equal(expected, automatic);
            Assert.Equal(codes.ToCodes().NormalizeUnorm<float>(255), automatic);
        }

        Assert.Equal(new SRgba<float>(1, 0, 0, 1), SRgba<float>.Red);
        Assert.Equal(default, SRgba<float>.Transparent);
        Assert.Equal(128 / 255f, SRgba<float>.Green.G);
        Assert.Equal(1, SRgba<float>.Lime.G);
        Assert.Equal(new SRgba<float>(100 / 255f, 149 / 255f, 237 / 255f, 128 / 255f), SRgba<float>.CornflowerBlue.WithAlpha(128 / 255f));
    }

    /// <summary>The normalized palette composes directly with an ordinary canvas recording.</summary>
    [Fact]
    public void NamedPaletteCanRecordCanvasFillWithoutManualComponents()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new Rect2D(0, 0, 10, 20), SRgba<float>.Red);
        DrawList2D list = canvas.End();
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(list.Commands));
        SolidPaintNode2D solid = Assert.IsType<SolidPaintNode2D>(command.Paint.Root);
        Assert.Equal(new SRgba<float>(1, 0, 0, 1), solid.Color);
    }
}
