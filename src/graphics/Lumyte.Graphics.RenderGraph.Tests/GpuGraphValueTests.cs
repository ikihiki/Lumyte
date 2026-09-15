namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class GpuGraphValueTests
{
    [Fact]
    public void ConstantIsAvailableToExternalFeatureContracts()
    {
        var value = GpuGraphValue<string>.Constant("scene");

        Assert.True(value.TryGetConstant(out var constant));
        Assert.Equal("scene", constant);
    }

    [Fact]
    public void SlotDoesNotExposeItsDefaultAsAConstant()
    {
        var graph = new GpuRenderGraph();
        GpuGraphValue<string> value = graph.CreateInput("slot", new StringInput(), "default");

        Assert.False(value.TryGetConstant(out var constant));
        Assert.Null(constant);
    }

    private sealed class StringInput : IGpuGraphInputContract<string>
    {
        public string Snapshot(string value) => value;
        public void Retain(GpuRenderInputRetentionContext context, string snapshot) { }
    }
}
