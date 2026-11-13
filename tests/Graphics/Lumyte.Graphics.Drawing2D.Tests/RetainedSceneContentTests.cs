using System.Reflection;
using Lumyte.Mathematics.Colors;
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
            canvas.FillRect(new(i, 0, 1, 1), new SRgba<float>(1, 0, 0, 1));
        }

        DrawList2D recording = canvas.End();
        DrawList2D original = recording;
        Assert.Equal(137, original.Count);
        Assert.Equal(3, original.RetainedPages.PageCount);
        Assert.Equal(64, DrawingContentPages2D.PageCapacity);
        Assert.Equal(new[] { 64, 64, 9 }, original.RetainedPages.Pages.Select(page => page.Commands.Count));
        Assert.Same(recording.Commands[70], original.RetainedPages.Pages[1].Commands[6]);
        DrawList2D replacement = original.WithPage(1, original.RetainedPages.Pages[0]);
        Assert.Same(original.RetainedPages.Pages[0], replacement.RetainedPages.Pages[0]);
        Assert.Same(original.RetainedPages.Pages[0], replacement.RetainedPages.Pages[1]);
        Assert.Same(original.RetainedPages.Pages[2], replacement.RetainedPages.Pages[2]);
        Assert.Same(original, original.WithPage(1, original.RetainedPages.Pages[1]));
        Assert.Throws<ArgumentException>(() => original.WithPage(0, original.RetainedPages.Pages[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => original.WithPage(3, original.RetainedPages.Pages[0]));
        canvas.Begin();
        canvas.FillCircle(new(0, 0), 2, new SRgba<float>(0, 1, 0, 1));
        canvas.End();
        Assert.Equal(137, original.Count);
        Assert.Equal(new Rect2D(70, 0, 1, 1), Assert.IsType<FillRectCommand2D>(original.RetainedPages.Pages[1].Commands[6]).Rect);
        IList<DrawList2D> mutable = Assert.IsAssignableFrom<IList<DrawList2D>>(original.RetainedPages.Pages);
        Assert.Throws<NotSupportedException>(() => mutable[0] = original.RetainedPages.Pages[1]);
    }

    /// <summary>Checks empty and short inputs do not acquire arbitrary minimum sizes.</summary>
    [Fact]
    public void EmptyAndSinglePageContentsAreValid()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        DrawList2D empty = canvas.End();
        Assert.Equal(0, empty.Count);
        Assert.Empty(empty.RetainedPages.Pages);
        Assert.Equal(0, empty.RetainedPages.PageCount);
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new SRgba<float>(1, 0, 0, 1));
        DrawList2D recording = canvas.End();
        DrawList2D content = recording;
        Assert.Same(recording, Assert.Single(content.RetainedPages.Pages));
    }

    /// <summary>Checks a recording is directly shared by placements, scenes and history without an adapter.</summary>
    [Fact]
    public void OneRecordingSharesContentAcrossPlacementsAndScenes()
    {
        DrawList2D recording = Record(137);
        Assert.False(recording.HasRetainedPages);
        Assert.Equal(137, recording.Count);
        using var first = new DrawingScene2D();
        using var second = new DrawingScene2D();
        DrawingNodeId2D node = first.Insert(first.Root, 0, recording);
        first.Insert(first.Root, 1, recording);
        second.Insert(second.Root, 0, recording);
        Assert.False(recording.HasRetainedPages);
        using DrawingSnapshot2D history = first.CaptureSnapshot();
        using (DrawingSceneReadLease2D borrowed = first.Borrow())
        {
            foreach (DrawingSceneEntry2D entry in ((IDrawingSceneSource2D)borrowed).Entries)
            {
                Assert.Same(recording, entry.Content);
                Assert.Same(recording.RetainedPages, entry.Content.RetainedPages);
            }
        }

        ulong revision;
        using (DrawingSceneReadLease2D read = first.Borrow())
        {
            revision = read.Revision;
        }

        first.ReplaceContent(node, recording);
        using (DrawingSceneReadLease2D read = first.Borrow())
        {
            Assert.Equal(revision, read.Revision);
        }

        using DrawingSceneReadLease2D other = second.Borrow();
        Assert.Same(recording, Assert.Single(((IDrawingSceneSource2D)other).Entries).Content);
        Assert.All(((IDrawingSceneSource2D)history).Entries, entry => Assert.Same(recording, entry.Content));
    }

    /// <summary>Checks ordinary immediate recording and replay never require retained layout.</summary>
    [Fact]
    public void ImmediateReplayDoesNotCreateRetainedPages()
    {
        DrawList2D recording = Record(137);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Replay(recording);
        Assert.Equal(137, canvas.End().Count);
        Assert.False(recording.HasRetainedPages);
    }

    /// <summary>Checks concurrent readers observe one published immutable layout.</summary>
    [Fact]
    public void ConcurrentReadersSharePublishedPages()
    {
        DrawList2D recording = Record(137);
        DrawingContentPages2D[] layouts = Enumerable.Range(0, 64).AsParallel().Select(_ => recording.RetainedPages).ToArray();
        Assert.All(layouts, layout => Assert.Same(layouts[0], layout));
        Assert.Equal(3, layouts[0].PageCount);
    }

    /// <summary>Checks internal page edits remain the same recording type for replay and typed consumers.</summary>
    [Fact]
    public void InternalPageUpdatesReplayAndExposeTheCorrectTypedOrder()
    {
        DrawList2D original = Record(137);
        DrawList2D changed = original.WithPage(1, original.RetainedPages.Pages[0]);
        Assert.Equal(137, changed.Count);
        Assert.Same(original.RetainedPages.Pages[0], changed.RetainedPages.Pages[0]);
        Assert.Same(original.RetainedPages.Pages[2], changed.RetainedPages.Pages[2]);
        Assert.Same(original.Commands[6], changed.Commands[70]);
        Assert.Same(original.Commands[6], changed.GetCommands<FillRectCommand2D>(70, 1).Span[0]);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Replay(changed);
        Assert.Equal(new Rect2D(6, 0, 1, 1), Assert.IsType<FillRectCommand2D>(canvas.End().Commands[70]).Rect);
        Assert.Equal(new Rect2D(70, 0, 1, 1), Assert.IsType<FillRectCommand2D>(original.Commands[70]).Rect);
    }

    /// <summary>Checks neither page storage nor a second content type leaks into the public contract.</summary>
    [Fact]
    public void PublicSceneApiAcceptsOnlyTheRecordingType()
    {
        Assert.DoesNotContain(typeof(DrawList2D).Assembly.GetExportedTypes(), type => type.Name is "DrawingContent2D" or "DrawingContentPages2D");
        Assert.Null(typeof(DrawList2D).GetMethod("WithPage", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(DrawList2D).GetProperty("RetainedPages", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(DrawList2D).GetProperty("PageCount", BindingFlags.Public | BindingFlags.Instance));
        MethodInfo insert = Assert.Single(typeof(DrawingScene2D).GetMethods(), method => method.Name == nameof(DrawingScene2D.Insert));
        Assert.Equal(typeof(DrawList2D), insert.GetParameters()[2].ParameterType);
        MethodInfo replace = Assert.Single(typeof(DrawingScene2D).GetMethods(), method => method.Name == nameof(DrawingScene2D.ReplaceContent));
        Assert.Equal(typeof(DrawList2D), replace.GetParameters()[1].ParameterType);
    }

    private static DrawList2D Record(int count)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int index = 0; index < count; index++)
        {
            canvas.FillRect(new(index, 0, 1, 1), new SRgba<float>(1, 0, 0, 1));
        }

        return canvas.End();
    }
}
