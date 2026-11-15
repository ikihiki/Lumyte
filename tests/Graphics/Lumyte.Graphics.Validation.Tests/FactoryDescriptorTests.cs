using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Exercises public factory ownership and immutable descriptor translation.</summary>
public sealed class FactoryDescriptorTests
{
    /// <summary>Graphics factories unwrap both modules and retain the caller descriptor.</summary>
    /// <param name="fragment">Whether a fragment module is present.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GraphicsDescriptorUsesNativeModulesOnlyAtBackend(bool fragment)
    {
        var backend = new FactoryBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShader vertex = device.CreateShader(Artifact());
        FactoryShader nativeVertex = backend.Shader!;
        using IGraphicsShader pixel = device.CreateShader(Artifact());
        FactoryShader nativePixel = backend.Shader!;
        var desc = new GraphicsPipelineDesc
        {
            VertexShader = vertex,
            FragmentShader = fragment ? pixel : null,
            Label = "factory graphics",
            AlphaToCoverageEnable = true,
            Optimization = PipelineOptimizationMode.FullSpecialization,
            TopologyClass = PrimitiveTopologyClass.Line,
        };
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(desc);
        Assert.Same(desc, pipeline.Desc);
        Assert.Equal(desc with { VertexShader = nativeVertex, FragmentShader = fragment ? nativePixel : null }, backend.GraphicsDesc);
        Assert.Same(vertex, desc.VertexShader);
        Assert.Same(vertex.Artifact, nativeVertex.Artifact);
    }

    /// <summary>Compute descriptors preserve validation identity while native creation sees raw modules.</summary>
    [Fact]
    public void ComputeDescriptorRetainsWrapperIdentity()
    {
        var backend = new FactoryBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShader shader = device.CreateShader(Artifact());
        var desc = new ComputePipelineDesc { ComputeShader = shader, Label = "factory compute" };
        IGraphicsComputePipeline pipeline = device.CreateComputePipeline(desc);
        Assert.Same(desc, pipeline.Desc);
        Assert.Equal(desc with { ComputeShader = backend.Shader! }, backend.ComputeDesc);
        pipeline.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pipeline.Desc);
    }

    /// <summary>Raw, foreign and disposed modules never reach either pipeline factory.</summary>
    /// <param name="kind">The invalid ownership case.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidModuleIsRejectedBeforeBackendCreation(int kind)
    {
        var backend = new FactoryBackend();
        using var device = new ValidationDevice(backend);
        using var foreign = new ValidationDevice(new FactoryBackend());
        using IGraphicsShader valid = device.CreateShader(Artifact());
        using IGraphicsShader invalid = kind switch
        {
            0 => new FactoryShader(Artifact()),
            1 => foreign.CreateShader(Artifact()),
            _ => device.CreateShader(Artifact()),
        };
        if (kind == 2)
        {
            invalid.Dispose();
        }

        if (kind == 2)
        {
            Assert.Throws<ObjectDisposedException>(() => device.CreateComputePipeline(new() { ComputeShader = invalid }));
            Assert.Throws<ObjectDisposedException>(() => device.CreateGraphicsPipeline(new() { VertexShader = valid, FragmentShader = invalid }));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => device.CreateComputePipeline(new() { ComputeShader = invalid }));
            Assert.Throws<ArgumentException>(() => device.CreateGraphicsPipeline(new() { VertexShader = valid, FragmentShader = invalid }));
        }

        Assert.Null(backend.ComputeDesc);
        Assert.Null(backend.GraphicsDesc);
    }

    /// <summary>A duplicate native allocation is not disposed or allowed to corrupt its original wrapper.</summary>
    [Fact]
    public void DuplicateShaderFactoryPreservesOriginalOwnership()
    {
        var backend = new FactoryBackend { ReuseShader = true };
        using var device = new ValidationDevice(backend);
        IGraphicsShader shader = device.CreateShader(Artifact());
        FactoryShader raw = backend.Shader!;
        Assert.Throws<InvalidOperationException>(() => device.CreateShader(Artifact()));
        Assert.Equal(0, raw.DisposeCount);
        Assert.Same(raw.Artifact, shader.Artifact);
        shader.Dispose();
        shader.Dispose();
        Assert.Equal(1, raw.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => shader.Artifact);
    }

    /// <summary>Backend errors propagate unchanged and leave no registered child.</summary>
    [Fact]
    public void BackendFactoryFailurePreservesOriginalException()
    {
        var failure = new InvalidOperationException("native factory failure");
        var backend = new FactoryBackend { Failure = failure };
        using var device = new ValidationDevice(backend);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => device.CreateShader(Artifact())));
        backend.Failure = null;
        using IGraphicsShader shader = device.CreateShader(Artifact());
        Assert.NotNull(shader.Artifact);
    }

    /// <summary>Views retain wrapper identity, forward exact ranges, and prevent premature texture release.</summary>
    [Fact]
    public void TextureViewDescriptorAndParentLifetimeArePreserved()
    {
        var backend = new FactoryBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsTexture texture = device.CreateTexture(new()
        {
            Width = 32,
            Height = 16,
            MipLevels = 3,
            ArrayLayers = 2,
            Format = TextureFormat.Rgba8Srgb,
            Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
        });
        var desc = new TextureViewDesc { BaseMipLevel = 1, MipLevelCount = 1, BaseArrayLayer = 0, ArrayLayerCount = 1 };
        IGraphicsTextureView view = texture.CreateView(desc);
        FactoryTexture raw = backend.Texture!;
        Assert.Same(desc, raw.LastViewDesc);
        Assert.Same(texture, view.Texture);
        Assert.Same(raw.View!.Info, view.Info);
        Assert.Equal(32u, texture.Width);
        Assert.Equal(16u, texture.Height);
        Assert.Equal(3u, texture.MipLevels);
        Assert.Equal(2u, texture.ArrayLayers);
        Assert.Equal(TextureFormat.Rgba8Srgb, texture.Format);
        Assert.Equal(TextureUsage.Sampled | TextureUsage.CopyDestination, texture.Usage);
        Assert.Equal((16u, 8u), texture.GetMipSize(1));
        Assert.Throws<InvalidOperationException>(texture.Dispose);
        Assert.Throws<InvalidOperationException>(() => texture.CreateView(desc));
        Assert.Equal(0, raw.View.DisposeCount);
        Assert.Same(texture, view.Texture);
        view.Dispose();
        Assert.Equal(1, raw.View.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => view.Info);
        texture.Dispose();
        Assert.Equal(1, raw.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => texture.CreateView());
    }

    /// <summary>A domain retired during native creation cannot leak the new allocation.</summary>
    [Fact]
    public void FailedWrapperConstructionDisposesNewNativeResource()
    {
        var backend = new FactoryBackend();
        using var device = new ValidationDevice(backend);
        backend.BeforeShaderReturn = device.Dispose;
        Assert.Throws<ObjectDisposedException>(() => device.CreateShader(Artifact()));
        Assert.Equal(1, backend.Shader!.DisposeCount);
        device.Dispose();
    }

    private static ShaderArtifact Artifact() => ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "factory", [1], "{}");
}
