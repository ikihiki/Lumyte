using System.Numerics;

namespace Lumyte.Graphics.TwoD.Tests;

public sealed class SceneTests
{
    [Fact]
    public void RemovedNodeCannotMutateItsReplacement()
    {
        var scene = new Scene();
        NodeId removed = scene.CreateNode();
        scene.SetContent(removed, SceneContent.Rectangle(new(0, 0, 10, 10), Brush.Solid(Color.White)));
        scene.Remove(removed);
        NodeId replacement = scene.CreateNode();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => scene.SetTransform(removed, Matrix3x2.CreateTranslation(1, 2)));

        Assert.NotEqual(removed, replacement);
        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
