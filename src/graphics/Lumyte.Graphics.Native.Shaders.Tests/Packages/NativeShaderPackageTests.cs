namespace Lumyte.Graphics.Native.Shaders.Tests.Packages;

public sealed class NativeShaderPackageTests
{
    [Fact]
    public void PackageSnapshotsArtifactCollection()
    {
        NativeShaderArtifact expected = ShaderFixtures.Artifact();
        NativeShaderArtifact[] input = [expected];
        NativeShaderPackage package = ShaderFixtures.Package(input);

        input[0] = ShaderFixtures.Artifact(hash: "replacement");

        Assert.Same(expected, Assert.Single(package.Artifacts));
    }

    [Fact]
    public void PackageDoesNotTreatMeshAndVertexAsVariantsOfOneProgram()
    {
        NativeShaderArtifact mesh = ShaderFixtures.Artifact(stages: [ShaderFixtures.Stage(GpuShaderStage.Mesh)]);
        NativeShaderArtifact vertex = ShaderFixtures.Artifact(stages: [ShaderFixtures.Stage(GpuShaderStage.Vertex)]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => ShaderFixtures.Package(mesh, vertex));

        Assert.Equal("artifacts", error.ParamName);
    }

    [Fact]
    public void StageOwnsInputBytes()
    {
        byte[] input = [5, 7, 11];
        NativeShaderStageArtifact stage = ShaderFixtures.Stage(code: input);

        input[1] = 99;
        Assert.Equal(new byte[] { 5, 7, 11 }, stage.Code.ToArray());
    }

    [Fact]
    public void ArtifactSnapshotsStageAndParameterCollections()
    {
        NativeShaderStageArtifact expectedStage = ShaderFixtures.Stage();
        NativeShaderInputLayout expectedLayout = ShaderFixtures.Layout("parameters");
        NativeShaderStageArtifact[] stages = [expectedStage];
        NativeShaderInputLayout[] parameters = [expectedLayout];
        NativeShaderArtifact artifact = new(NativeShaderTarget.Vulkan, GpuShaderCodeFormat.SpirV, stages,
            NativeShaderCapabilities.None, NativeShaderDescriptorHeapAbi.None, ShaderFixtures.Layout(), parameters, "abi-v1");

        stages[0] = ShaderFixtures.Stage(GpuShaderStage.Vertex);
        parameters[0] = ShaderFixtures.Layout("changed");

        Assert.Same(expectedStage, Assert.Single(artifact.Stages));
        Assert.Same(expectedLayout, Assert.Single(artifact.ParameterLayouts));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshStagesContributeTheirRequiredCapabilities(bool amplification)
    {
        NativeShaderStageArtifact[] stages = amplification
            ? [ShaderFixtures.Stage(GpuShaderStage.Mesh), ShaderFixtures.Stage(GpuShaderStage.Amplification)]
            : [ShaderFixtures.Stage(GpuShaderStage.Mesh)];

        NativeShaderArtifact artifact = ShaderFixtures.Artifact(stages: stages);

        Assert.Equal(NativeShaderCapabilities.MeshShaders |
            (amplification ? NativeShaderCapabilities.AmplificationShaders : NativeShaderCapabilities.None), artifact.RequiredCapabilities);
    }

    [Fact]
    public void ArtifactRejectsAmbiguousStageFamilies()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => ShaderFixtures.Artifact(
            stages: [ShaderFixtures.Stage(GpuShaderStage.Compute), ShaderFixtures.Stage(GpuShaderStage.Vertex)]));

        Assert.Equal("stages", error.ParamName);
    }

    [Fact]
    public void ArtifactRejectsDuplicateParameterLayoutIdentifiers()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeShaderArtifact(
            NativeShaderTarget.Vulkan, GpuShaderCodeFormat.SpirV, [ShaderFixtures.Stage()], NativeShaderCapabilities.None,
            NativeShaderDescriptorHeapAbi.None, ShaderFixtures.Layout(), [ShaderFixtures.Layout("same"), ShaderFixtures.Layout("same")], "abi-v1"));

        Assert.Equal("parameterLayouts", error.ParamName);
    }
}
