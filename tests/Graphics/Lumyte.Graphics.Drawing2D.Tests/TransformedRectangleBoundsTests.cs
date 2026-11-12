using System.Numerics;
using System.Reflection;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Protects caller-specific bounds policy while shared math reduces the corners.</summary>
public sealed class TransformedRectangleBoundsTests
{
    /// <summary>Rectangle batches validate finite edges, not an overflowing reconstructed extent.</summary>
    [Fact]
    public void BatchIntersectionPreservesFiniteEdgesAndPadding()
    {
        Type batch = typeof(PreparedRectangleBatch2D).GetNestedTypes(BindingFlags.NonPublic).Single(t => t.GetMethod("Intersect", BindingFlags.NonPublic | BindingFlags.Static) is not null);
        MethodInfo intersect = batch.GetMethod("Intersect", BindingFlags.NonPublic | BindingFlags.Static)!;

        // Finite corners span more than float.MaxValue without reconstructing their extent.
        object[] args = [new Vector2(-float.MaxValue), new Vector2(float.MaxValue), new Rect2D(-float.MaxValue / 2, 3, float.MaxValue, 4), Matrix3x2.CreateScale(2, 1)];
        intersect.Invoke(null, args);
        Assert.Equal(new Vector2(-float.MaxValue, 2), (Vector2)args[0]);
        Assert.Equal(new Vector2(float.MaxValue, 8), (Vector2)args[1]);
    }

    /// <summary>Render graph bounds transform each corner before pixel scaling and integer rounding.</summary>
    [Fact]
    public void RenderGraphPixelBoundsMatchHistoricalEvaluationOrder()
    {
        MethodInfo method = typeof(RenderGraphRenderer2D).GetMethod("PixelBounds", BindingFlags.NonPublic | BindingFlags.Static)!;
        var random = new Random(233);
        for (int i = 0; i < 200; i++)
        {
            Rect2D rect = new(Next(), Next(), Next(), Next());
            Matrix3x2 transform = new(Next(), Next(), Next(), Next(), Next(), Next());
            float scale = Next();
            Vector2 a = Vector2.Transform(new(rect.X, rect.Y), transform) * scale;
            Vector2 b = Vector2.Transform(new(rect.X + rect.Width, rect.Y), transform) * scale;
            Vector2 c = Vector2.Transform(new(rect.X, rect.Y + rect.Height), transform) * scale;
            Vector2 d = Vector2.Transform(new(rect.X + rect.Width, rect.Y + rect.Height), transform) * scale;
            var low = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            var high = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            int x = checked((int)Math.Floor(low.X));
            int y = checked((int)Math.Floor(low.Y));
            var expected = new EffectPixelRect(x, y, checked((int)Math.Ceiling(high.X) - x), checked((int)Math.Ceiling(high.Y) - y));
            Assert.Equal(expected, (EffectPixelRect)method.Invoke(null, [rect, transform, scale])!);
        }

        float Next() => (random.NextSingle() - 0.5f) * 100;
    }
}
