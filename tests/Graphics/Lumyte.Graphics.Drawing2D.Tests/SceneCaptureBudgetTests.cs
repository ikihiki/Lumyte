using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Capture admission accounts actual placement and clip replication before transformation.</summary>
public sealed class SceneCaptureBudgetTests
{
    /// <summary>Empty placements still own scene-key and page capture storage.</summary>
    [Fact]
    public void EmptyPlacementsCannotBypassCaptureBudget()
    {
        var empty = new DrawList2D([]);
        DrawingSceneEntry2D[] entries = Enumerable.Range(0, 128).Select(_ => Entry(empty, [])).ToArray();
        Assert.Throws<NotSupportedException>(() => DrawingSceneCapture2D.Capture(new Source(entries), default, 65536));
    }

    /// <summary>A large captured local stack is charged before its combined snapshot is allocated.</summary>
    [Fact]
    public void LocalClipElementsAreIncludedBeforeCapture()
    {
        Path2D path = ModelValidation2D.RectanglePath(new(0, 0, 4, 4));
        var clip = new ClipStack2D(Enumerable.Repeat(new ClipElement2D(path, Matrix3x2.Identity, FillRule2D.NonZero), 1024).ToArray());
        DrawList2D content = Content([clip]);
        Assert.Throws<NotSupportedException>(() => DrawingSceneCapture2D.Capture(new Source([Entry(content, [])]), default, 65536));
    }

    /// <summary>Every distinct local stack replicates the ancestor clip list.</summary>
    [Fact]
    public void ParentClipsAreChargedForEveryDistinctLocalStack()
    {
        Path2D path = ModelValidation2D.RectanglePath(new(0, 0, 4, 4));
        DrawingSceneClip2D[] parents = Enumerable.Repeat(new DrawingSceneClip2D(path, FillRule2D.NonZero, Matrix3x2.Identity), 64).ToArray();
        DrawList2D content = Content(Enumerable.Range(0, 16).Select(_ => new ClipStack2D([])).ToArray());
        Assert.Throws<NotSupportedException>(() => DrawingSceneCapture2D.Capture(new Source([Entry(content, parents)]), default, 131072));
    }

    /// <summary>Even an empty scene has a bounded capture owner, propagated into later preparation.</summary>
    [Fact]
    public void EmptyCaptureHasAnAdmittedOwner()
    {
        var source = new Source([]);
        Assert.Throws<NotSupportedException>(() => DrawingSceneCapture2D.Capture(source, default, 4095));
        Assert.Equal(4096, DrawingSceneCapture2D.Capture(source, default, 4096).CapturedWorkingBytes);
    }

    private static DrawingSceneEntry2D Entry(DrawList2D content, DrawingSceneClip2D[] clips)
        => new(default, content, Matrix3x2.CreateTranslation(1, 2), clips, 0, 0);

    private static DrawList2D Content(ClipStack2D[] clips)
        => new(clips.Select(clip => new FillRectCommand2D(new(0, 0, 4, 4), PaintGraph2D.Solid(SRgba<float>.White), new(Matrix3x2.Identity, clip))));

    private sealed class Source(IReadOnlyList<DrawingSceneEntry2D> entries) : IDrawingSceneSource2D
    {
        public ulong Revision => 0;

        public IReadOnlyList<DrawingSceneEntry2D> Entries => entries;
    }
}
