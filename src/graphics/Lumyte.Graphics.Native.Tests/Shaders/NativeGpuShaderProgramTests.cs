namespace Lumyte.Graphics.Native.Tests.Shaders;

public sealed class NativeGpuShaderProgramTests
{
    [Theory]
    [InlineData(GpuShaderStage.Compute, null, null)]
    [InlineData(GpuShaderStage.Vertex, null, null)]
    [InlineData(GpuShaderStage.Vertex, GpuShaderStage.Pixel, null)]
    [InlineData(GpuShaderStage.Mesh, null, null)]
    [InlineData(GpuShaderStage.Mesh, GpuShaderStage.Pixel, null)]
    [InlineData(GpuShaderStage.Mesh, GpuShaderStage.Amplification, null)]
    [InlineData(GpuShaderStage.Mesh, GpuShaderStage.Amplification, GpuShaderStage.Pixel)]
    public void ValidProgramShapesPreserveTheirStages(GpuShaderStage primary, GpuShaderStage? second, GpuShaderStage? third)
    {
        NativeGpuShaderCode[] shaders = new[] { (GpuShaderStage?)primary, second, third }
            .OfType<GpuShaderStage>().Select(Code).ToArray();

        var program = new NativeGpuShaderProgram(shaders);

        Assert.Equal(shaders.OrderBy(shader => shader.Stage), Stages(program).OrderBy(shader => shader.Stage));
    }

    [Theory]
    [InlineData(GpuShaderStage.Vertex)]
    [InlineData(GpuShaderStage.Pixel)]
    [InlineData(GpuShaderStage.Compute)]
    [InlineData(GpuShaderStage.Mesh)]
    [InlineData(GpuShaderStage.Amplification)]
    public void RepeatedStagesCannotDefineAProgram(GpuShaderStage stage)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeGpuShaderProgram(Code(stage), Code(stage)));

        Assert.Equal("shaders", error.ParamName);
    }

    [Theory]
    [InlineData(GpuShaderStage.Pixel)]
    [InlineData(GpuShaderStage.Amplification)]
    public void ProgramRequiresAPrimaryStage(GpuShaderStage stage)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeGpuShaderProgram(Code(stage)));

        Assert.Equal("shaders", error.ParamName);
    }

    [Theory]
    [InlineData(GpuShaderStage.Compute, GpuShaderStage.Vertex)]
    [InlineData(GpuShaderStage.Compute, GpuShaderStage.Pixel)]
    [InlineData(GpuShaderStage.Compute, GpuShaderStage.Mesh)]
    [InlineData(GpuShaderStage.Compute, GpuShaderStage.Amplification)]
    [InlineData(GpuShaderStage.Vertex, GpuShaderStage.Mesh)]
    [InlineData(GpuShaderStage.Vertex, GpuShaderStage.Amplification)]
    [InlineData(GpuShaderStage.Pixel, GpuShaderStage.Amplification)]
    public void IncompatibleStageFamiliesCannotBeMixed(GpuShaderStage first, GpuShaderStage second)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeGpuShaderProgram(Code(first), Code(second)));

        Assert.Equal("shaders", error.ParamName);
    }

    [Fact]
    public void EmptyProgramIsRejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeGpuShaderProgram());

        Assert.Equal("shaders", error.ParamName);
    }

    [Fact]
    public void UnknownStageIsRejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeGpuShaderProgram(Code((GpuShaderStage)123)));

        Assert.Equal("shaders", error.ParamName);
    }

    [Fact]
    public void NullShaderCollectionIsRejected()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => new NativeGpuShaderProgram(null!));

        Assert.Equal("shaders", error.ParamName);
    }

    [Fact]
    public void NullShaderCannotDefineAStage()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeGpuShaderProgram([null!]));

        Assert.Equal("shaders", error.ParamName);
    }

    [Fact]
    public void MutatingTheInputCollectionDoesNotChangeTheProgramShape()
    {
        NativeGpuShaderCode vertex = Code(GpuShaderStage.Vertex);
        NativeGpuShaderCode pixel = Code(GpuShaderStage.Pixel);
        NativeGpuShaderCode[] shaders = [vertex, pixel];
        var program = new NativeGpuShaderProgram(shaders);

        shaders[0] = Code(GpuShaderStage.Compute);
        shaders[1] = Code(GpuShaderStage.Mesh);

        Assert.Equal(new[] { vertex, pixel }, Stages(program));
    }

    [Fact]
    public void ProgramDoesNotInterpretOrRejectNativeShaderBytes()
    {
        byte[] arbitraryCode = [91, 73, 51];
        var shader = new NativeGpuShaderCode { Stage = GpuShaderStage.Compute, Code = arbitraryCode.AsMemory(1), EntryPoint = "kernel" };

        var program = new NativeGpuShaderProgram(shader);

        NativeGpuShaderCode actual = Assert.IsType<NativeGpuShaderCode>(program.Compute);
        Assert.Same(shader, actual);
        Assert.Equal(arbitraryCode.AsMemory(1), actual.Code);
    }

    [Fact]
    public void ShaderEntryDefaultsToMain()
    {
        NativeGpuShaderCode shader = Code(GpuShaderStage.Compute);

        Assert.Equal("main", shader.EntryPoint);
    }

    private static NativeGpuShaderCode Code(GpuShaderStage stage) => new() { Stage = stage, Code = ReadOnlyMemory<byte>.Empty };

    private static IEnumerable<NativeGpuShaderCode> Stages(NativeGpuShaderProgram program)
        => new[] { program.Vertex, program.Pixel, program.Compute, program.Mesh, program.Amplification }.OfType<NativeGpuShaderCode>();
}
