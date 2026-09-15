using System.Numerics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes.Tests;

public sealed class ImageFilterTests
{
    [Fact]
    public void BlurRadiusChangesWithoutRecompiling()
    {
        var graph = new GpuRenderGraph();
        var source = graph.CreateTexture("source", new(3, 2, GpuFormat.Rgba8Unorm));
        graph.AddClearPass("clear", new(source, TextureClearValue.Color(Vector4.One)));
        var radius = graph.CreateInput("radius", BlurRadiusInputContract.Instance, 1);
        var request = new BlurPassRequest(source, radius);
        var result = graph.AddBlurPass("blur", request);
        graph.ExportTexture(result.Color);
        var plan = graph.Compile();
        var values = plan.CreateBindings();
        values.Set(radius, 4);

        Assert.Equal(4, plan.Passes.Single(p => p.Name == "blur").GetInput(request.Radius, values.Build()));
        Assert.Equal(GpuFormat.Rgba16Float, result.Color.Description.Format);
    }
    [Fact]
    public void CompositeSnapshotsLayerOrder()
    {
        var graph = new GpuRenderGraph();
        var source = graph.CreateTexture("source", new(2, 2, GpuFormat.Rgba8Unorm));
        var target = graph.CreateTexture("target", source.Description);
        graph.AddClearPass("clear", new(source, TextureClearValue.Color(Vector4.One)));
        var layers = new List<CompositeLayer> { new(source, .5f) };
        var request = new CompositePassRequest(target, layers, TargetContent.Clear);
        graph.AddCompositePass("composite", request);
        graph.ExportTexture(target);

        layers.Clear();
        var plan = graph.Compile();

        Assert.Contains(plan.Passes.Single(p => p.Name == "composite").Uses, u => u.Resource == source && u.Access == GpuRenderGraphAccess.Read);
    }
    [Theory]
    [InlineData(TargetContent.Clear, GpuRenderGraphAccess.Write)]
    [InlineData(TargetContent.Preserve, GpuRenderGraphAccess.ReadWrite)]
    public void EmptyCompositeDeclaresItsTargetContent(TargetContent content, GpuRenderGraphAccess access)
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(2, 2, GpuFormat.Rgba8Unorm));
        graph.AddClearPass("clear", new(target, TextureClearValue.Color(Vector4.One)));
        graph.AddCompositePass("composite", new(target, [], content));
        graph.ExportTexture(target);

        var pass = graph.Compile().Passes.Single(p => p.Name == "composite");

        Assert.Equal(access, Assert.Single(pass.Uses).Access);
    }
    [Fact]
    public void CompositeRejectsSamplingItsOwnTarget()
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(2, 2, GpuFormat.Rgba8Unorm));
        var error = Assert.Throws<ArgumentException>(() => graph.AddCompositePass("composite", new(target, [new(target)])));
        Assert.Contains("distinct", error.Message);
    }
    [Theory]
    [InlineData(-1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    public void CompositeRejectsInvalidOpacity(float opacity)
    { Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => CompositeOpacityInputContract.Instance.Snapshot(opacity)).ParamName); }
    [Fact]
    public void BlurRejectsNegativeRadius()
    { Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => BlurRadiusInputContract.Instance.Snapshot(-1)).ParamName); }
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ToneMapRejectsNonFiniteExposure(float exposure)
    { Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => ExposureInputContract.Instance.Snapshot(exposure)).ParamName); }
}
