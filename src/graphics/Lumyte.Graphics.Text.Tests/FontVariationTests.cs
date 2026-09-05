namespace Lumyte.Graphics.Text.Tests;

public sealed class FontVariationTests
{
    [Fact]
    public void ConstructorPreservesAValidAxisSetting()
    {
        var variation = new FontVariation("wght", 725.5f);

        Assert.Equal("wght", variation.Tag);
        Assert.Equal(725.5f, variation.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("abcde")]
    [InlineData("w\u001Fth")]
    [InlineData("wéth")]
    public void ConstructorRejectsInvalidAxisTags(string tag)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new FontVariation(tag, 400));

        Assert.Equal("tag", exception.ParamName);
    }

    [Fact]
    public void ConstructorRejectsANullAxisTag()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new FontVariation(null!, 400));

        Assert.Equal("tag", exception.ParamName);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ConstructorRejectsNonFiniteAxisValues(float value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new FontVariation("wght", value));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void FontFaceRejectsDuplicateVariationAxes()
    {
        FontVariation[] variations =
        [
            new("wght", 400),
            new("wght", 700),
        ];

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new FontFace(TestFontData.Create(), variations: variations));

        Assert.Equal("variations", exception.ParamName);
        Assert.Contains("wght", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FontFaceCopiesVariationSettingsFromTheCaller()
    {
        var original = new FontVariation("wght", 400);
        var replacement = new FontVariation("wdth", 75);
        FontVariation[] variations = [original];
        using var font = new FontFace(TestFontData.Create(), variations: variations);

        variations[0] = replacement;

        FontVariation stored = Assert.Single(font.Variations);
        Assert.Equal(original, stored);
    }

    [Fact]
    public void FontFaceExposesVariationsAsReadOnly()
    {
        using var font = new FontFace(
            TestFontData.Create(),
            variations: [new FontVariation("wght", 400)]);
        var exposed = Assert.IsAssignableFrom<IList<FontVariation>>(font.Variations);

        Assert.Throws<NotSupportedException>(
            () => exposed.Add(new FontVariation("wdth", 75)));
    }

    [Fact]
    public void ExistingConstructionCreatesAnUnvariedFace()
    {
        using var font = new FontFace(TestFontData.Create());

        ShapedText shaped = font.Shape("A");

        Assert.Empty(font.Variations);
        ShapedGlyph glyph = Assert.Single(shaped.Glyphs.ToArray());
        Assert.Equal(1u, glyph.GlyphId);
    }
}
