using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Native.Shaders.Tests.Programs;

public sealed class NativeShaderProgramTests
{
    [Fact]
    public void LoadedProgramsHaveIndependentCodeOwnership()
    {
        using TestBackend backend = new();
        NativeShaderArtifact artifact = ShaderFixtures.Artifact();
        NativeShaderLoader loader = new(backend);
        using NativeShaderProgram first = loader.Load(ShaderFixtures.Package(artifact));
        using NativeShaderProgram second = loader.Load(ShaderFixtures.Package(artifact));

        Assert.True(MemoryMarshal.TryGetArray(first.Code.Compute!.Code, out ArraySegment<byte> firstBytes));
        firstBytes.Array![firstBytes.Offset] = 99;

        Assert.Equal(new byte[] { 7, 11, 19 }, artifact.Stages[0].Code.ToArray());
        Assert.Equal(new byte[] { 7, 11, 19 }, second.Code.Compute!.Code.ToArray());
    }

    [Fact]
    public void ProgramPreservesSelectedMetadata()
    {
        using TestBackend backend = new();
        NativeShaderInputLayout parameters = ShaderFixtures.Layout("parameter-input");
        NativeShaderInputLayout root = ShaderFixtures.Layout("root-input");
        NativeShaderArtifact artifact = new(NativeShaderTarget.Vulkan, GpuShaderCodeFormat.SpirV,
            [ShaderFixtures.Stage()], NativeShaderCapabilities.None, NativeShaderDescriptorHeapAbi.None, root, [parameters], "host-abi");

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(ShaderFixtures.Package(artifact));

        Assert.Same(root, program.RootLayout);
        Assert.Same(parameters, Assert.Single(program.ParameterLayouts));
        Assert.Equal("host-abi", program.AbiHash);
    }

    [Fact]
    public void DisposedProgramRejectsFurtherCodeAccess()
    {
        using TestBackend backend = new();
        NativeShaderProgram program = new NativeShaderLoader(backend).Load(ShaderFixtures.Package(ShaderFixtures.Artifact()));

        program.Dispose();

        ObjectDisposedException error = Assert.Throws<ObjectDisposedException>(() => _ = program.Code);
        Assert.Equal(nameof(NativeShaderProgram), error.ObjectName);
    }

    [Fact]
    public void ProgramDisposalDoesNotOwnBackendOrOtherPrograms()
    {
        using TestBackend backend = new();
        NativeShaderLoader loader = new(backend);
        NativeShaderPackage package = ShaderFixtures.Package(ShaderFixtures.Artifact());
        NativeShaderProgram disposed = loader.Load(package);
        using NativeShaderProgram live = loader.Load(package);

        disposed.Dispose();
        disposed.Dispose();

        Assert.False(backend.Disposed);
        Assert.Equal("kernel", live.Code.Compute!.EntryPoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RasterStageEntriesReachTheLowLevelProgram(bool mesh)
    {
        using TestBackend backend = new() { Capabilities = new(MeshShaders: true, AmplificationShaders: true) };
        NativeShaderStageArtifact[] stages = mesh
            ? [ShaderFixtures.Stage(GpuShaderStage.Amplification, entry: "task"), ShaderFixtures.Stage(GpuShaderStage.Mesh, entry: "mesh"),
                ShaderFixtures.Stage(GpuShaderStage.Pixel, entry: "pixel")]
            : [ShaderFixtures.Stage(GpuShaderStage.Vertex, entry: "vertex"), ShaderFixtures.Stage(GpuShaderStage.Pixel, entry: "pixel")];

        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(ShaderFixtures.Package(ShaderFixtures.Artifact(stages: stages)));

        string?[] entries = mesh
            ? [program.Code.Amplification?.EntryPoint, program.Code.Mesh?.EntryPoint, program.Code.Pixel?.EntryPoint]
            : [program.Code.Vertex?.EntryPoint, program.Code.Pixel?.EntryPoint];
        Assert.Equal(stages.Select(static stage => stage.EntryPoint), entries);
    }
}
