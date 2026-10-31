using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Checks geometry and portable shader artifacts without requiring a GPU.</summary>
public sealed class DrawingTests
{
    /// <summary>Checks rectangle positions, winding, UV orientation and shader serialization.</summary>
    [Fact]
    public void RectangleHasCounterClockwiseTriangles()
    {
        DrawingVertex[] vertices = DrawingPrimitives.Rectangle(new(2, 3), new(4, 5), Vector4.One);
        Assert.Equal(6, vertices.Length);
        Assert.Equal(new Vector3(2, 3, 0), vertices[0].Position);
        Assert.Equal(new Vector3(6, 8, 0), vertices[2].Position);
        Assert.Equal(new Vector2(0, 1), vertices[0].TexCoord);
        Assert.Equal(new Vector2(1, 0), vertices[2].TexCoord);
        AssertPositiveWinding(vertices);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in vertices[0]);
        Assert.Equal("DrawingVertex", snapshot.ShaderTypeName);
        Assert.Equal(3, snapshot.Values.Count);
    }

    /// <summary>Checks the circle closes exactly and all triangles face forward.</summary>
    [Fact]
    public void CircleClosesAndPreservesRadius()
    {
        DrawingVertex[] vertices = DrawingPrimitives.Circle(new(2, 3), 4, 16, Vector4.One);
        Assert.Equal(48, vertices.Length);
        Assert.Equal(vertices[1], vertices[^1]);
        AssertPositiveWinding(vertices);
        for (int i = 0; i < vertices.Length; i += 3)
        {
            Assert.Equal(new Vector3(2, 3, 0), vertices[i].Position);
            Assert.InRange(Vector3.Distance(vertices[i].Position, vertices[i + 1].Position), 3.9999f, 4.0001f);
        }
    }

    /// <summary>Checks invalid geometry cannot silently generate nonfinite vertices.</summary>
    [Fact]
    public void InvalidGeometryIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawingPrimitives.Rectangle(Vector2.Zero, new(0, 1), Vector4.One));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawingPrimitives.Circle(Vector2.Zero, float.NaN, 16, Vector4.One));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawingPrimitives.Circle(Vector2.Zero, 1, 2, Vector4.One));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawingPrimitives.Line(new(float.PositiveInfinity, 0, 0), Vector3.Zero, Vector4.One));
    }

    /// <summary>Checks every drawing entry is embedded for all supported targets.</summary>
    [Fact]
    public void DrawingShadersContainEveryTarget()
    {
        ShaderArtifact[] artifacts = [DrawingShaders.LoadColorVertex(), DrawingShaders.LoadColorFragment(), DrawingShaders.LoadTextureVertex(), DrawingShaders.LoadTextureFragment(), DrawingShaders.LoadModelVertex(), DrawingShaders.LoadModelFragment()];
        for (int i = 0; i < artifacts.Length; i++)
        {
            foreach (ShaderTarget target in Enum.GetValues<ShaderTarget>())
            {
                ShaderTargetData data = artifacts[i].GetTarget(target);
                Assert.Equal(i % 2 == 0 ? ShaderStage.Vertex : ShaderStage.Fragment, data.Stage);
                Assert.NotEmpty(data.Code);
                Assert.Equal("row-major", data.MatrixLayout);
            }
        }
    }

    private static void AssertPositiveWinding(DrawingVertex[] vertices)
    {
        for (int i = 0; i < vertices.Length; i += 3)
        {
            var cross = Vector3.Cross(vertices[i + 1].Position - vertices[i].Position, vertices[i + 2].Position - vertices[i].Position);
            Assert.True(cross.Z > 0);
        }
    }
}
