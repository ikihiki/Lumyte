namespace Lumyte.Graphics.Native.Shaders.Tests;

public sealed class NativeShaderInputFieldTests
{
    [Theory]
    [InlineData(NativeShaderResourceKind.View)]
    [InlineData(NativeShaderResourceKind.Sampler)]
    public void LayoutRetainsExplicitDescriptorReferenceKind(NativeShaderResourceKind kind)
    {
        var field = new NativeShaderInputField("resource", NativeShaderInputFieldKind.DescriptorIndex, 4, 4, kind);

        var layout = new NativeShaderInputLayout("Root", 8, 4, [field]);

        Assert.Equal(field, Assert.Single(layout.Fields));
    }
}
