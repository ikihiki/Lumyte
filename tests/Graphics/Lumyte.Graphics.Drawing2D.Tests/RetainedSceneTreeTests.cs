using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks mutable ordered placement semantics and independent revisions.</summary>
public sealed class RetainedSceneTreeTests
{
    /// <summary>Checks nested composition, own content order, and stationary ancestor clips.</summary>
    [Fact]
    public void DeepTransformsAndClipsFollowTheirOwningNode()
    {
        using var scene = new DrawingScene2D();
        DrawList2D content = Content();
        DrawingNodeId2D group = scene.AddGroup(scene.Root, 0);
        DrawingNodeId2D nested = scene.AddGroup(group, 0);
        DrawingNodeId2D leaf = scene.Insert(nested, 0, content);
        DrawingNodeId2D last = scene.Insert(group, 1, content);
        scene.ReplaceContent(group, content);
        Matrix3x2 parent = Matrix3x2.CreateScale(2, 3) * Matrix3x2.CreateTranslation(20, 30);
        Matrix3x2 child = Matrix3x2.CreateRotation(0.4f) * Matrix3x2.CreateTranslation(4, 5);
        var local = Matrix3x2.CreateTranslation(7, 8);
        scene.SetTransform(group, parent);
        scene.SetTransform(nested, child);
        scene.SetTransform(leaf, local);
        Path2D clip = ModelValidation2D.RectanglePath(new(0, 0, 20, 20));
        scene.SetClip(group, clip, FillRule2D.EvenOdd);
        using DrawingSnapshot2D before = scene.CaptureSnapshot();
        IReadOnlyList<DrawingSceneEntry2D> entries = ((IDrawingSceneSource2D)before).Entries;
        Assert.Equal(new[] { group, leaf, last }, entries.Select(entry => entry.Node));
        Assert.Equal(local * (child * parent), entries[1].Transform);
        Assert.Equal(new DrawingSceneClip2D(clip, FillRule2D.EvenOdd, parent), Assert.Single(entries[1].Clips));
        scene.SetTransform(leaf, Matrix3x2.CreateTranslation(100, 200));
        using DrawingSceneReadLease2D read = scene.Borrow();
        DrawingSceneEntry2D changed = ((IDrawingSceneSource2D)read).Entries[1];
        Assert.Equal(parent, Assert.Single(changed.Clips).Transform);
        Assert.Equal(local * (child * parent), entries[1].Transform);
        Assert.Same(content, changed.Content);
    }

    /// <summary>Checks order-only edits preserve content and property revisions and reject cycles atomically.</summary>
    [Fact]
    public void MoveUsesFinalIndexAndSeparatesTopologyFromProperties()
    {
        using var scene = new DrawingScene2D();
        DrawList2D content = Content();
        DrawingNodeId2D first = scene.Insert(scene.Root, 0, content);
        DrawingNodeId2D second = scene.Insert(scene.Root, 1, content);
        DrawingNodeId2D third = scene.Insert(scene.Root, 2, content);
        using DrawingSnapshot2D before = scene.CaptureSnapshot();
        DrawingSceneEntry2D old = ((IDrawingSceneSource2D)before).Entries[0];
        scene.Move(first, scene.Root, 2);
        using (DrawingSceneReadLease2D read = scene.Borrow())
        {
            IReadOnlyList<DrawingSceneEntry2D> entries = ((IDrawingSceneSource2D)read).Entries;
            Assert.Equal(new[] { second, third, first }, entries.Select(entry => entry.Node));
            Assert.Equal(old.ContentRevision, entries[2].ContentRevision);
            Assert.Equal(old.PropertyRevision, entries[2].PropertyRevision);
        }

        ulong topology = scene.TopologyRevision;
        scene.Move(first, second, 0);
        Assert.Throws<ArgumentException>(() => scene.Move(second, first, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Move(first, scene.Root, 10));
        using DrawingSnapshot2D after = scene.CaptureSnapshot();
        Assert.Equal(new[] { second, first, third }, ((IDrawingSceneSource2D)after).Entries.Select(entry => entry.Node));
        Assert.True(scene.TopologyRevision > topology);
        Assert.Throws<ArgumentException>(() => scene.Move(scene.Root, second, 0));
        Assert.Throws<ArgumentException>(() => scene.Remove(scene.Root));
    }

    /// <summary>Checks every unchanged sibling move preserves the plan and both scene revisions.</summary>
    [Fact]
    public void SameParentNoOpsPreservePlanAndRevisionsForDenseFlatScene()
    {
        using var scene = new DrawingScene2D();
        DrawList2D content = Content();
        var nodes = new DrawingNodeId2D[10_000];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = scene.Insert(scene.Root, i, content);
        }

        IReadOnlyList<DrawingSceneEntry2D> plan;
        ulong revision;
        ulong topology = scene.TopologyRevision;
        using (DrawingSceneReadLease2D read = scene.Borrow())
        {
            plan = ((IDrawingSceneSource2D)read).Entries;
            revision = read.Revision;
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            scene.Move(nodes[i], scene.Root, i);
        }

        using DrawingSceneReadLease2D unchanged = scene.Borrow();
        Assert.Equal(revision, unchanged.Revision);
        Assert.Equal(topology, scene.TopologyRevision);
        Assert.Same(plan, ((IDrawingSceneSource2D)unchanged).Entries);
        Assert.Equal(nodes, plan.Select(entry => entry.Node));
        Assert.Throws<InvalidOperationException>(() => scene.Move(nodes[^1], scene.Root, nodes.Length - 1));
    }

