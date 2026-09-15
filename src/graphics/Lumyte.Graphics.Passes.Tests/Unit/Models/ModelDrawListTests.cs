using System.Numerics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes.Tests;

public sealed class ModelDrawListTests
{
    private static ModelDrawItem Draw() => new(new(new("geometry",0),ModelTopology.Triangles,
        new(new(new("positions",0),new[] { Vector3.Zero,Vector3.UnitX,Vector3.UnitY }))),new(new("material",0)),Matrix4x4.Identity);
    [Fact]
    public void OldSnapshotsSurviveReplacementRemovalAndReordering()
    {
        var list = new ModelDrawList(); var original = Draw();
        var first = list.Add(original); var second = list.Add(original with { LocalToWorld = Matrix4x4.CreateTranslation(1,0,0) });
        var before = list.Snapshot();

        list.Set(first,original with { Visible = false }); list.MoveBefore(second,first);
        var reordered = list.Snapshot(); list.Remove(first);

        Assert.Collection(before.Items,x => Assert.Same(original,x),x => Assert.Equal(1,x.LocalToWorld.M41));
        Assert.Collection(reordered.Items,x => Assert.Equal(1,x.LocalToWorld.M41),x => Assert.False(x.Visible));
        Assert.Single(list.Snapshot().Items);
    }
    [Fact]
    public void UnchangedValuesReuseTheSnapshot()
    {
        var list = new ModelDrawList(); var draw = Draw(); var handle = list.Add(draw); var before = list.Snapshot();
        list.Set(handle,draw with { });
        Assert.Same(before,list.Snapshot());
    }
    [Fact]
    public void RemovedAndForeignHandlesAreRejected()
    {
        var list = new ModelDrawList(); var handle = list.Add(Draw()); list.Remove(handle);
        Assert.Throws<ArgumentException>(() => list.Set(handle,Draw()));
        Assert.Throws<ArgumentException>(() => list.Remove(new ModelDrawList().Add(Draw())));
    }
    [Fact]
    public void OneChangeSharesUnmodifiedSubtreesInALargeCollection()
    {
        var list = new ModelDrawList(); var draw = Draw(); ModelDrawHandle changed = default;
        for (int i = 0; i < 100_000; i++) { var handle = list.Add(draw); if (i == 50_000) { changed = handle; } }
        var before = list.Snapshot();

        list.Set(changed,draw with { LocalToWorld = Matrix4x4.CreateTranslation(1,2,3) });
        var after = list.Snapshot();

        Assert.Equal(100_000,after.Count);
        Assert.InRange(ChangedNodes(before.Root,after.Root),1,100);
        Assert.All(before.Items,item => Assert.Same(draw,item));
    }
    private static int ChangedNodes(ModelDrawNode? before,ModelDrawNode? after) => ReferenceEquals(before,after) ? 0
        : 1+ChangedNodes(before?.Left,after?.Left)+ChangedNodes(before?.Right,after?.Right);
    [Fact]
    public void AttributeRangeEditsOwnInputAndPreserveThePreviousValue()
    {
        Vector3[] input = [Vector3.Zero,Vector3.One]; var original = new ModelVertexAttributeData<Vector3>(new("p",0),input);
        input[0] = Vector3.UnitX;
        var edited = original.WithRange(new("p",1),1,[Vector3.UnitY]);
        Assert.Equal(new[] { Vector3.Zero,Vector3.One },original.Values);
        Assert.Equal(new[] { Vector3.Zero,Vector3.UnitY },edited.Values);
    }
    [Fact]
    public void AttributesRejectInconsistentVertexCounts()
    {
        var positions = new ModelVertexAttributeData<Vector3>(new("p",0),[Vector3.Zero]);
        var normals = new ModelVertexAttributeData<Vector3>(new("n",0),[]);
        Assert.Throws<ArgumentException>(() => new ModelVertexData(positions,normals));
    }
    [Fact]
    public void RepeatedMovesDoNotExhaustOrderingPrecision()
    {
        var list = new ModelDrawList(); var draw = Draw();
        list.Add(draw);
        var a = list.Add(draw with { LocalToWorld = Matrix4x4.CreateTranslation(1,0,0) });
        var b = list.Add(draw with { LocalToWorld = Matrix4x4.CreateTranslation(2,0,0) });
        for (int i = 0; i < 300; i++) { list.MoveBefore(a,b); (a,b) = (b,a); }
        Assert.Equal(new[] { 0f,2f,1f },list.Snapshot().Items.Select(x => x.LocalToWorld.M41));
    }
}
