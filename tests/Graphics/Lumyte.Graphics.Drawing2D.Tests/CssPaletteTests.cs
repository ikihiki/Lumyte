using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies the explicit palette-code bridge into existing normalized drawing colors.</summary>
public sealed class CssPaletteTests
{
    /// <summary>Every byte value is normalized once without applying gamma or premultiplication.</summary>
    [Fact]
    public void SrgbByteFactoryNormalizesAllComponentAndAlphaCodes()
    {
        for (int i = 0; i <= 255; i++)
        {
            var codes = new Rgba<byte>((byte)i, (byte)(255 - i), (byte)(i ^ 127), (byte)i);
            var expected = new Color2D((float)(i / 255d), (float)((255 - i) / 255d), (float)((i ^ 127) / 255d), (float)(i / 255d));
            Assert.Equal(expected, Color2D.FromSrgb8(codes));
        }

        Assert.Equal(new Color2D(1, 0, 0, 1), Color2D.FromSrgb8(CssColors.Red));
        Assert.Equal(default, Color2D.FromSrgb8(CssColors.Transparent));
        Assert.Equal(128 / 255f, Color2D.FromSrgb8(CssColors.Green).G);
        Assert.Equal(1, Color2D.FromSrgb8(CssColors.Lime).G);
        Assert.Equal(new Color2D(100 / 255f, 149 / 255f, 237 / 255f, 128 / 255f), Color2D.FromSrgb8(CssColors.CornflowerBlue.WithAlpha(128)));
    }

    /// <summary>The palette bridge composes directly with an ordinary canvas recording.</summary>
    [Fact]
    public void NamedPaletteCanRecordCanvasFillWithoutManualComponents()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new Rect2D(0, 0, 10, 20), Color2D.FromSrgb8(CssColors.Red));
        DrawList2D list = canvas.End();
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(list.Commands));
        SolidPaintNode2D solid = Assert.IsType<SolidPaintNode2D>(command.Paint.Root);
        Assert.Equal(new Color2D(1, 0, 0, 1), solid.Color);
    }

    /// <summary>Constructor, init, with, deconstruction and parameter names retain their original validation boundary.</summary>
    [Fact]
    public void PaletteBridgeDoesNotWeakenExistingColorValidation()
    {
        var valid = Color2D.FromSrgb8(CssColors.CornflowerBlue);
        (float r, float g, float b, float a) = valid;
        Assert.Equal(new Color2D(r, g, b, a), valid);
        Assert.Equal("R", Assert.Throws<ArgumentOutOfRangeException>(() => new Color2D(float.NaN, 0, 0, 1)).ParamName);
        Assert.Equal("A", Assert.Throws<ArgumentOutOfRangeException>(() => new Color2D { A = 2 }).ParamName);
        Assert.Equal("G", Assert.Throws<ArgumentOutOfRangeException>(() => valid with { G = float.PositiveInfinity }).ParamName);
    }
}
