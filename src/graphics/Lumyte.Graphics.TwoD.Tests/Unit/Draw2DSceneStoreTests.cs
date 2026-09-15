using System.Numerics;

namespace Lumyte.Graphics.TwoD.Tests;

public sealed class Draw2DSceneStoreTests
{
    [Fact]
    public void UnchangedStoreReusesItsSnapshot()
    {
        var store = new Draw2DSceneStore();
        var node = store.CreateNode(Content(1));
        var first = store.Snapshot();

        store.SetTransform(node, Matrix3x2.Identity);

        Assert.Same(first, store.Snapshot());
    }

    [Fact]
    public void UpdatingOneNodePreservesOldContentAndUnchangedSubtrees()
    {
        var store = new Draw2DSceneStore();
        var ids = Enumerable.Range(0, 32).Select(i => store.CreateNode(Content(i))).ToArray();
        var old = store.Snapshot();
        var oldChildren = Descendants(old).ToHashSet();

        store.SetContent(ids[10], Content(90));
        var next = store.Snapshot();

        Assert.Equal(Enumerable.Range(0, 32).Select(i => (float)i), Colors(old));
        Assert.Equal(Enumerable.Range(0, 32).Select(i => i == 10 ? 90f : i), Colors(next));
        Assert.True(Descendants(next).Count(oldChildren.Contains) >= 50, "An edit should share content and unrelated persistent subtrees.");
    }

    [Fact]
    public void OrderChangesPreserveStableCreationOrderForTies()
    {
        var store = new Draw2DSceneStore();
        var a = store.CreateNode(Content(1));
        var b = store.CreateNode(Content(2));
        store.CreateNode(Content(3));

        store.SetOrder(b, -1);
        store.SetVisible(a, false);

        Assert.Equal(new float[] { 2, 3 }, Colors(store.Snapshot()));
    }

    [Fact]
    public void RemovedAndForeignNodeIdsAreRejected()
    {
        var store = new Draw2DSceneStore();
        var removed = store.CreateNode(Content(1));
        store.Remove(removed);
        store.CreateNode(Content(2));
        var foreign = new Draw2DSceneStore().CreateNode(Content(3));

        Assert.Equal("node", Assert.Throws<ArgumentException>(() => store.SetVisible(removed, true)).ParamName);
        Assert.Equal("node", Assert.Throws<ArgumentException>(() => store.Remove(foreign)).ParamName);
    }

    [Fact]
    public void RemovingEveryNodeProducesAnEmptyScene()
    {
        var store = new Draw2DSceneStore();
        var nodes = Enumerable.Range(0, 63).Select(i => store.CreateNode(Content(i))).ToArray();

        foreach (var index in Enumerable.Range(0, 63).Select(i => i * 17 % 63))
        { store.Remove(nodes[index]); }

        Assert.Equal(0, store.Count);
        Assert.Empty(store.Snapshot().Commands);
    }

    private static Draw2DScene Content(float red)
    { using var builder = new Draw2DSceneBuilder(); builder.FillRectangle(new(0, 0, 1, 1), Brush.Solid(new(red, 0, 0))); return builder.Finish(); }
    private static IEnumerable<Draw2DScene> Descendants(Draw2DScene scene)
    { yield return scene; foreach (var child in scene.ChildScenes) { foreach (var descendant in Descendants(child)) { yield return descendant; } } }
    private static IEnumerable<float> Colors(Draw2DScene scene)
    {
        foreach (var command in scene.Commands)
        {
            if (command is Draw2DShapeCommand shape)
            { yield return Assert.IsType<SolidBrush>(shape.Brush).Color.Red; }
            if (command is Draw2DSceneCommand child)
            { foreach (var color in Colors(child.Content)) { yield return color; } }
        }
    }
}
