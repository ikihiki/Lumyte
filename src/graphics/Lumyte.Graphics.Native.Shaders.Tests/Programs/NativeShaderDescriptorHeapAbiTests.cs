namespace Lumyte.Graphics.Native.Shaders.Tests.Programs;

public sealed class NativeShaderDescriptorHeapAbiTests
{
    [Fact]
    public void DirectX12UsesOpaqueIndexingWithoutByteDescriptorLimits()
    {
        using TestBackend backend = new() { ShaderCodeFormat = GpuShaderCodeFormat.Dxil, Limits = new(256, new(1, 1, 1, 1)) };
        NativeShaderArtifact artifact = ShaderFixtures.Artifact(NativeShaderTarget.DirectX12, abi: NativeShaderDescriptorHeapAbi.DirectX12);

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(ShaderFixtures.Package(artifact));

        Assert.Equal(NativeShaderTarget.DirectX12, program.Target);
    }

    [Fact]
    public void UnifiedPolicyMatchesShaderSizeExpressions()
    {
        using TestBackend backend = new();

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(ShaderFixtures.Package(
            ShaderFixtures.Artifact(abi: NativeShaderDescriptorHeapAbi.VulkanUnified)));

        Assert.Equal(NativeShaderTarget.Vulkan, program.Target);
    }

    [Fact]
    public void UnifiedPolicyDoesNotRoundShaderStrideToHostPadding()
    {
        NativeGpuDescriptorLimits padded = new(64, 32, 48, 32, 32, 16, 32, 16);
        using TestBackend backend = new() { Limits = new(256, new(1, 1, 1, 1), padded) };

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new NativeShaderLoader(backend).Load(
            ShaderFixtures.Package(ShaderFixtures.Artifact(abi: NativeShaderDescriptorHeapAbi.VulkanUnified))));

        Assert.Contains("descriptor heap ABI", error.Message);
    }

    [Fact]
    public void UnifiedPolicyRequiresUnpaddedSamplerStride()
    {
        using TestBackend backend = new()
        {
            Limits = new(256, new(1, 1, 1, 1), ShaderFixtures.UnifiedLayout with { SamplerSlotStride = 64 }),
        };

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new NativeShaderLoader(backend).Load(
            ShaderFixtures.Package(ShaderFixtures.Artifact(abi: NativeShaderDescriptorHeapAbi.VulkanUnified))));

        Assert.Contains("descriptor heap ABI", error.Message);
    }

    [Fact]
    public void FixedPolicySelectsTheExactDeviceLayout()
    {
        NativeGpuDescriptorLimits current = ShaderFixtures.UnifiedLayout;
        using TestBackend backend = new();
        NativeShaderPackage package = ShaderFixtures.Package(
            ShaderFixtures.Artifact(abi: NativeShaderDescriptorHeapAbi.VulkanFixed(current with { ResourceSlotStride = 128 }), hash: "other-gpu"),
            ShaderFixtures.Artifact(abi: NativeShaderDescriptorHeapAbi.VulkanFixed(current), hash: "matching-gpu"));

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(package);

        Assert.Equal("matching-gpu", program.AbiHash);
    }

    [Fact]
    public void FixedPolicyMatchesDescriptorAlignmentAsWellAsStride()
    {
        using TestBackend backend = new();
        NativeShaderArtifact artifact = ShaderFixtures.Artifact(abi: NativeShaderDescriptorHeapAbi.VulkanFixed(
            ShaderFixtures.UnifiedLayout with { ImageDescriptorAlignment = 16 }));

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            new NativeShaderLoader(backend).Load(ShaderFixtures.Package(artifact)));

        Assert.Contains("descriptor heap ABI", error.Message);
    }

    [Theory]
    [InlineData(NativeShaderDescriptorHeapAbiKind.VulkanUnified, 2u)]
    [InlineData((NativeShaderDescriptorHeapAbiKind)99, 1u)]
    public void UnknownAbiPolicyIsNotAssumedCompatible(NativeShaderDescriptorHeapAbiKind kind, uint version)
    {
        using TestBackend backend = new();

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new NativeShaderLoader(backend).Load(
            ShaderFixtures.Package(ShaderFixtures.Artifact(abi: new(kind, version)))));

        Assert.Contains("descriptor heap ABI", error.Message);
    }

    [Fact]
    public void UnusedDescriptorsDoNotRequireAHeapAbi()
    {
        using TestBackend backend = new() { Limits = new(256, new(1, 1, 1, 1)) };

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(ShaderFixtures.Package(
            ShaderFixtures.Artifact(abi: NativeShaderDescriptorHeapAbi.None)));

        Assert.Equal("native-v1", program.AbiHash);
    }
}
