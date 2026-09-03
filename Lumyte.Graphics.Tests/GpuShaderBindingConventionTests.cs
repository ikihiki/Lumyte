using System.Security.Cryptography;

using Lumyte.Graphics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Tests;

public sealed class GpuShaderBindingConventionTests
{
    [Fact]
    public void AbiDefinesOneLogicalTablePerResourceKind()
    {
        Assert.Equal(0, GpuShaderBindingConvention.TextureTable);
        Assert.Equal(1, GpuShaderBindingConvention.SamplerTable);
        Assert.Equal(2, GpuShaderBindingConvention.BufferTable);
        Assert.Equal(128, GpuShaderBindingConvention.RootDataSize);
        Assert.Equal(SHA256.HashSizeInBytes, GpuShaderBindingConvention.AbiHash.Length);
    }

    [Fact]
    public void ShaderBindingsDeclareSparseTextureAndBufferIndices()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture texture = graph.ImportTexture(
            "texture",
            new GpuTextureHandle(1),
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        GpuRenderGraphBuffer buffer = graph.ImportBuffer(
            "buffer",
            new GpuBufferHandle(2, 64),
            new(64, GpuBufferUsage.ShaderData));
        var bindings = new GpuRenderGraphShaderBindings(
            textures:
            [
                new(9, texture, GpuStage.PixelShader),
                new(3, texture, GpuStage.VertexShader),
            ],
            buffers: [new(7, buffer, GpuStage.VertexShader)]);
        graph.AddPass(
                "draw",
                0,
                static (_, _) => { },
                GpuRenderGraphPassFlags.NeverCull)
            .UseShaderBindings(bindings);

        GpuRenderGraphPassPlan pass = Assert.Single(graph.Compile().Passes);

        Assert.Collection(
            pass.Accesses,
            access =>
            {
                Assert.Equal(texture.Resource, access.Resource);
                Assert.Equal(GpuRenderGraphAccess.Read, access.Access);
                Assert.Equal(GpuStage.VertexShader | GpuStage.PixelShader, access.Stage);
                Assert.Equal(GpuBarrierHazards.Descriptors, access.Hazards);
            },
            access =>
            {
                Assert.Equal(buffer.Resource, access.Resource);
                Assert.Equal(GpuRenderGraphAccess.Read, access.Access);
                Assert.Equal(GpuStage.VertexShader, access.Stage);
                Assert.Equal(GpuBarrierHazards.Descriptors, access.Hazards);
            });
        Assert.Equal([3, 9], bindings.Textures.Select(static binding => binding.Index));
        Assert.Equal([7], bindings.Buffers.Select(static binding => binding.Index));
    }

    [Fact]
    public void DuplicateDescriptorIndicesAreRejectedWithinAResourceKind()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture first = graph.ImportTexture(
            "first", new GpuTextureHandle(1),
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        GpuRenderGraphTexture second = graph.ImportTexture(
            "second", new GpuTextureHandle(2),
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new GpuRenderGraphShaderBindings(textures: [new(4, first), new(4, second)]));

        Assert.Equal("source", exception.ParamName);
        Assert.Contains("unique", exception.Message, StringComparison.Ordinal);
    }
}
