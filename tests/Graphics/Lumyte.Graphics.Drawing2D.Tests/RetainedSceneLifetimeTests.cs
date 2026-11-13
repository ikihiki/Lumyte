using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks read guards, explicit history, and disposed-source validation.</summary>
public sealed class RetainedSceneLifetimeTests
{
    /// <summary>Checks all live borrows must end before even no-op mutations.</summary>
    [Fact]
    public void EveryBorrowBlocksEveryMutation()
    {
        using var scene = new DrawingScene2D();
        DrawList2D content = Content();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, content);
        DrawingSceneReadLease2D first = scene.Borrow();
        DrawingSceneReadLease2D second = scene.Borrow();
        Assert.Throws<InvalidOperationException>(() => scene.SetTransform(node, Matrix3x2.Identity));
        Assert.Throws<InvalidOperationException>(() => scene.ReplaceContent(node, content));
        Assert.Throws<InvalidOperationException>(() => scene.SetClip(node, null));
        Assert.Throws<InvalidOperationException>(() => scene.Move(node, scene.Root, 0));
        Assert.Throws<InvalidOperationException>(() => scene.Remove(node));
        Assert.Throws<InvalidOperationException>(() => scene.AddGroup(scene.Root, 0));
        Assert.Throws<InvalidOperationException>(() => scene.Insert(scene.Root, 0, content));
        first.Dispose();
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.Revision);
        Assert.Throws<ObjectDisposedException>(() => ((IDrawingSceneSource2D)first).Entries);
        Assert.Throws<InvalidOperationException>(() => scene.Remove(node));
        second.Dispose();
        scene.SetTransform(node, Matrix3x2.CreateTranslation(10, 20));
        using DrawingSceneReadLease2D current = scene.Borrow();
        Assert.Equal(Matrix3x2.CreateTranslation(10, 20), Assert.Single(((IDrawingSceneSource2D)current).Entries).Transform);
    }

    /// <summary>Checks historical metadata is copied while pages are shared across edits and scene disposal.</summary>
    [Fact]
    public void ExplicitSnapshotSurvivesEditsRemovalAndSceneDisposal()
    {
        var scene = new DrawingScene2D();
        DrawList2D content = Content();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, content);
        using DrawingSnapshot2D old = scene.CaptureSnapshot();
        ulong revision = old.Revision;
        scene.ReplaceContent(node, Content());
        scene.SetTransform(node, Matrix3x2.CreateTranslation(10, 20));
        scene.Remove(node);
        scene.Dispose();
        DrawingSceneEntry2D entry = Assert.Single(((IDrawingSceneSource2D)old).Entries);
        Assert.Equal(node, entry.Node);
        Assert.Equal(Matrix3x2.Identity, entry.Transform);
        Assert.Same(content, entry.Content);
        Assert.Equal(revision, old.Revision);
        Assert.Throws<ObjectDisposedException>(() => scene.Borrow());
        Assert.Throws<ObjectDisposedException>(() => scene.CaptureSnapshot());
        Assert.Throws<ObjectDisposedException>(() => scene.AddGroup(scene.Root, 0));
        old.Dispose();
        Assert.Throws<ObjectDisposedException>(() => old.Revision);
        Assert.Throws<ObjectDisposedException>(() => ((IDrawingSceneSource2D)old).Entries);
    }

    /// <summary>Checks disposal of the scene preserves current borrowers but denies new acquisitions.</summary>
    [Fact]
    public void ExistingBorrowRemainsReadableAfterSceneDisposal()
    {
        var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, Content());
        using DrawingSceneReadLease2D read = scene.Borrow();
        ulong revision = read.Revision;
        scene.Dispose();
        Assert.Single(((IDrawingSceneSource2D)read).Entries);
        Assert.Equal(revision, read.Revision);
        Assert.Throws<ObjectDisposedException>(() => scene.Borrow());
        read.Dispose();
        Assert.Throws<ObjectDisposedException>(() => read.Revision);
        Assert.Throws<ObjectDisposedException>(() => ((IDrawingSceneSource2D)read).Entries);
    }

    private static DrawList2D Content()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new SrgbRgba<float>(1, 0, 0, 1));
        return canvas.End();
    }
}
