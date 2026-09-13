namespace Lumyte.Graphics.Native.Shaders.Offline.Tests;

public sealed class NativeShaderBuildRequestTests
{
    [Theory]
    [InlineData("Root")]
    [InlineData("RootResources")]
    [InlineData("Model|ModelResources")]
    [InlineData("A::B|A_B")]
    public async Task ConflictingGeneratedInputTypeNamesFailBeforeCompilerStart(string parameterTypes)
    {
        var request = new NativeShaderBuildRequest("Example.slang", [new("main", GpuShaderStage.Compute)],
            [new(NativeShaderTarget.Vulkan)], "Generated", parameterTypes: parameterTypes.Split('|'));
        var compiler = new NativeShaderCompiler("UnavailableSlangCompiler.exe");

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => compiler.BuildAsync(request));

        Assert.Contains("collides", error.Message);
        Assert.Equal("request", error.ParamName);
    }

    [Theory]
    [InlineData(NativeShaderTarget.DirectX12, NativeShaderDescriptorHeapAbiKind.VulkanUnified)]
    [InlineData(NativeShaderTarget.Vulkan, NativeShaderDescriptorHeapAbiKind.DirectX12)]
    [InlineData(NativeShaderTarget.Vulkan, NativeShaderDescriptorHeapAbiKind.VulkanFixed)]
    public void MismatchedHeapAbiIsRejectedBeforeCompilation(NativeShaderTarget target, NativeShaderDescriptorHeapAbiKind kind)
    {
        var buildTarget = new NativeShaderBuildTarget(target, DescriptorHeapAbi: new(kind));

        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeShaderBuildRequest("Example.slang",
            [new("main", GpuShaderStage.Compute)], [buildTarget], "Generated"));

        Assert.Equal("targets", error.ParamName);
    }

    [Fact]
    public void UnknownHeapAbiVersionIsRejectedBeforeCompilation()
    {
        var target = new NativeShaderBuildTarget(NativeShaderTarget.DirectX12, DescriptorHeapAbi: new(NativeShaderDescriptorHeapAbiKind.DirectX12, 2));

        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeShaderBuildRequest("Example.slang",
            [new("main", GpuShaderStage.Compute)], [target], "Generated"));

        Assert.Equal("targets", error.ParamName);
    }
}