    /// <summary>Checks invalid final indices still throw without mutating an otherwise unchanged placement.</summary>
    /// <param name="index">The invalid final insertion index.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void SameParentMoveValidatesIndexBeforeNoOp(int index)
    {
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, Content());
        using DrawingSnapshot2D before = scene.CaptureSnapshot();
        ulong topology = scene.TopologyRevision;
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => scene.Move(node, scene.Root, index));
        Assert.Equal("index", error.ParamName);
        using DrawingSceneReadLease2D read = scene.Borrow();
        Assert.Equal(before.Revision, read.Revision);
        Assert.Equal(topology, scene.TopologyRevision);
        Assert.Equal(node, Assert.Single(((IDrawingSceneSource2D)read).Entries).Node);
    }

    /// <summary>Checks inserts, removals, and moves use current sibling positions rather than stale indices.</summary>
    [Fact]
    public void SiblingMutationsKeepFinalIndexOrdering()
    {
        using var scene = new DrawingScene2D();
        DrawList2D content = Content();
        DrawingNodeId2D first = scene.Insert(scene.Root, 0, content);
        DrawingNodeId2D second = scene.Insert(scene.Root, 1, content);
        DrawingNodeId2D third = scene.Insert(scene.Root, 2, content);
        DrawingNodeId2D inserted = scene.Insert(scene.Root, 1, content);
        AssertOrder(first, inserted, second, third);
        scene.Move(second, scene.Root, 2);
        AssertOrder(first, inserted, second, third);
        scene.Remove(first);
        AssertOrder(inserted, second, third);
        scene.Move(second, scene.Root, 0);
        AssertOrder(second, inserted, third);
        scene.Move(second, scene.Root, 2);
        AssertOrder(inserted, third, second);
        scene.Move(third, scene.Root, 1);
        AssertOrder(inserted, third, second);
        scene.Remove(inserted);
        scene.Move(second, scene.Root, 0);
        AssertOrder(second, third);

        void AssertOrder(params DrawingNodeId2D[] expected)
        {
            using DrawingSceneReadLease2D read = scene.Borrow();
            Assert.Equal(expected, ((IDrawingSceneSource2D)read).Entries.Select(entry => entry.Node));
        }
    }

    /// <summary>Checks slot reuse, subtree removal, default IDs, and foreign identities.</summary>
    [Fact]
    public void StaleAndCrossSceneIdsNeverResolve()
    {
        using var scene = new DrawingScene2D();
        using var foreign = new DrawingScene2D();
        DrawingNodeId2D group = scene.AddGroup(scene.Root, 0);
        DrawingNodeId2D child = scene.Insert(group, 0, Content());
        scene.Remove(group);
        DrawingNodeId2D replacement = scene.AddGroup(scene.Root, 0);
        Assert.NotEqual(group, replacement);
        Assert.NotEqual(child, replacement);
        Assert.Throws<ArgumentException>(() => scene.SetTransform(group, Matrix3x2.Identity));
        Assert.Throws<ArgumentException>(() => scene.Remove(child));
        Assert.Throws<ArgumentException>(() => scene.Remove(default));
        Assert.Throws<ArgumentException>(() => scene.AddGroup(foreign.Root, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.AddGroup(scene.Root, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.AddGroup(scene.Root, -1));
    }

    /// <summary>Checks validation precedes edits even when a descendant clip needs an inverse.</summary>
    [Fact]
    public void InvalidTransformsAndReparentingLeaveTheSceneUnchanged()
    {
        using var scene = new DrawingScene2D();
        DrawingNodeId2D group = scene.AddGroup(scene.Root, 0);
        DrawingNodeId2D clipped = scene.Insert(group, 0, Content());
        scene.SetClip(clipped, ModelValidation2D.RectanglePath(new(0, 0, 1, 1)));
        DrawingNodeId2D singular = scene.AddGroup(scene.Root, 1);
        scene.SetTransform(singular, Matrix3x2.CreateScale(0));
        using DrawingSnapshot2D before = scene.CaptureSnapshot();
        Assert.Throws<ArgumentException>(() => scene.SetTransform(group, Matrix3x2.CreateScale(0)));
        Assert.Throws<ArgumentException>(() => scene.Move(clipped, singular, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.SetTransform(group, new(float.NaN, 0, 0, 1, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.SetClip(group, null, (FillRule2D)123));
        using DrawingSnapshot2D after = scene.CaptureSnapshot();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(((IDrawingSceneSource2D)before).Entries, ((IDrawingSceneSource2D)after).Entries);
    }

    /// <summary>Checks unchanged borrows reuse a plan and no-op edits do not advance revisions.</summary>
    [Fact]
    public void NoOpsAndContentOnlyEditsPreserveIndependentState()
    {
        using var scene = new DrawingScene2D();
        DrawList2D content = Content();
        DrawingNodeId2D first = scene.Insert(scene.Root, 0, content);
        DrawingNodeId2D second = scene.Insert(scene.Root, 1, content);
        IReadOnlyList<DrawingSceneEntry2D> plan;
        ulong revision;
        DrawingSceneEntry2D old;
        using (DrawingSceneReadLease2D read = scene.Borrow())
        {
            plan = ((IDrawingSceneSource2D)read).Entries;
            revision = read.Revision;
            old = plan[0];
        }

        scene.SetTransform(first, Matrix3x2.Identity);
        scene.SetClip(first, null);
        scene.ReplaceContent(first, content);
        scene.Move(first, scene.Root, 0);
        using (DrawingSceneReadLease2D read = scene.Borrow())
        {
            Assert.Equal(revision, read.Revision);
            Assert.Same(plan, ((IDrawingSceneSource2D)read).Entries);
        }

        ulong topology = scene.TopologyRevision;
        scene.ReplaceContent(first, Content());
        using DrawingSceneReadLease2D changed = scene.Borrow();
        IReadOnlyList<DrawingSceneEntry2D> current = ((IDrawingSceneSource2D)changed).Entries;
        Assert.Same(plan, current);
        Assert.Equal(old.PropertyRevision, current[0].PropertyRevision);
        Assert.NotEqual(old.ContentRevision, current[0].ContentRevision);
        Assert.Equal(topology, scene.TopologyRevision);
        Assert.Equal(second, current[1].Node);
        Assert.Same(content, current[1].Content);
    }

    /// <summary>Checks deep trees use iterative traversal rather than a fixed hierarchy limit.</summary>
    [Fact]
    public void DeepTreeAndEmptyContentHaveNoPrototypeLimits()
    {
        using var scene = new DrawingScene2D();
        DrawingNodeId2D parent = scene.Root;
        for (int i = 0; i < 1500; i++)
        {
            parent = scene.AddGroup(parent, 0);
        }

        var canvas = new Canvas2D();
        canvas.Begin();
        DrawList2D empty = canvas.End();
        scene.Insert(parent, 0, empty);
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        Assert.Same(empty, Assert.Single(((IDrawingSceneSource2D)snapshot).Entries).Content);
        scene.Remove(((IDrawingSceneSource2D)snapshot).Entries[0].Node);
    }

    /// <summary>Checks world overflow during reparenting does not detach or reorder the original subtree.</summary>
    [Fact]
    public void ReparentWorldOverflowIsAtomic()
    {
        using var scene = new DrawingScene2D();
        DrawingNodeId2D source = scene.AddGroup(scene.Root, 0);
        DrawingNodeId2D destination = scene.AddGroup(scene.Root, 1);
        DrawingNodeId2D leaf = scene.Insert(source, 0, Content());
        scene.SetTransform(leaf, Matrix3x2.CreateScale(2));
        scene.SetTransform(destination, Matrix3x2.CreateScale(float.MaxValue));
        using DrawingSnapshot2D before = scene.CaptureSnapshot();
        ulong topology = scene.TopologyRevision;
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Move(leaf, destination, 0));
        using DrawingSnapshot2D after = scene.CaptureSnapshot();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(topology, scene.TopologyRevision);
        Assert.Equal(((IDrawingSceneSource2D)before).Entries, ((IDrawingSceneSource2D)after).Entries);
        scene.Move(leaf, source, 0);
        using DrawingSceneReadLease2D read = scene.Borrow();
        Assert.Equal(before.Revision, read.Revision);
    }

    private static DrawList2D Content()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new Rgba<float>(1, 0, 0, 1));
        return canvas.End();
    }
}
