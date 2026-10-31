using Lumyte.Graphics.Text.Parley;
using Lumyte.TextEditing;
using Lumyte.UI.TextEditing.Parley;
using Xunit;

namespace Lumyte.UI.TextEditing.Tests;

/// <summary>Checks indexed range queries preserve copied native geometry, including overlaps and bidirectional text.</summary>
public sealed class ParleyRangeIndexTests
{
    /// <summary>Checks every nonempty logical range against the full native selection list.</summary>
    /// <param name="text">The shaped document.</param>
    [Theory]
    [InlineData("office AV fi")]
    [InlineData("abc אבג def")]
    [InlineData("A😀e\u0301Z")]
    public void IndexedRangesMatchNativeIntersections(string text)
    {
        using var engine = new ParleyTextEngine2D();
        using Stream font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts/DejaVuSans.ttf"));
        string family = engine.AddFont(font);
        var options = new ParleyTextOptions2D { FontFamily = family, FontSize = 20, IncludeEditingGeometry = true };
        ParleyTextLayout2D native = engine.CreateDetailedLayout(text, options);
        UiTextLayout indexed = new ParleyUiTextLayoutProvider(engine, options).CreateLayout(new TextDocument(text).CaptureSnapshot(), float.PositiveInfinity);
        for (int start = 0; start < text.Length; start++)
        {
            for (int end = start + 1; end <= text.Length; end++)
            {
                var expected = native.SelectionRects.Where(rect => rect.Start < end && rect.Start + rect.Length > start).Select(rect => rect.Bounds).ToHashSet();
                Assert.True(expected.SetEquals(indexed.GetRangeBounds(new(start, end - start))));
            }
        }
    }
}
