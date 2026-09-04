using Lumyte.Graphics.Vulkan.Samples;

using Silk.NET.Shaderc;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed class VulkanSampleShaderTests
{
    public static TheoryData<string, ShaderKind> MaterialShaders => new()
    {
        { SampleShaders.MaterialVertex, ShaderKind.VertexShader },
        { SampleShaders.MaterialPixel, ShaderKind.FragmentShader },
    };

    [Theory]
    [MemberData(nameof(MaterialShaders))]
    public void MaterialShadersCompileToSpirV(string source, ShaderKind kind)
    {
        byte[] spirV = TriangleShaders.Compile(source, kind);

        Assert.NotEmpty(spirV);
    }
}
