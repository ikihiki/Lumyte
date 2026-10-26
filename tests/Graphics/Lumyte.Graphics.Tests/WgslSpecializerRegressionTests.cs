using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks texture reference lowering before malformed WGSL can reach a native driver.</summary>
public sealed class WgslSpecializerRegressionTests
{
    /// <summary>Preserves value and pointer receiver ABIs, including Slang's uniform-layout structures.</summary>
    /// <param name="texturePointer">Whether the texture receiver is a function pointer.</param>
    /// <param name="samplerPointer">Whether the sampler receiver is a function pointer.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TextureDispatchUsesCorrectReceiverFields(bool texturePointer, bool samplerPointer)
    {
        string source = Shader(texturePointer, samplerPointer);
        string result = WgslBindingSpecializer.Specialize(source, 2, 2, 0, 0);
        string texture = texturePointer ? "(*textureRef_0).Id_1" : "textureRef_0.Id_1";
        string sampler = samplerPointer ? "(*samplerRef_0).Id_2" : "samplerRef_0.Id_2";
        Assert.Contains("switch lumyteMap_0[" + texture + "].x", result, StringComparison.Ordinal);
        Assert.Contains("switch lumyteMap_0[" + sampler + "].y", result, StringComparison.Ordinal);
        Assert.Contains("textureSampleGrad(lumyteTexture1_0, lumyteSampler1_0, uv_0, dx_0, dy_0)", result, StringComparison.Ordinal);
        Assert.DoesNotContain("textureRef_0.]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("samplerRef_0.]", result, StringComparison.Ordinal);
    }

    /// <summary>Rejects unknown reference layouts before emitting invalid member accesses.</summary>
    [Fact]
    public void MissingReferenceIdentifierIsRejected()
    {
        string source = Shader(true, true).Replace("Id_1 : u32", "Unknown_1 : u32", StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => WgslBindingSpecializer.Specialize(source, 1, 1, 0, 0));
    }

    /// <summary>Rejects missing mapping resources instead of creating an invalid switch expression.</summary>
    [Fact]
    public void MissingMappingResourceIsRejected()
    {
        string source = Shader(false, false).Replace("@binding(9) @group(0) var<storage, read> lumyteMap_0 : array<vec4<u32>>;", string.Empty, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => WgslBindingSpecializer.Specialize(source, 1, 1, 0, 0));
    }

    private static string Shader(bool texturePointer, bool samplerPointer)
    {
        string texture = texturePointer ? "ptr<function, GpuTextureRef_std140_0>" : "GpuTextureRef_std140_0";
        string sampler = samplerPointer ? "ptr<function, GpuSamplerRef_std140_0>" : "GpuSamplerRef_std140_0";
        return $$"""
            struct GpuTextureRef_std140_0
            {
                @align(16) Id_1 : u32,
                @align(4) Offset_1 : u32,
                @align(8) Count_1 : u32,
                @align(4) Reserved_1 : u32,
            };
            struct GpuSamplerRef_std140_0
            {
                @align(16) Id_2 : u32,
                @align(4) Offset_2 : u32,
                @align(8) Count_2 : u32,
                @align(4) Reserved_2 : u32,
            };
            @binding(0) @group(0) var lumyteTexture0_0 : texture_2d<f32>;
            @binding(1) @group(0) var lumyteSampler0_0 : sampler;
            @binding(9) @group(0) var<storage, read> lumyteMap_0 : array<vec4<u32>>;
            fn LumyteSampleGrad_0(textureRef_0 : {{texture}}, samplerRef_0 : {{sampler}}, uv_0 : vec2<f32>, dx_0 : vec2<f32>, dy_0 : vec2<f32>) -> vec4<f32>
            {
                return vec4<f32>(0.0);
            }
            """;
    }
}
