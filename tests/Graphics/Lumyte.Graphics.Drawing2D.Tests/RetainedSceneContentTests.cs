using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks immutable command pages and sharing boundaries.</summary>
public sealed class RetainedSceneContentTests
{
    /// <summary>Checks arbitrary command counts, identity, and immutable input capture.</summary>
    [Fact]
    public void PagesAreCanonicalAndReplacementSharesUnchangedPages()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 137; i++)
        {
            canvas.FillRect(new(i, 0, 1, 1), new Color2D(1, 0, 0, 1));
        }

        DrawList2D recording = canvas.End();
        var original = DrawingContent2D.FromDrawList(recording);
        Assert.Equal(137, original.Count);
        Assert.Equal(3, original.PageCount);
        Assert.Equal(64, DrawingContent2D.PageCapacity);
        Assert.Equal(new[] { 64, 64, 9 }, original.Pages.Select(page => page.Commands.Count));
        Assert.Same(recording.Commands[70], original.Pages[1].Commands[6]);
        DrawingContent2D replacement = original.WithPage(1, original.Pages[0]);
        Assert.Same(original.Pages[0], replacement.Pages[0]);
        Assert.Same(original.Pages[0], replacement.Pages[1]);
        Assert.Same(original.Pages[2], replacement.Pages[2]);
        Assert.Same(original, original.WithPage(1, original.Pages[1]));
        Assert.Throws<ArgumentException>(() => original.WithPage(0, original.Pages[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => original.WithPage(3, original.Pages[0]));
        canvas.Begin();
        canvas.FillCircle(new(0, 0), 2, new Color2D(0, 1, 0, 1));
        canvas.End();
        Assert.Equal(137, original.Count);
        Assert.Equal(new Rect2D(70, 0, 1, 1), Assert.IsType<FillRectCommand2D>(original.Pages[1].Commands[6]).Rect);
        IList<DrawList2D> mutable = Assert.IsAssignableFrom<IList<DrawList2D>>(original.Pages);
        Assert.Throws<NotSupportedException>(() => mutable[0] = original.Pages[1]);
    }

    /// <summary>Checks empty and short inputs do not acquire arbitrary minimum sizes.</summary>
    [Fact]
    public void EmptyAndSinglePageContentsAreValid()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        var empty = DrawingContent2D.FromDrawList(canvas.End());
        Assert.Equal(0, empty.Count);
        Assert.Empty(empty.Pages);
        Assert.Equal(0, empty.PageCount);
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new Color2D(1, 0, 0, 1));
        DrawList2D recording = canvas.End();
        var content = DrawingContent2D.FromDrawList(recording);
        Assert.Same(recording, Assert.Single(content.Pages));
    }
}
