using System.Buffers.Binary;

using Lumyte.Graphics.RenderGraph.Conformance;

namespace Lumyte.Graphics.DirectX12.Tests;

// These checks exercise only the reference renderer, without creating a GPU device.
public sealed class SkiaTwoDReferenceTests
{
    [Theory]
    [InlineData(22, 4, 0.398114f)]
    [InlineData(23, 4, 0.724697f)]
    [InlineData(18, 6, 0.664053f)]
    public void CircleCoverageAgreesWithItsGeometricArea(int x, int y, float area)
    {
        var scene = TwoDScenarios.Create("radial-gradient");

        byte[] reference = SkiaTwoDReference.Render(scene);

        // Circle radius 29, center (32,32). The area values are the independent
        // integral of its intersection with the specified unit pixel squares.
        float alpha = reference[(y * TwoDRenderConsumer.Size + x) * 4 + 3] / 255f;
        Assert.InRange(alpha, area - 0.04f, area + 0.04f);
    }

    [Fact]
    public void HalfFloatReferencePreservesHdrThroughLayerOpacityAndBlur()
    {
        var scene = TwoDScenarios.Create("hdr-layer");

        byte[] reference = SkiaTwoDReference.RenderHalf(scene);

        int center = (32 * TwoDRenderConsumer.Size + 32) * 8;
        float red = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(reference.AsSpan(center)));
        Assert.InRange(red, 1.79f, 1.81f);
    }
}
