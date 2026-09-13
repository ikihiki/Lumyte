namespace Lumyte.Graphics.Native.Shaders.Tests.Programs;

public sealed class NativeShaderLoaderTests
{
    [Theory]
    [InlineData(NativeShaderTarget.DirectX12, GpuShaderCodeFormat.Dxil)]
    [InlineData(NativeShaderTarget.Vulkan, GpuShaderCodeFormat.SpirV)]
    public void LoaderSelectsTheBackendCodeTarget(NativeShaderTarget target, GpuShaderCodeFormat format)
    {
        using TestBackend backend = new() { ShaderCodeFormat = format };
        NativeShaderPackage package = ShaderFixtures.Package(
            ShaderFixtures.Artifact(NativeShaderTarget.DirectX12, hash: "dxil-host"),
            ShaderFixtures.Artifact(NativeShaderTarget.Vulkan, hash: "spirv-host"));

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(package);

        Assert.Equal(target, program.Target);
        Assert.Equal(target == NativeShaderTarget.DirectX12 ? "dxil-host" : "spirv-host", program.AbiHash);
    }

    [Fact]
    public void SelectionRequiresBothTargetAndCodeFormat()
    {
        using TestBackend backend = new();
        NativeShaderArtifact wrong = new(NativeShaderTarget.Vulkan, GpuShaderCodeFormat.Dxil,
            [ShaderFixtures.Stage()], NativeShaderCapabilities.None, NativeShaderDescriptorHeapAbi.None,
            ShaderFixtures.Layout(), [], "wrong-format");

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            new NativeShaderLoader(backend).Load(ShaderFixtures.Package(wrong)));

        Assert.Contains("no artifact", error.Message);
    }

    [Theory]
    [InlineData(NativeShaderCapabilities.RawShaderPointers)]
    [InlineData(NativeShaderCapabilities.BufferDescriptors)]
    [InlineData(NativeShaderCapabilities.MeshShaders)]
    [InlineData(NativeShaderCapabilities.AmplificationShaders)]
    public void MissingRequiredShaderFeatureRejectsArtifact(NativeShaderCapabilities required)
    {
        using TestBackend backend = new();

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new NativeShaderLoader(backend)
            .Load(ShaderFixtures.Package(ShaderFixtures.Artifact(required: required))));

        Assert.Contains("enabled shader capabilities", error.Message);
    }

    [Fact]
    public void EnabledFeaturesMayBeASupersetOfArtifactRequirements()
    {
        using TestBackend backend = new() { Capabilities = new(RawShaderPointers: true, BufferDescriptors: true, MeshShaders: true) };
        NativeShaderArtifact artifact = ShaderFixtures.Artifact(required: NativeShaderCapabilities.RawShaderPointers);

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(ShaderFixtures.Package(artifact));

        Assert.Equal("native-v1", program.AbiHash);
    }

    [Fact]
    public void UnknownRequiredFeatureIsNotSilentlyIgnored()
    {
        using TestBackend backend = new() { Capabilities = new(true, true, true, true, true) };

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new NativeShaderLoader(backend)
            .Load(ShaderFixtures.Package(ShaderFixtures.Artifact(required: (NativeShaderCapabilities)128))));

        Assert.Contains("no artifact", error.Message);
    }

    [Fact]
    public void MeshShapeCannotBypassFeatureSelectionWithEmptyRequirements()
    {
        using TestBackend backend = new();
        NativeShaderArtifact mesh = ShaderFixtures.Artifact(stages: [ShaderFixtures.Stage(GpuShaderStage.Mesh)]);

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            new NativeShaderLoader(backend).Load(ShaderFixtures.Package(mesh)));

        Assert.Contains("no artifact", error.Message);
    }

    [Fact]
    public void MultipleCompatibleArtifactsAreAnError()
    {
        using TestBackend backend = new() { Capabilities = new(BufferDescriptors: true) };
        NativeShaderPackage package = ShaderFixtures.Package(ShaderFixtures.Artifact(),
            ShaderFixtures.Artifact(required: NativeShaderCapabilities.BufferDescriptors, hash: "other-abi"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new NativeShaderLoader(backend).Load(package));

        Assert.Contains("multiple compatible artifacts", error.Message);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    public void UnsupportedPackageVersionIsRejected(uint version)
    {
        using TestBackend backend = new();

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            new NativeShaderLoader(backend).Load(new(version, [ShaderFixtures.Artifact()])));

        Assert.Contains($"version {version}", error.Message);
    }

    [Fact]
    public void ExpectedHostAbiIsMatchedOrdinally()
    {
        using TestBackend backend = new();
        NativeShaderPackage package = ShaderFixtures.Package(ShaderFixtures.Artifact(hash: "Host-ABI"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new NativeShaderLoader(backend).Load(package, "host-abi"));

        Assert.Contains("host ABI hash", error.Message);
    }

    [Fact]
    public void MatchingHostAbiLoadsTheSelectedArtifact()
    {
        using TestBackend backend = new();

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(
            ShaderFixtures.Package(ShaderFixtures.Artifact(hash: "Host-ABI")), "Host-ABI");

        Assert.Equal("Host-ABI", program.AbiHash);
    }

    [Fact]
    public void LoaderPreservesNativeCodeWithoutShaderValidation()
    {
        using TestBackend backend = new() { Limits = new(0, new(0, 0, 0, 0)) };
        NativeShaderArtifact artifact = ShaderFixtures.Artifact(stages: [ShaderFixtures.Stage(code: [0xFF], entry: "native-entry")]);

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(ShaderFixtures.Package(artifact));
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(program.Code);

        Assert.Same(program.Code, backend.CapturedProgram);
        Assert.Equal("native-entry", backend.CapturedProgram!.Compute!.EntryPoint);
        Assert.Equal(new byte[] { 0xFF }, backend.CapturedProgram.Compute.Code.ToArray());
        backend.DestroyComputePipeline(pipeline);
    }

    [Fact]
    public void UnsupportedBackendCodeFormatIsReported()
    {
        using TestBackend backend = new() { ShaderCodeFormat = GpuShaderCodeFormat.Wgsl };

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            new NativeShaderLoader(backend).Load(ShaderFixtures.Package(ShaderFixtures.Artifact())));

        Assert.Contains("not a Native shader target", error.Message);
    }
}
