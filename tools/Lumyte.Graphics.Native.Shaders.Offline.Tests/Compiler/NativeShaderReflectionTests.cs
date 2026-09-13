using System.Text.Json;

namespace Lumyte.Graphics.Native.Shaders.Offline.Tests;

public sealed class NativeShaderReflectionTests
{
    [Theory]
    [InlineData("GpuAddress", "uint64", 8, NativeShaderInputFieldKind.GpuAddress, NativeShaderResourceKind.Buffer)]
    [InlineData("View", "uint32", 4, NativeShaderInputFieldKind.DescriptorIndex, NativeShaderResourceKind.View)]
    [InlineData("Sampler", "uint32", 4, NativeShaderInputFieldKind.DescriptorIndex, NativeShaderResourceKind.Sampler)]
    public void ExplicitAttributesIdentifyResourceReferences(string semantic, string scalar, uint size,
        NativeShaderInputFieldKind kind, NativeShaderResourceKind resourceKind)
    {
        using JsonDocument reflection = Fixture(scalar, size, semantic);

        NativeReflectedLayout result = NativeShaderReflection.Root(reflection.RootElement, "root");

        Assert.Equal(new NativeShaderInputField("value", kind, 0, size, resourceKind), Assert.Single(result.Fields));
    }

    [Fact]
    public void UnsignedIntegerDoesNotImplyResourceReference()
    {
        using JsonDocument reflection = Fixture("uint64", 8, null);

        NativeReflectedLayout result = NativeShaderReflection.Root(reflection.RootElement, "root");

        Assert.Equal(new NativeShaderInputField("value", NativeShaderInputFieldKind.Scalar, 0, 8), Assert.Single(result.Fields));
    }

    [Theory]
    [InlineData("GpuAddress", "uint32", 4)]
    [InlineData("Sampler", "uint64", 8)]
    [InlineData("Unknown", "uint32", 4)]
    public void IncompatibleAnnotationFailsWithoutReinterpretation(string semantic, string scalar, uint size)
    {
        using JsonDocument reflection = Fixture(scalar, size, semantic);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => NativeShaderReflection.Root(reflection.RootElement, "root"));

        Assert.Contains("LumyteResource", error.Message);
    }

    [Fact]
    public void ExplicitEmptyRootHasNoFields()
    {
        using JsonDocument reflection = JsonDocument.Parse("{}");

        NativeReflectedLayout result = NativeShaderReflection.Root(reflection.RootElement, null);

        Assert.Equal(0u, result.Size);
        Assert.Empty(result.Fields);
    }

    [Theory]
    [InlineData("pushConstantBuffer")]
    [InlineData("constantBuffer")]
    public void ExplicitEmptyRootRejectsReflectedDirectRoot(string bindingKind)
    {
        using JsonDocument source = Fixture("uint32", 4, null);
        using JsonDocument reflection = JsonDocument.Parse(source.RootElement.GetRawText().Replace("pushConstantBuffer", bindingKind, StringComparison.Ordinal));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => NativeShaderReflection.Root(reflection.RootElement, null));

        Assert.Contains("RootParameterName", error.Message);
    }

    [Fact]
    public void ExplicitEmptyRootRejectsImplicitGlobalConstantBuffer()
    {
        using JsonDocument reflection = JsonDocument.Parse("""{"globalScope":{"kind":"constantBuffer"}}""");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => NativeShaderReflection.Root(reflection.RootElement, null));

        Assert.Contains("RootParameterName", error.Message);
    }

    private static JsonDocument Fixture(string scalar, uint size, string? resource)
    {
        string attribute = resource is null ? "" : $"\"userAttribs\":[{{\"name\":\"LumyteResource\",\"arguments\":[\"{resource}\"]}}],";
        return JsonDocument.Parse("""
            {"parameters":[{"name":"root","binding":{"kind":"pushConstantBuffer","index":0},"type":{"elementType":{
              "kind":"struct","sizes":[{"kind":"uniform","value":SIZE,"alignment":SIZE}],
              "fields":[{"name":"value",ATTRIBUTE"type":{"kind":"scalar","scalarType":"SCALAR"},
                "binding":{"kind":"uniform","offset":0,"size":SIZE}}]}}}]}
            """.Replace("SIZE", size.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("ATTRIBUTE", attribute, StringComparison.Ordinal).Replace("SCALAR", scalar, StringComparison.Ordinal));
    }
}
